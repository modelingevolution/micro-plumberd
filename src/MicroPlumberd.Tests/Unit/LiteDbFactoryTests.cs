using FluentAssertions;
using LiteDB;
using MicroPlumberd.Tests.App.CinemaDomain;
using MicroPlumberd.Tests.App.Infrastructure;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// HowToSubscribeRealModel counted reservations of EARLIER test runs: LiteDbFactory "freshened" ./test_{n}.db by deleting
/// it, but LiteDB's journal ./test_{n}-log.db survived and was replayed on the next open ("found 2", "found 6", 10 — growing
/// run on run). Two rows, because a file factory shows differently on the two kinds of bin (rev-reach):
/// - CLEAN bin (CI, always): the file factory CREATES test_1.db — the no-file row is red; StartsEmpty is green (nothing to replay).
/// - DIRTY bin (a dev's second run): it re-opens existing names — no new path, but the files are MODIFIED (the no-file row,
///   by length + write time, is red). StartsEmpty is red only where a NOT-yet-checkpointed journal was left behind (as the
///   HowTo rows left one: "found 11"); in this class's own runs it stayed green. The no-file row is the guard on both bins.
/// A path-set-only form of the no-file row was blind on the dirty bin (names are reused); this one sees creation AND change.
/// </summary>
public class LiteDbFactoryTests
{
    [Fact]
    public void AnAssertionDb_CreatesOrTouchesNoFile_ForALaterRunToReplay()
    {
        var dir = Directory.GetCurrentDirectory();
        var before = Snapshot(dir);

        using (var db = LiteDbFactory.Get())
            db.Reservations().Insert(new Reservation { MovieName = "written", RoomName = "1" });

        var after = Snapshot(dir);
        var created = after.Keys.Except(before.Keys).ToList();
        var modified = after.Where(f => before.TryGetValue(f.Key, out var was) && was != f.Value).Select(f => f.Key).ToList();
        created.Concat(modified).Should().BeEmpty(
            "an assertion db must leave nothing on disk — a file, or its journal, is replayed into the next run's model");
    }

    private static Dictionary<string, (long Length, DateTime Written)> Snapshot(string dir) =>
        Directory.GetFiles(dir, "*.db").ToDictionary(f => f, f => { var i = new FileInfo(f); return (i.Length, i.LastWriteTimeUtc); });

    [Fact]
    public void EachAssertionDb_StartsEmpty()
    {
        using (var first = LiteDbFactory.Get())
            first.Reservations().Insert(new Reservation { MovieName = "written", RoomName = "1" });

        using var next = LiteDbFactory.Get();
        next.Reservations().Count().Should().Be(0);
    }
}
