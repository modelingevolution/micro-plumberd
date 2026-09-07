using System.Text.Json.Nodes;
using MicroPlumberd.Migration;

namespace MicroPlumberd.Migration.Tests;

/// <summary>Inline migrations used by the unit tests. Each declares a small, deterministic rule set.</summary>
internal static class TestMigrations
{
    public sealed class DropOneStream : Migration
    {
        public override string Id => "0001_drop_one";
        public override void Migrate(IMigrationBuilder b) => b.DropStream("Foo-1");
    }

    public sealed class DropManyStreams : Migration
    {
        public override string Id => "0002_drop_many";
        public override void Migrate(IMigrationBuilder b) => b.DropStreams("Foo-1", "Foo-2");
    }

    public sealed class DropByStreamPredicate : Migration
    {
        public override string Id => "0003_drop_pred";
        public override void Migrate(IMigrationBuilder b) =>
            b.DropStream(s => s.StartsWith("Temp-", StringComparison.Ordinal));
    }

    public sealed class DropByEventPredicate : Migration
    {
        public override string Id => "0004_drop_event";
        public override void Migrate(IMigrationBuilder b) =>
            b.DropEvent(e => e.Type == "Noise");
    }

    public sealed class RenameTheType : Migration
    {
        public override string Id => "0005_rename_type";
        public override void Migrate(IMigrationBuilder b) => b.RenameType("OldName", "NewName");
    }

    public sealed class TransformData : Migration
    {
        public override string Id => "0006_transform";
        public override void Migrate(IMigrationBuilder b) =>
            b.TransformJson("Widget", (data, meta) =>
            {
                data["added"] = "yes";
                meta["touched"] = true;
            });
    }

    public sealed class RenameTheStream : Migration
    {
        public override string Id => "0007_rename_stream";
        public override void Migrate(IMigrationBuilder b) => b.RenameStream("Old-1", "New-1");
    }

    /// <summary>Rename type then transform the NEW type — composition across ops in declared order.</summary>
    public sealed class RenameThenTransform : Migration
    {
        public override string Id => "0008_rename_then_transform";
        public override void Migrate(IMigrationBuilder b)
        {
            b.RenameType("V1", "V2");
            b.TransformJson("V2", (data, _) => data["v2"] = true);
        }
    }

    /// <summary>Rename a stream then drop the NEW stream name — composition of stream ops.</summary>
    public sealed class RenameStreamThenDrop : Migration
    {
        public override string Id => "0009_rename_stream_then_drop";
        public override void Migrate(IMigrationBuilder b)
        {
            b.RenameStream("A-1", "B-1");
            b.DropStream("B-1");
        }
    }
}
