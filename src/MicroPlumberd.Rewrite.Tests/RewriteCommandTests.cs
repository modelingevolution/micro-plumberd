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
    public async Task Without_the_flag_the_same_command_line_gets_as_far_as_docker()
    {
        // The control for the test above: without --force-volume-copy this exact invocation DOES reach docker.
        // Without it, "the refusal came first" would be unfalsifiable — the tool might simply never call docker.
        var report = await RunAsync(Options());

        report.Code.Should().Be(ExitCode.DockerUnavailable);
        report.Headline.Should().Contain("No such container");
    }
}
