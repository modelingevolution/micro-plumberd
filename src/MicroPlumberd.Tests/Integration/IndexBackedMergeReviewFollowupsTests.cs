using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MicroPlumberd.Services;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.App.Domain;
using MicroPlumberd.Tests.App.Infrastructure;
using MicroPlumberd.Tests.Utils;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Integration;

/// <summary>
/// Review follow-ups closed before v1: (CLOSE #1) the full-DI ACCEPTANCE PATH a real consumer wires — host boot →
/// EventHandlerStarter → SubscribeEventHandlerViaIndex(eh:null) DI resolution — and (CLOSE #3) the cross-output
/// managed-index-name COLLISION guard (a punctuation collision must fail loud, not silently DELETE the wrong index).
/// </summary>
public class IndexBackedMergeReviewFollowupsTests
{
    // CLOSE #3 — collision guard (no server): two DISTINCT output streams that normalize to the same managed base
    // must be rejected; the SAME output stream re-registering is idempotent.
    [Fact]
    public void ManagedBase_collision_is_rejected_same_name_is_idempotent()
    {
        UserDefinedIndex.RegisterManagedBase("CollFoo.1"); // claims base "collfoo-1"
        var idempotent = () => UserDefinedIndex.RegisterManagedBase("CollFoo.1");
        idempotent.Should().NotThrow("re-registering the SAME output stream must be a no-op");

        var collision = () => UserDefinedIndex.RegisterManagedBase("CollFoo-1"); // also normalizes to "collfoo-1"
        collision.Should().Throw<InvalidOperationException>()
            .WithMessage("*collision*")
            .Which.Message.Should().Contain("collfoo-1");
    }

    // CLOSE #1 — the full DI acceptance path (real KurrentDB 26.1). A consumer registers the handler with
    // MergeSource.UserDefinedIndex and starts the host; EventHandlerService → EventHandlerStarter.Start →
    // SubscribeEventHandlerViaIndex(eh:null) resolves the handler from DI and folds index-delivered events. This
    // exercises the eh==null DI-resolution + starter routing that the instance-passing tests bypass.
    [TestCategory("Integration")]
    public class DiAcceptancePath(ITestOutputHelper output)
    {
        private static string? NameOf(object? payload) => payload switch
        {
            FooCreated c => c.Name,
            FooRefined r => r.Name,
            _ => null
        };

        private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                await Task.Delay(200);
            }
            return condition();
        }

        [Fact]
        public async Task Full_di_index_backed_handler_folds_events_and_tails_live()
        {
            var es = EventStoreServer.Create($"mp-di-{Guid.NewGuid():N}");
            await es.StartInDocker(inMemory: true);
            TestAppHost? host = null;
            try
            {
                var settings = es.GetEventStoreSettings();
                var appender = Plumber.Create(settings);

                // History appended before the host boots.
                for (var i = 0; i < 3; i++) await appender.SaveNew(FooAggregate.Open(i.ToString(), Guid.NewGuid()));

                // The FULL DI path a real consumer uses: opt in via mergeSource, boot the host (blocks until the
                // starter has subscribed via SubscribeEventHandlerViaIndex(eh:null)).
                host = new TestAppHost(output);
                host.Configure(x => x
                    .AddPlumberd(settings)
                    .AddSingleton<InMemoryAssertionDb>()
                    .AddSingletonEventHandler<CaughtUpFooModel>(mergeSource: MergeSource.UserDefinedIndex));
                var sp = await host.StartAsync();

                var model = sp.GetRequiredService<CaughtUpFooModel>(); // the DI-resolved singleton the runner drives
                (await WaitUntil(() => model.AssertionDb.Index.Count >= 3, TimeSpan.FromSeconds(60)))
                    .Should().BeTrue("the DI-wired index-backed handler must fold the 3 historical events");

                DeliveredOrder(model).Should().Equal(new[] { "0", "1", "2" }, "history folded in commit order via the index");

                // Live after boot, still through the DI-resolved handler.
                await appender.SaveNew(FooAggregate.Open("3", Guid.NewGuid()));
                (await WaitUntil(() => model.AssertionDb.Index.Count >= 4, TimeSpan.FromSeconds(30)))
                    .Should().BeTrue("post-boot appends tail into the DI-wired index-backed handler");
                DeliveredOrder(model).Should().Equal(new[] { "0", "1", "2", "3" });
            }
            finally
            {
                host?.Dispose();
                await es.DisposeAsync();
            }
        }

        private static string[] DeliveredOrder(CaughtUpFooModel m) =>
            m.Timeline.Where(t => t.Kind == "event").Select(t => NameOf(t.Payload)).ToArray()!;
    }
}
