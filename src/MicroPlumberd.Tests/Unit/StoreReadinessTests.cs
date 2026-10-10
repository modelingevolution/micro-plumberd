using System.Net;
using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Testing;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// <see cref="KurrentDBClientSettingsExtensions.WaitUntilReady"/> — what <see cref="EventStoreServer"/> waits on —
/// must wait for what the FIRST CALL needs: KurrentDB serves <c>/health/live</c> (HTTP/1.1) ~500 ms before it
/// serves gRPC on the same port (measured 2026-09-07, saturn), and every MicroPlumberd client is gRPC. A wait on
/// the health page alone returns inside that window, and the first append meets HTTP_1_1_REQUIRED. dev-pane saw
/// AggregateTests.Update fail on a 30 s readiness timeout when the suite started stores in parallel.
/// </summary>
public class StoreReadinessTests
{
    /// <summary>An HTTP/1.1-only server answering <c>/health/live</c> 204: a starting KurrentDB inside the window.
    /// <see cref="HttpListener"/> cannot speak HTTP/2, so a gRPC call against it can never succeed.</summary>
    private sealed class HttpOnlyStore : IDisposable
    {
        private readonly HttpListener _listener = new();
        public int Port { get; } = FreePort();
        public KurrentDBClientSettings Settings =>
            KurrentDBClientSettings.Create($"esdb://admin:changeit@127.0.0.1:{Port}?tls=false&tlsVerifyCert=false");

        public HttpOnlyStore()
        {
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    try
                    {
                        var ctx = await _listener.GetContextAsync();
                        ctx.Response.StatusCode = 204;
                        ctx.Response.Close();
                    }
                    catch { return; }
                }
            });
        }

        public void Dispose() { try { _listener.Stop(); ((IDisposable)_listener).Dispose(); } catch { } }
    }

    private static int FreePort()
    {
        using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    [Fact]
    public async Task Readiness_IsNotReached_WhileOnlyHttp11IsServed()
    {
        using var store = new HttpOnlyStore();

        var act = () => store.Settings.WaitUntilReady(TimeSpan.FromSeconds(4));

        (await act.Should().ThrowAsync<TimeoutException>(
                "/health/live is HTTP/1.1 and says nothing about whether the gRPC the first append needs is served"))
            .WithMessage("*gRPC*", "the message names the thing that was never ready");
    }

    [Fact]
    public async Task AnUnreachableStore_StillFailsOnTheHealthCheckItself()
    {
        var act = () => KurrentDBClientSettings
            .Create($"esdb://admin:changeit@127.0.0.1:{FreePort()}?tls=false&tlsVerifyCert=false")
            .WaitUntilReady(TimeSpan.FromSeconds(2));

        (await act.Should().ThrowAsync<TimeoutException>()).WithMessage("*health/live*",
            "nothing listening is the ordinary failure, said as such — not every failure is a gRPC problem");
    }

    [Fact]
    public async Task EveryGrpcAttempt_UsesAFreshClient()
    {
        // KurrentDB.Client caches channel discovery per client; one opened inside the window would keep its
        // poisoned channel for every later attempt. So each attempt must build its own (mechanism pinned here;
        // the poisoning itself is not reproducible on demand).
        using var store = new HttpOnlyStore();
        var built = 0;

        var act = () => KurrentDBClientSettingsExtensions.WaitUntilReady(store.Settings, TimeSpan.FromSeconds(3), null,
            s => { Interlocked.Increment(ref built); return new KurrentDBClient(s); });

        await act.Should().ThrowAsync<TimeoutException>();
        built.Should().BeGreaterThan(1, "a failed gRPC attempt is followed by another on a NEW client");
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(5, 150)]
    public void TheReadinessBudget_ScalesWithTheStoresStartingAtOnce(int starting, int expectedSeconds)
    {
        // dev-pane's failure: the suite starts stores in parallel, and 30 s was one store's budget shared by all.
        EventStoreServer.ReadyBudget(TimeSpan.FromSeconds(30), starting)
            .Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }
}
