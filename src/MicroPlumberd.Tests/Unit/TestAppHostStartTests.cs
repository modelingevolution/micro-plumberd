using FluentAssertions;
using KurrentDB.Client;
using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Unit;

/// <summary>
/// TestAppHost.StartAsync waited for the handlers' health with <c>while (true)</c>. A starter that threw (the host stops;
/// IsReady never turns true) or never finished spun the test forever, silently: a full suite was killed by its 25-min
/// timeout with no summary, and IndexBackedMergeReviewFollowupsTests' store was left running (its finally never ran).
/// The wait is now bounded and THROWS: at once when the host stopped (a faulted starter), after ReadyTimeout when nothing
/// became ready. No store: the starters here are fakes, and nothing connects.
/// Each row guards itself with its own deadline, so the unbounded loop shows up as a red row, not a hung run.
/// </summary>
public class TestAppHostStartTests(ITestOutputHelper output)
{
    private static readonly TimeSpan RowDeadline = TimeSpan.FromSeconds(30);

    private TestAppHost Host(IEventHandlerStarter starter, TimeSpan readyTimeout)
    {
        var host = new TestAppHost(output)
            .Configure(x => x
                .AddPlumberd(KurrentDBClientSettings.Create("kurrentdb://127.0.0.1:1?tls=false"))
                .AddSingleton(starter));
        host.ReadyTimeout = readyTimeout;
        return host;
    }

    private static async Task<Exception?> StartWithin(TestAppHost host)
    {
        var start = host.StartAsync();
        var first = await Task.WhenAny(start, Task.Delay(RowDeadline));
        first.Should().BeSameAs(start, $"StartAsync must return or throw on its own — it ran past the row's {RowDeadline.TotalSeconds} s deadline");
        try { await start; return null; }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public async Task AStarterThatNeverBecomesReady_Throws_AfterTheBound()
    {
        using var host = Host(new StallingStarter(), TimeSpan.FromSeconds(2));

        var ex = await StartWithin(host);

        ex.Should().BeOfType<TimeoutException>().Which.Message.Should().Contain("not ready after 2 s");
    }

    [Fact]
    public async Task AStarterThatFails_Throws_AtOnce_NotAfterTheBound()
    {
        using var host = Host(new FailingStarter(), TimeSpan.FromMinutes(10)); // the bound is far: only "at once" passes

        var ex = await StartWithin(host);

        ex.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("stopped before it was ready");
    }

    [Fact]
    public async Task AHostedServiceThatNeverFinishesStarting_Throws_AfterTheBound_Named()
    {
        var host = new TestAppHost(output).Configure(x => x
            .AddPlumberd(KurrentDBClientSettings.Create("kurrentdb://127.0.0.1:1?tls=false"))
            .AddHostedService<NeverStartingService>());
        host.ReadyTimeout = TimeSpan.FromSeconds(2);

        var ex = await StartWithin(host); // no Dispose: the blocked StartAsync would block StopAsync too

        ex.Should().BeOfType<TimeoutException>().Which.Message.Should().Contain("Host.StartAsync did not return within 2 s");
    }

    // ---- must still start ----

    [Fact]
    public async Task AStarterThatCompletes_Starts()
    {
        using var host = Host(new CompletingStarter(TimeSpan.Zero), TimeSpan.FromSeconds(5));

        (await StartWithin(host)).Should().BeNull();
    }

    [Fact]
    public async Task ASlowStarterInsideTheBound_Starts()
    {
        using var host = Host(new CompletingStarter(TimeSpan.FromSeconds(1.5)), TimeSpan.FromSeconds(10));

        (await StartWithin(host)).Should().BeNull();
    }

    // ---- the not-ready health result NAMES the starter (EventHandlerService.Starting / Failure) ----

    [Fact]
    public async Task AStall_IsReported_NamingTheStarterItWaitsFor()
    {
        using var host = Host(new StallingStarter(), TimeSpan.FromSeconds(2));

        (await StartWithin(host)).Should().BeOfType<TimeoutException>().Which.Message.Should().Contain($"(starting {nameof(StallingStarter)})");
    }

    [Fact]
    public async Task AFailure_IsReported_NamingTheStarter_WithItsException()
    {
        using var host = Host(new FailingStarter(), TimeSpan.FromMinutes(10));

        var ex = await StartWithin(host);

        ex.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain($"failed to start at {nameof(FailingStarter)}: InvalidOperationException");
        ex!.InnerException!.Message.Should().Be("index unreachable", "the full message stays on the attached exception");
    }

    /// <summary>
    /// rev-reach 23166defc: a starter's message can carry a connection string. The health DESCRIPTION — served by the health
    /// endpoint — names the starter and the exception type only; the secret stays in the attached exception.
    /// </summary>
    [Fact]
    public async Task AFailureMessageWithASecret_StaysOutOfTheHealthText()
    {
        var starter = new FailingStarter("cannot reach esdb://admin:secret@x:2113");
        using var host = Host(starter, TimeSpan.FromMinutes(10));
        await StartWithin(host);

        var health = await host.Host.Services.GetRequiredService<StartupHealthCheck>().CheckHealthAsync(null!, default);

        health.Description.Should().Contain(nameof(FailingStarter)).And.NotContain("secret");
        health.Exception!.Message.Should().Contain("secret", "the full message is still there for the logs");
    }
}

sealed class StallingStarter : IEventHandlerStarter
{
    public Task Start(CancellationToken stoppingToken) => Task.Delay(Timeout.Infinite, stoppingToken);
}

sealed class FailingStarter(string message = "index unreachable") : IEventHandlerStarter
{
    public async Task Start(CancellationToken stoppingToken)
    {
        await Task.Yield();
        throw new InvalidOperationException(message);
    }
}

sealed class CompletingStarter(TimeSpan after) : IEventHandlerStarter
{
    public Task Start(CancellationToken stoppingToken) => Task.Delay(after, stoppingToken);
}

sealed class NeverStartingService : Microsoft.Extensions.Hosting.IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
