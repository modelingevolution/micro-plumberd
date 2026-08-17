using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using MicroPlumberd.Services;
using MicroPlumberd.Services.Identity;
using MicroPlumberd.Services.Identity.ReadModels;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace MicroPlumberd.Tests.Integration.Identity;

/// <summary>
/// AT-01…AT-08 of epic-083 feature-001 (acceptance-tests.md).
/// Every host below leaves <see cref="HostOptions.BackgroundServiceExceptionBehavior"/> at the .NET default
/// (<see cref="BackgroundServiceExceptionBehavior.StopHost"/>) on purpose: the tests prove the runner's catch,
/// not the option.
/// </summary>
[TestCategory("Integration")]
public class IdentitySeedTests : IClassFixture<EventStoreServer>, IAsyncLifetime
{
    /// <summary>Hard cap for every wait in this file. Never a blind Task.Delay.</summary>
    private static readonly TimeSpan Cap = TimeSpan.FromSeconds(90);

    private readonly EventStoreServer _eventStore;
    private readonly ITestOutputHelper _output;

    public IdentitySeedTests(EventStoreServer eventStore, ITestOutputHelper output)
    {
        _eventStore = eventStore;
        _output = output;
    }

    /// <summary>Restarting the in-memory container wipes it, so every test starts from a fresh store.</summary>
    public async Task InitializeAsync() => await _eventStore.StartInDocker(inMemory: true);

    public Task DisposeAsync() => Task.CompletedTask;

    #region AT-01

    [Fact]
    public async Task AT01_FreshStore_ConvergesOnTheFirstAttempt()
    {
        var logs = NewLogs();
        using var host = CreateHost(logs, s => s.AddIdentitySeed(seed => seed
            .Role("Administrator")
            .Role("Accountant")
            .User("admin@localhost", u => u.WithUserName("admin").WithPassword("Admin123!").InRoles("Administrator"))));

        await host.StartAsync();
        await Seed(host).Completed.WaitAsync(Cap);

        var roles = host.Services.GetRequiredService<RolesModel>();
        var users = host.Services.GetRequiredService<UsersModel>();
        var userAuth = host.Services.GetRequiredService<UserAuthorizationModel>();

        roles.GetAllRoles().Select(r => r.Name).Should().BeEquivalentTo("Administrator", "Accountant");

        var all = users.GetAllUsers();
        all.Should().HaveCount(1);
        all[0].Email.Should().Be("admin@localhost");
        all[0].UserName.Should().Be("admin");
        all[0].EmailConfirmed.Should().BeTrue();

        userAuth.IsInRole(UserIdentifier.Parse(all[0].Id, null), "ADMINISTRATOR").Should().BeTrue();

        using (var scope = host.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync("admin@localhost");
            user.Should().NotBeNull();
            (await userManager.IsInRoleAsync(user!, "Administrator")).Should().BeTrue();
        }

        Seed(host).State.Ready.Should().BeTrue();
        Seed(host).State.Attempts.Should().Be(1);
        (await HealthOf(host)).Status.Should().Be(HealthStatus.Healthy);

        logs.Errors.Should().BeEmpty("a converging seed writes nothing at Error level");

        await host.StopAsync();
    }

    #endregion

    #region AT-02

    [Fact]
    public async Task AT02_RestartIsIdempotentAndNeverModifiesAnExistingUser()
    {
        // The store from AT-01, then an operator changes the password.
        using (var first = CreateHost(NewLogs(), s => s.AddIdentitySeed(seed => AdminDeclaration(seed))))
        {
            await first.StartAsync();
            await Seed(first).Completed.WaitAsync(Cap);

            using var scope = first.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync("admin@localhost");
            user.Should().NotBeNull();

            var changed = await userManager.ChangePasswordAsync(user!, "Admin123!", "Changed456!");
            changed.Succeeded.Should().BeTrue(
                "the operator's password change must land: {0}", string.Join(", ", changed.Errors.Select(e => e.Description)));

            await Until(() => userManager.CheckPasswordAsync(user!, "Changed456!"), "the new password to be folded");

            await first.StopAsync();
        }

        // The same declaration, second host, same store.
        var logs = NewLogs();
        using var second = CreateHost(logs, s => s.AddIdentitySeed(seed => AdminDeclaration(seed)));
        await second.StartAsync();
        await Seed(second).Completed.WaitAsync(Cap);

        var roles = second.Services.GetRequiredService<RolesModel>();
        var users = second.Services.GetRequiredService<UsersModel>();

        roles.GetAllRoles().Where(r => r.Name == "Administrator").Should().HaveCount(1);
        roles.GetAllRoles().Where(r => r.Name == "Accountant").Should().HaveCount(1);
        users.GetAllUsers().Should().HaveCount(1);

        using (var scope = second.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync("admin@localhost");
            user.Should().NotBeNull();
            (await userManager.CheckPasswordAsync(user!, "Changed456!"))
                .Should().BeTrue("the seed must not reset the password of an existing user");
            (await userManager.CheckPasswordAsync(user!, "Admin123!"))
                .Should().BeFalse("the declared password must not have been re-applied");
        }

        Seed(second).State.Attempts.Should().Be(1);
        logs.Errors.Should().BeEmpty();

        await second.StopAsync();
    }

    [Fact]
    public async Task AT02b_AnExistingUnconfirmedDeclaredUserIsConfirmedAndNothingElseIsTouched()
    {
        // Given: a user an operator created with a password, which the store persists UNCONFIRMED
        // (UserStore.CreateAsync does not carry User.EmailConfirmed into UserProfileAggregate).
        using (var first = CreateHost(NewLogs(), s => s.AddIdentitySeed(seed => seed.Role("Administrator"))))
        {
            await first.StartAsync();
            await Seed(first).Completed.WaitAsync(Cap);

            using var scope = first.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var created = await userManager.CreateAsync(
                new User { UserName = "operator", Email = "unconfirmed@example.com", EmailConfirmed = true },
                "Original123!");
            created.Succeeded.Should().BeTrue(
                "the operator's user must land: {0}", string.Join(", ", created.Errors.Select(e => e.Description)));

            await Until(async () => await userManager.FindByEmailAsync("unconfirmed@example.com") is not null,
                "the operator's user to become visible");

            // Positive control: the precondition this test exists for really holds.
            var beforeSeed = await userManager.FindByEmailAsync("unconfirmed@example.com");
            beforeSeed!.EmailConfirmed.Should().BeFalse(
                "UserStore.CreateAsync leaves the profile unconfirmed - without that this test proves nothing");

            await first.StopAsync();
        }

        // When: a host declares that same user.
        var logs = NewLogs();
        using var second = CreateHost(logs, s => s.AddIdentitySeed(seed => seed
            .Role("Administrator")
            .User("unconfirmed@example.com", u => u
                .WithUserName("seeded")
                .WithPassword("Seeded999!")
                .InRoles("Administrator"))));

        await second.StartAsync();
        await Seed(second).Completed.WaitAsync(Cap);

        second.Services.GetRequiredService<UsersModel>().GetAllUsers().Should().HaveCount(1);

        using (var scope = second.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync("unconfirmed@example.com");
            user.Should().NotBeNull();

            user!.EmailConfirmed.Should().BeTrue("the confirm-only flip false->true is part of the declared state");
            (await userManager.IsInRoleAsync(user, "Administrator")).Should().BeTrue();

            // ...and it is the ONLY write applied to an existing user.
            user.UserName.Should().Be("operator", "the declared user name must not overwrite an existing one");
            (await userManager.CheckPasswordAsync(user, "Original123!"))
                .Should().BeTrue("the declared password must not be applied to an existing user");
            (await userManager.CheckPasswordAsync(user, "Seeded999!")).Should().BeFalse();
        }

        logs.Errors.Should().BeEmpty();

        await second.StopAsync();
    }

    #endregion

    #region AT-03

    [Fact]
    public async Task AT03_ARoleDroppedFromTheDeclarationIsLeftAlone()
    {
        await SeedAdminHost();

        var logs = NewLogs();
        using var host = CreateHost(logs, s => s.AddIdentitySeed(seed => seed.Role("Administrator")));
        await host.StartAsync();
        await Seed(host).Completed.WaitAsync(Cap);

        var roles = host.Services.GetRequiredService<RolesModel>();
        roles.GetAllRoles().Select(r => r.Name).Should().Contain("Accountant",
            "the seed is additive: dropping a role from the declaration never deletes it");
        roles.GetAllRoles().Select(r => r.Name).Should().Contain("Administrator");

        await host.StopAsync();
    }

    #endregion

    #region AT-04

    [Fact]
    public async Task AT04_AUserWithoutAPasswordIsAnExternalLoginOnlyAccount()
    {
        var logs = NewLogs();
        using var host = CreateHost(logs, s => s.AddIdentitySeed(seed => seed
            .Role("Auditor")
            .User("audit@example.com", u => u.InRoles("Auditor"))));

        await host.StartAsync();
        await Seed(host).Completed.WaitAsync(Cap);

        using var scope = host.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync("audit@example.com");

        user.Should().NotBeNull();
        user!.UserName.Should().Be("audit@example.com", "the user name defaults to the e-mail");
        (await userManager.IsInRoleAsync(user, "Auditor")).Should().BeTrue();
        (await userManager.HasPasswordAsync(user)).Should().BeFalse();

        logs.Errors.Should().BeEmpty();

        await host.StopAsync();
    }

    #endregion

    #region AT-05

    [Fact]
    public async Task AT05_AFailingStepNeverStopsTheHost_HealthNamesIt_AndTheRetrySucceeds()
    {
        var invocations = 0;
        var logs = NewLogs();

        // Compressed, not instantaneous: the backoff windows (500 ms, 1 s) stay long enough to sample health
        // deterministically, while the whole test still costs well under two seconds of waiting.
        using var host = CreateHost(logs, s => s.AddIdentitySeed(seed => seed
            .Role("Administrator")
            .Then(async (ctx, ct) =>
            {
                if (Interlocked.Increment(ref invocations) <= 2)
                    throw new InvalidOperationException("planted");

                // R6: the escape hatch works through the context, with the same read-your-write waits.
                await ctx.EnsureRoleAsync("FromCustomStep", ct);
                await ctx.EnsureUserAsync("custom@example.com", ct: ct);
            })
            .WaitUpTo(TimeSpan.FromMinutes(2))), new CompressedTimeProvider(2));

        using var samplerStop = new CancellationTokenSource();
        var sawPlantedUnhealthy = 0;
        var sampler = Task.Run(async () =>
        {
            while (!samplerStop.IsCancellationRequested)
            {
                var entry = await HealthOf(host);
                if (entry.Status == HealthStatus.Unhealthy && (entry.Description?.Contains("planted") ?? false))
                    Interlocked.Exchange(ref sawPlantedUnhealthy, 1);
                try { await Task.Delay(10, samplerStop.Token); } catch (OperationCanceledException) { }
            }
        });

        await host.StartAsync();
        await Seed(host).Completed.WaitAsync(Cap);
        await samplerStop.CancelAsync();
        await sampler;

        // Positive controls first: the failure really happened and was really reported.
        logs.Errors.Any(l => l.Message.Contains("Identity seed attempt") && l.Message.Contains("planted"))
            .Should().BeTrue("a failed attempt logs at Error naming the attempt and the cause");
        logs.Errors.Any(l => l.Exception is InvalidOperationException)
            .Should().BeTrue("the Error line carries the original exception");
        sawPlantedUnhealthy.Should().Be(1, "health entry 'identity' must be Unhealthy naming 'planted' while the step is failing");

        // The host is still running.
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.IsCancellationRequested.Should().BeFalse();
        host.Services.GetRequiredService<RolesModel>().Should().NotBeNull();

        invocations.Should().Be(3);
        Seed(host).State.Ready.Should().BeTrue();
        Seed(host).State.Attempts.Should().Be(3);
        (await HealthOf(host)).Status.Should().Be(HealthStatus.Healthy);

        var rolesAfter = host.Services.GetRequiredService<RolesModel>().GetAllRoles();
        rolesAfter.Where(r => r.Name == "Administrator").Should().HaveCount(1,
            "the retried attempts re-run the whole plan and every step is idempotent");

        // R6: what the custom step did through IIdentitySeedContext really landed.
        rolesAfter.Where(r => r.Name == "FromCustomStep").Should().HaveCount(1);
        using (var scope = host.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var fromStep = await userManager.FindByEmailAsync("custom@example.com");
            fromStep.Should().NotBeNull();
            fromStep!.UserName.Should().Be("custom@example.com", "EnsureUserAsync defaults the user name to the e-mail");
            fromStep.EmailConfirmed.Should().BeTrue();
            (await userManager.HasPasswordAsync(fromStep)).Should().BeFalse();
        }

        await host.StopAsync();
    }

    #endregion

    #region AT-06

    [Fact]
    public async Task AT06_ACompressedReadinessBoundSummonsTheFailureAndTheSameHostCuresItself()
    {
        await SeedAdminHost();

        var logs = NewLogs();
        using var host = CreateHost(logs, s => s.AddIdentitySeed(seed => AdminDeclaration(seed).WaitUpTo(TimeSpan.FromMilliseconds(1))),
            new CompressedTimeProvider(10));

        await host.StartAsync();
        await Seed(host).Completed.WaitAsync(Cap);

        logs.Errors.Any(l => l.Exception is TimeoutException &&
                             (l.Exception.Message.Contains("identity read models") ||
                              l.Exception.Message.Contains("visible")))
            .Should().BeTrue("a 1 ms per-attempt bound cannot cover a replay, so at least one attempt must time out");

        host.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.IsCancellationRequested.Should().BeFalse();

        Seed(host).State.Ready.Should().BeTrue();
        Seed(host).State.Attempts.Should().BeGreaterThan(1);

        var roles = host.Services.GetRequiredService<RolesModel>();
        roles.GetAllRoles().Where(r => r.Name == "Administrator").Should().HaveCount(1);
        roles.GetAllRoles().Where(r => r.Name == "Accountant").Should().HaveCount(1);
        host.Services.GetRequiredService<UsersModel>().GetAllUsers().Should().HaveCount(1,
            "the failed attempts wrote nothing, so they cannot have produced duplicates");

        await host.StopAsync();
    }

    [Fact]
    public async Task AT06b_APostWriteVisibilityTimeoutIsRetriedWithoutCreatingDuplicates()
    {
        // A FRESH store plus a 1 ms per-attempt bound: once the read models are live, every attempt writes and
        // then times out waiting for its own write to be projected back. That is the path that used to create a
        // duplicate role/user on the next attempt (the stores dedupe only through the still-unfolded read model).
        //
        // The compression factor is load-bearing, not cosmetic. The duplicate only exists in the window between
        // "the append returned" and "the projection delivered it back" (measured here: tens of milliseconds). At
        // x10 the retry lands AFTER that window, the read model already shows the role, and the test passes even
        // with the written-keys memory removed - i.e. it proves nothing. At x1000 the retry lands inside the
        // window, so this test fails (two roles, two users) if the memory is taken away. Verified by mutation.
        var logs = NewLogs();
        using var host = CreateHost(logs, s => s.AddIdentitySeed(seed => seed
            .Role("Administrator")
            .User("admin@localhost", u => u.WithUserName("admin").WithPassword("Admin123!").InRoles("Administrator"))
            .WaitUpTo(TimeSpan.FromMilliseconds(1))), new CompressedTimeProvider(1000));

        await host.StartAsync();
        await Seed(host).Completed.WaitAsync(Cap);

        // Positive control: at least one attempt really failed AFTER a write, in a visibility wait - not only in
        // the read-model wait that happens before anything is written.
        logs.Errors.Any(l => l.Exception is TimeoutException && l.Exception.Message.Contains("to become visible"))
            .Should().BeTrue("a 1 ms bound cannot cover an append being projected back, so a post-write wait must time out");

        host.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.IsCancellationRequested.Should().BeFalse();

        Seed(host).State.Ready.Should().BeTrue();
        Seed(host).State.Attempts.Should().BeGreaterThan(1);

        var roles = host.Services.GetRequiredService<RolesModel>();
        roles.GetAllRoles().Should().HaveCount(1, "the retried attempts must not create a second role");
        roles.GetAllRoles().Single().Name.Should().Be("Administrator");

        var users = host.Services.GetRequiredService<UsersModel>();
        users.GetAllUsers().Should().HaveCount(1, "the retried attempts must not create a second user");
        users.GetAllUsers().Single().EmailConfirmed.Should().BeTrue();

        using (var scope = host.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync("admin@localhost");
            user.Should().NotBeNull();
            (await userManager.IsInRoleAsync(user!, "Administrator")).Should().BeTrue();
        }

        await host.StopAsync();
    }

    #endregion

    #region AT-07

    [Fact]
    public async Task AT07_HealthNamesTheStepInProgress()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logs = NewLogs();

        using var host = CreateHost(logs, s => s.AddIdentitySeed(seed => seed
            .Role("Administrator")
            .Then(async (_, ct) => await gate.Task.WaitAsync(ct))));

        await host.StartAsync();

        await Until(async () =>
        {
            var e = await HealthOf(host);
            return e.Status == HealthStatus.Unhealthy && (e.Description?.Contains("custom step #1") ?? false);
        }, "health 'identity' to be Unhealthy naming the custom step in progress");

        Seed(host).State.Ready.Should().BeFalse();

        gate.SetResult();

        await Seed(host).Completed.WaitAsync(Cap);
        (await HealthOf(host)).Status.Should().Be(HealthStatus.Healthy);
        Seed(host).State.Ready.Should().BeTrue();

        await host.StopAsync();
    }

    #endregion

    #region AT-08

    [Fact]
    public async Task AT08_TheLegacyAddIdentityInitializerIsAnAdapterOverTheSeed()
    {
        // SeedAdminUser = false => empty seed => Ready immediately, nothing created.
        using (var disabled = CreateHost(NewLogs(), s => s.AddIdentityInitializer(o =>
               {
                   o.SeedAdminUser = false;
                   o.ProjectionWaitTime = TimeSpan.FromSeconds(5);
               })))
        {
            await disabled.StartAsync();
            await Seed(disabled).Completed.WaitAsync(TimeSpan.FromSeconds(10));

            Seed(disabled).State.Ready.Should().BeTrue();
            Seed(disabled).State.Attempts.Should().Be(0, "an empty seed never starts an attempt");
            Seed(disabled).State.Description.Should().Be("nothing declared");
            (await HealthOf(disabled)).Status.Should().Be(HealthStatus.Healthy);

            disabled.Services.GetRequiredService<RolesModel>().GetAllRoles().Should().BeEmpty();
            disabled.Services.GetRequiredService<UsersModel>().GetAllUsers().Should().BeEmpty();

            await disabled.StopAsync();
        }

        var logs = NewLogs();
        using var host = CreateHost(logs, s => s.AddIdentityInitializer(o =>
        {
            o.AdminEmail = "root@x";
            o.AdminUserName = "root";
            o.AdminPassword = "Root123!";
            o.AdminRoleName = "Owner";
            o.ProjectionWaitTime = TimeSpan.FromSeconds(5);
        }));

        await host.StartAsync();
        await Seed(host).Completed.WaitAsync(Cap);

        host.Services.GetRequiredService<RolesModel>().GetAllRoles()
            .Select(r => r.Name).Should().BeEquivalentTo("Owner");

        using (var scope = host.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync("root@x");
            user.Should().NotBeNull();
            user!.UserName.Should().Be("root");
            (await userManager.IsInRoleAsync(user, "Owner")).Should().BeTrue();
            (await userManager.CheckPasswordAsync(user, "Root123!")).Should().BeTrue();
        }

        Seed(host).State.Attempts.Should().Be(1);
        logs.Errors.Should().BeEmpty();

        await host.StopAsync();
    }

    #endregion

    #region Harness

    private static IdentitySeedBuilder AdminDeclaration(IdentitySeedBuilder seed) => seed
        .Role("Administrator")
        .Role("Accountant")
        .User("admin@localhost", u => u.WithUserName("admin").WithPassword("Admin123!").InRoles("Administrator"));

    /// <summary>Runs one host that seeds the AT-01 state, then stops it. Leaves the store populated.</summary>
    private async Task SeedAdminHost()
    {
        using var host = CreateHost(NewLogs(), s => s.AddIdentitySeed(seed => AdminDeclaration(seed)));
        await host.StartAsync();
        await Seed(host).Completed.WaitAsync(Cap);
        await host.StopAsync();
    }

    private static IdentityInitializerService Seed(IHost host) =>
        host.Services.GetRequiredService<IdentityInitializerService>();

    private static async Task<HealthReportEntry> HealthOf(IHost host)
    {
        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        report.Entries.Should().ContainKey("identity");
        return report.Entries["identity"];
    }

    private IHost CreateHost(CapturedLogs logs, Action<IServiceCollection> configure, TimeProvider? time = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(l =>
            {
                l.ClearProviders();
                l.AddProvider(new CapturingLoggerProvider(logs));
                l.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices((_, services) =>
            {
                services.AddPlumberd(_eventStore.GetEventStoreSettings());
                services.AddPlumberdIdentity();
                // AddPlumberdIdentity -> AddDefaultTokenProviders registers DataProtectorTokenProvider, and
                // UserManager's constructor resolves every provider in Options.Tokens.ProviderMap. A web host gets
                // data protection from WebApplicationBuilder; a plain Host.CreateDefaultBuilder does not, so the
                // harness supplies it. Without it UserManager<User> cannot be constructed at all.
                services.AddDataProtection();
                if (time is not null) services.AddSingleton(time);
                services.AddHealthChecks().AddIdentitySeedHealthCheck();
                configure(services);
                // HostOptions deliberately left at the .NET default (StopHost).
            })
            .Build();

    private CapturedLogs NewLogs() => new(_output);

    private static async Task Until(Func<Task<bool>> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!await condition())
        {
            if (sw.Elapsed > Cap)
                throw new TimeoutException($"Timed out after {Cap} waiting for {what}.");
            await Task.Delay(25);
        }
    }

    /// <summary>
    /// A <see cref="TimeProvider"/> whose timers fire <paramref name="factor"/> times sooner than asked, so a test
    /// can compress the retry backoff and the per-attempt bound without touching the wall clock.
    /// Everything the runner waits on (<c>Task.Delay</c>, <c>Task.WaitAsync</c>, the deadline
    /// <see cref="CancellationTokenSource"/>) goes through <see cref="TimeProvider.CreateTimer"/>, so all of it
    /// compresses uniformly.
    /// </summary>
    private sealed class CompressedTimeProvider(int factor) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => base.CreateTimer(callback, state, Scale(dueTime), Scale(period));

        private TimeSpan Scale(TimeSpan t) =>
            t < TimeSpan.Zero ? t : TimeSpan.FromTicks(t.Ticks / factor);
    }

    internal sealed record LogRecord(LogLevel Level, string Category, string Message, Exception? Exception);

    /// <summary>Log sink of one host. <see cref="Errors"/> is the positive control several ATs assert on.</summary>
    internal sealed class CapturedLogs(ITestOutputHelper output)
    {
        private readonly ConcurrentQueue<LogRecord> _all = new();

        public void Add(LogRecord r)
        {
            _all.Enqueue(r);
            try { output.WriteLine($"[{r.Level}] {r.Category}: {r.Message}{(r.Exception is null ? "" : " <- " + r.Exception.GetType().Name + ": " + r.Exception.Message)}"); }
            catch (InvalidOperationException) { /* the test already finished */ }
        }

        /// <summary>Error (and worse) written by the seed itself.</summary>
        public IReadOnlyList<LogRecord> Errors => _all
            .Where(r => r.Level >= LogLevel.Error && r.Category.Contains(nameof(IdentityInitializerService)))
            .ToList();
    }

    private sealed class CapturingLoggerProvider(CapturedLogs logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, logs);
        public void Dispose() { }

        private sealed class CapturingLogger(string category, CapturedLogs logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => logs.Add(new LogRecord(logLevel, category, formatter(state, exception), exception));
        }
    }

    #endregion
}
