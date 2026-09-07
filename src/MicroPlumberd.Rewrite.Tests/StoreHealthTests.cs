using System.Net;
using FluentAssertions;
using MicroPlumberd.Rewrite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MicroPlumberd.Rewrite.Tests;

/// <summary>
/// Readiness. `/health/live` is an HTTP/1.1 endpoint and KurrentDB answers it BEFORE it will serve gRPC on
/// the same port — measured at 528 ms of daylight on a fresh container (2026-09-07, saturn). Every client this
/// tool and this suite open is gRPC, so treating `/health/live` as "ready" opens a window in which the server
/// answers the HTTP/2 preface with GOAWAY <c>HTTP_1_1_REQUIRED</c>. E2E-05 fell into it on the 81-test run,
/// failing in `RewriteFixture.SeedAsync`'s very first append.
/// </summary>
public class StoreHealthTests
{
    /// <summary>
    /// An HTTP/1.1-only server that answers <c>/health/live</c> — exactly the state a starting KurrentDB is in
    /// during the window. <see cref="HttpListener"/> cannot speak HTTP/2, so gRPC can never succeed against it.
    /// </summary>
    private sealed class HttpOnlyStore : IDisposable
    {
        private readonly HttpListener _listener = new();
        public int Port { get; }
        public string ConnectionString => $"esdb://admin:changeit@127.0.0.1:{Port}?tls=false&tlsVerifyCert=false";

        public HttpOnlyStore()
        {
            Port = FreePort();
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

        private static int FreePort()
        {
            using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        public void Dispose() { try { _listener.Stop(); ((IDisposable)_listener).Dispose(); } catch { } }
    }

    [Fact]
    public async Task Readiness_is_not_reached_while_only_HTTP_1_1_is_being_served()
    {
        // The store answers /health/live perfectly. If that alone counted as ready, this would return at once
        // and every caller would then open a gRPC client against a server that cannot serve one.
        using var store = new HttpOnlyStore();

        var act = () => StoreHealth.WaitLiveAsync(store.ConnectionString, TimeSpan.FromSeconds(6),
            NullLogger.Instance);

        (await act.Should().ThrowAsync<TimeoutException>(
                "/health/live is HTTP/1.1 and says nothing about whether gRPC is servable yet"))
            .WithMessage("*gRPC*", "the message has to name the thing that was never ready");
    }

    [Fact]
    public async Task An_unreachable_store_still_fails_on_the_health_check_itself()
    {
        // The control: readiness must still fail for the ordinary reason — nothing listening — and say so,
        // rather than every failure now being reported as a gRPC problem.
        using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();

        var act = () => StoreHealth.WaitLiveAsync(
            $"esdb://admin:changeit@127.0.0.1:{port}?tls=false&tlsVerifyCert=false",
            TimeSpan.FromSeconds(2), NullLogger.Instance);

        (await act.Should().ThrowAsync<TimeoutException>()).WithMessage("*health/live*");
    }
}
