using FluentAssertions;
using LiteDB;
using MicroPlumberd.Tests.App.CinemaDomain;
using MicroPlumberd.Tests.App.Infrastructure;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// HowToSubscribeRealModel counted reservations of EARLIER test runs: LiteDbFactory "freshened" ./test_{n}.db by deleting
/// it, but LiteDB's journal ./test_{n}-log.db survived and was replayed on the next open ("found 2", "found 6", 10 — growing
/// run on run). The property that matters is that an assertion db leaves NOTHING on disk for a later process to pick up.
/// </summary>
public class LiteDbFactoryTests
{
    [Fact]
    public void AnAssertionDb_LeavesNoFileBehind_ForALaterRunToReplay()
    {
        var dir = Directory.GetCurrentDirectory();
        var before = Directory.GetFiles(dir, "*.db").ToHashSet();

        using (var db = LiteDbFactory.Get())
            db.Reservations().Insert(new Reservation { MovieName = "written", RoomName = "1" });

        Directory.GetFiles(dir, "*.db").Where(f => !before.Contains(f))
            .Should().BeEmpty("a file — or its journal — outlives this process and is replayed into the next run's model");
    }

    [Fact]
    public void EachAssertionDb_StartsEmpty()
    {
        using (var first = LiteDbFactory.Get())
            first.Reservations().Insert(new Reservation { MovieName = "written", RoomName = "1" });

        using var next = LiteDbFactory.Get();
        next.Reservations().Count().Should().Be(0);
    }
}
