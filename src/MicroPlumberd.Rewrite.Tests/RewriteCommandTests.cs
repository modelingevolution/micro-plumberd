using Docker.DotNet;
using FluentAssertions;
using MicroPlumberd.Rewrite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MicroPlumberd.Rewrite.Tests;

/// <summary>Command-level refusals that are decided before docker is consulted at all.</summary>
public class RewriteCommandTests
{
    private static RewriteOptions Options() => new()
    {
        // A container that does not exist. If the tool reached docker, the answer would be exit 3
        // "No such container" — so the assertions below double as proof that it did not.
        Container = $"mp-rewrite-test-absent-{Guid.NewGuid():N}",
        Yes = true,
        Output = new StringWriter()
    };

    private static async Task<RewriteReport> RunAsync(RewriteOptions options)
    {
        using var docker = new DockerClientConfiguration().CreateClient();
        return await RewriteCommand.RunAsync(options, docker, NullLoggerFactory.Instance);
    }

    [Theory]
    [InlineData(RewriteMode.Rewrite)]
    [InlineData(RewriteMode.Status)]
    [InlineData(RewriteMode.Rollback)]
    public async Task force_volume_copy_is_refused_with_exit_2_before_any_docker_call(RewriteMode mode)
    {
        // The ruling this pins: a flag the tool accepts and ignores is a trap. An operator on a named-volume
        // host would pass it, be refused for the volume anyway, and have no way to tell the flag never helped.
        var report = await RunAsync(Options() with { ForceVolumeCopy = true, Mode = mode });

        // Exit 2: an ARGUMENT this version does not support. A named-volume STORE, without the flag, is a
        // guard on the store's state and exits 1 — the test below pins that, so the pair cannot drift.
        report.Code.Should().Be(ExitCode.ScriptError);
        report.Headline.Should().Contain("--force-volume-copy",
            "the refusal has to name the flag the operator typed")
            .And.Contain("not implemented in this version")
            .And.Contain("bind mount", "and say what to do instead");

        report.Headline.Should().NotContain("No such container",
            "reaching docker first would have produced this instead — the refusal must come before any call");
        report.Container.Should().BeNull("nothing was inspected");
    }

    [Fact]
    public void A_store_on_a_named_volume_is_refused_with_exit_1()
    {
        var onAVolume = Container(new DataLocation(null, "/var/lib/kurrentdb", IsBind: false, VolumeName: "esdata"));

        var act = () => RewriteCommand.RequireSwappableData(onAVolume, Options());

        act.Should().Throw<RewriteRefusedException>()
            .Where(e => e.Code == ExitCode.GuardRefusal,
                "the STORE being unswappable is a guard on its state — exit 2 is for an unsupported argument")
            .WithMessage("*esdata*", "the operator has to be told which volume")
            .WithMessage("*bind mount*", "and what to do instead");
    }

    [Fact]
    public void A_store_on_a_bind_mount_is_not_refused()
    {
        // The control: without it, "named volumes are refused" would also pass if everything were refused.
        var onABind = Container(new DataLocation("/srv/store/data", "/var/lib/kurrentdb", IsBind: true, VolumeName: null));

        var act = () => RewriteCommand.RequireSwappableData(onABind, Options());

        act.Should().NotThrow();
    }

    private static StoreContainer Container(DataLocation data) => new()
    {
        Id = "abc123", Name = "some-store", Image = "kurrentdb:latest", Env = [], Data = data, Running = true
    };

    [Fact]
    public async Task Without_the_flag_the_same_command_line_gets_as_far_as_docker()
    {
        // The control for the test above: without --force-volume-copy this exact invocation DOES reach docker.
        // Without it, "the refusal came first" would be unfalsifiable — the tool might simply never call docker.
        var report = await RunAsync(Options());

        report.Code.Should().Be(ExitCode.DockerUnavailable);
        report.Headline.Should().Contain("No such container");
    }
}
