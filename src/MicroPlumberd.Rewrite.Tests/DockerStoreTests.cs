using Docker.DotNet.Models;
using FluentAssertions;
using MicroPlumberd.Rewrite;
using Xunit;

namespace MicroPlumberd.Rewrite.Tests;

/// <summary>UT-08 — resolving which mount holds the store, and refusing what cannot be swapped.</summary>
public class DockerStoreTests
{
    private static MountPoint Bind(string source, string destination) =>
        new() { Type = "bind", Source = source, Destination = destination };

    private static MountPoint Volume(string name, string destination) =>
        new() { Type = "volume", Name = name, Source = $"/var/lib/docker/volumes/{name}/_data", Destination = destination };

    [Fact]
    public void UT08_The_env_configured_data_directory_wins_over_the_defaults()
    {
        // The trap this guards: a container told KURRENTDB_DB=/data that ALSO mounts something at the default
        // path. Picking the default would rename the wrong directory away — and the backup would then be a
        // backup of the wrong thing, which is the one mistake in this tool that cannot be undone by hand.
        var location = DockerStore.ResolveDataLocation(
            ["PATH=/usr/bin", "KURRENTDB_DB=/data"],
            [Bind("/srv/other/data", "/var/lib/kurrentdb"), Bind("/srv/store/data", "/data")]);

        location.StoreDir.Should().Be("/srv/store/data");
        location.Destination.Should().Be("/data");
        location.IsBind.Should().BeTrue();
    }

    [Fact]
    public void UT08_The_legacy_EVENTSTORE_DB_setting_is_honoured_too()
    {
        var location = DockerStore.ResolveDataLocation(
            ["EVENTSTORE_DB=/var/lib/eventstore"],
            [Bind("/srv/legacy/data", "/var/lib/eventstore")]);

        location.StoreDir.Should().Be("/srv/legacy/data");
    }

    [Fact]
    public void UT08_With_no_env_setting_the_default_kurrentdb_path_is_used()
    {
        var location = DockerStore.ResolveDataLocation(
            ["PATH=/usr/bin"],
            [Bind("/var/docker/data/eventstore", "/var/lib/kurrentdb")]);

        location.StoreDir.Should().Be("/var/docker/data/eventstore");
        location.Parent.Should().Be("/var/docker/data");
        location.Name.Should().Be("eventstore",
            "backups are named after the data directory, which is not always called 'data'");
    }

    [Fact]
    public void UT08_A_named_volume_is_reported_as_such_and_carries_no_host_path()
    {
        var location = DockerStore.ResolveDataLocation([], [Volume("esdata", "/var/lib/kurrentdb")]);

        location.IsBind.Should().BeFalse();
        location.VolumeName.Should().Be("esdata");
        location.StoreDir.Should().BeNull("a named volume has no host path this tool may rename");
    }

    [Fact]
    public void UT08_A_container_with_no_mount_at_its_data_directory_is_refused()
    {
        var act = () => DockerStore.ResolveDataLocation([], [Bind("/srv/logs", "/var/log/kurrentdb")]);

        act.Should().Throw<RewriteRefusedException>()
            .Where(e => e.Code == ExitCode.GuardRefusal)
            .WithMessage("*no mount at its data directory*");
    }

    // ------------------------------------------------------------------ metrics parsing

    [Fact]
    public void An_absent_connection_metric_reads_as_cannot_tell_not_as_zero()
    {
        // If a future KurrentDB renames the counter, "absent" must not silently become "no clients connected"
        // — that would turn the guard off without anyone noticing, which is the failure it exists to prevent.
        DockerStore.ReadMetric("some_other_metric{a=\"b\"} 3 1788\n", DockerStore.OpenGrpcCallsMetric)
            .Should().Be(-1);
    }

    [Fact]
    public void The_open_grpc_call_count_is_read_from_a_labelled_prometheus_sample()
    {
        const string body = """
            # HELP kurrentdb_current_incoming_grpc_calls calls
            kurrentdb_current_incoming_grpc_calls{otel_scope_name="KurrentDB.Core",otel_scope_version="1.0.0"} 2 1788792975770
            kurrentdb_kestrel_connections{otel_scope_name="KurrentDB.Core",otel_scope_version="1.0.0"} 3 1788792975770
            """;

        DockerStore.ReadMetric(body, DockerStore.OpenGrpcCallsMetric).Should().Be(2);
        DockerStore.ReadMetric(body, DockerStore.KestrelConnectionsMetric).Should().Be(3);
    }

    [Fact]
    public void A_longer_metric_whose_name_merely_starts_the_same_is_not_mistaken_for_it()
    {
        // kurrentdb_incoming_grpc_calls_total sits right next to the gauge in a real /metrics body and starts
        // with the same characters; matching it would report a lifetime TOTAL as a live count and refuse for ever.
        const string body = "kurrentdb_current_incoming_grpc_calls_total{kind=\"total\"} 97 1788\n";

        DockerStore.ReadMetric(body, DockerStore.OpenGrpcCallsMetric).Should().Be(-1);
    }

    // ------------------------------------------------------------------ state paths outside the mount

    [Theory]
    [InlineData("KURRENTDB_INDEX")]
    [InlineData("KURRENTDB_DB")]
    [InlineData("EVENTSTORE_INDEX")]
    public void A_state_path_outside_the_swapped_mount_is_refused_and_the_setting_is_named(string key)
    {
        // The index is the dangerous one: the scratch store builds an index for the NEW log, but if the
        // original container keeps its index somewhere this tool does not swap, it comes back on new data with
        // a stale index — silent, and the same shape as the in-memory-database case.
        var data = new DataLocation("/srv/store/data", "/var/lib/kurrentdb", IsBind: true, VolumeName: null);

        var (refusals, _) = DockerStore.CheckStatePathsInsideMount([$"{key}=/var/lib/kurrentdb-index"], data);

        refusals.Should().ContainSingle().Which.Should().Contain(key).And.Contain("/var/lib/kurrentdb");
    }

    [Fact]
    public void A_state_path_inside_the_mount_is_accepted()
    {
        // The control: the gate must not refuse the ordinary layout, or it refuses every run.
        var data = new DataLocation("/srv/store/data", "/var/lib/kurrentdb", IsBind: true, VolumeName: null);

        var (refusals, notes) = DockerStore.CheckStatePathsInsideMount(
            ["KURRENTDB_DB=/var/lib/kurrentdb", "KURRENTDB_INDEX=/var/lib/kurrentdb/index"], data);

        refusals.Should().BeEmpty();
        notes.Should().BeEmpty();
    }

    [Fact]
    public void A_log_path_outside_the_mount_is_reported_but_never_refused()
    {
        // Logs are not state. KurrentDB's own default log path is outside the data directory, so refusing on
        // it would block a legitimate repair — with no override — for no safety gain.
        var data = new DataLocation("/srv/store/data", "/var/lib/kurrentdb", IsBind: true, VolumeName: null);

        var (refusals, notes) = DockerStore.CheckStatePathsInsideMount(["KURRENTDB_LOG=/var/log/kurrentdb"], data);

        refusals.Should().BeEmpty("a log path cannot make a rewrite incorrect");
        notes.Should().ContainSingle().Which.Should().Contain("KURRENTDB_LOG");
    }

    [Theory]
    [InlineData("/var/lib/kurrentdb", "/var/lib/kurrentdb", true)]
    [InlineData("/var/lib/kurrentdb/index", "/var/lib/kurrentdb", true)]
    [InlineData("/var/lib/kurrentdb-index", "/var/lib/kurrentdb", false)]
    [InlineData("/var/lib/other", "/var/lib/kurrentdb", false)]
    public void Containment_is_by_path_SEGMENT_not_by_string_prefix(string path, string root, bool inside)
    {
        // "/var/lib/kurrentdb-index".StartsWith("/var/lib/kurrentdb") is true and would silently accept a
        // sibling directory that is not swapped at all.
        DockerStore.IsInside(path, root).Should().Be(inside);
    }

    // ------------------------------------------------------------------ scratch environment

    [Fact]
    public void The_scratch_store_forces_projections_on_and_never_inherits_an_in_memory_database()
    {
        var env = DockerStore.BuildScratchEnv(
            ["PATH=/usr/bin", "KURRENTDB_MEM_DB=true", "KURRENTDB_RUN_PROJECTIONS=None", "KURRENTDB_CLUSTER_SIZE=1"]);

        env.Should().Contain("KURRENTDB_RUN_PROJECTIONS=All")
            .And.Contain("KURRENTDB_START_STANDARD_PROJECTIONS=true")
            .And.Contain("KURRENTDB_INSECURE=true")
            .And.Contain("KURRENTDB_MEM_DB=false",
                "inheriting an in-memory database would copy the whole store into nothing and then swap it in");
        env.Should().Contain("KURRENTDB_CLUSTER_SIZE=1", "unrelated settings are carried over verbatim");
        env.Should().NotContain("KURRENTDB_RUN_PROJECTIONS=None")
            .And.NotContain("KURRENTDB_MEM_DB=true", "a forced key REPLACES the old one rather than shadowing it");
    }
}
