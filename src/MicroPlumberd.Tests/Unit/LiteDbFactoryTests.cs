using FluentAssertions;
using LiteDB;
using MicroPlumberd.Tests.App.CinemaDomain;
using MicroPlumberd.Tests.App.Infrastructure;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// HowToSubscribeRealModel counted reservations of EARLIER test runs: LiteDbFactory "freshened" ./test_{n}.db by deleting
/// it, but LiteDB's journal ./test_{n}-log.db survived and was replayed on the next open ("found 2", "found 6", 10 — growing
/// run on run). The guard is that a new assertion db starts EMPTY whatever an earlier one held: red on the shipped file
/// factory ("found 11" — a leftover journal replayed). (A "no new *.db file" row was dropped, rev-reach 6646ada65: the old
/// factory REUSED test_{n} names, so on any bin that had run once it created no NEW path — blind exactly where the defect
/// lived. The in-memory seam makes "no file" true by construction.)
/// </summary>
public class LiteDbFactoryTests
{
    [Fact]
    public void EachAssertionDb_StartsEmpty()
    {
        using (var first = LiteDbFactory.Get())
            first.Reservations().Insert(new Reservation { MovieName = "written", RoomName = "1" });

        using var next = LiteDbFactory.Get();
        next.Reservations().Count().Should().Be(0);
    }
}
