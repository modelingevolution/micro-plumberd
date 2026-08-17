using MicroPlumberd.Services.Identity.ReadModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Runs the declared identity seed (see <c>AddIdentitySeed</c>) once the identity read models are live, and
/// converges the store to it.
/// <para>
/// Three invariants (feature-001 requirements R3–R5):
/// it starts on <b>readiness</b> — <see cref="ICaughtUpHandler.CaughtUp"/> of <see cref="RolesModel"/>,
/// <see cref="UsersModel"/> and <c>UserAuthorizationModel</c> — never on a timer;
/// it <b>never stops the host</b> — no exception escapes <see cref="ExecuteAsync"/>, a failed attempt logs Error
/// and is retried with bounded backoff, which holds even with the .NET default
/// <c>BackgroundServiceExceptionBehavior.StopHost</c>;
/// and its progress is <b>observable</b> through <see cref="State"/> and the opt-in <c>identity</c> health check.
/// </para>
/// </summary>
public sealed class IdentityInitializerService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IdentitySeedPlan _plan;
    private readonly ILogger<IdentityInitializerService> _logger;
    private readonly TimeProvider _time;
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile IdentitySeedState _state = new(false, "not started", 0);

    /// <summary>
    /// Creates the runner. <see cref="TimeProvider"/> is resolved from DI when registered so tests can compress
    /// the backoff; otherwise <see cref="TimeProvider.System"/> is used.
    /// </summary>
    /// <param name="sp">Root service provider; a scope is created per attempt.</param>
    /// <param name="plan">The accumulated seed declarations.</param>
    /// <param name="logger">Logger.</param>
    internal IdentityInitializerService(IServiceProvider sp, IdentitySeedPlan plan, ILogger<IdentityInitializerService> logger)
    {
        _sp = sp;
        _plan = plan;
        _logger = logger;
        _time = sp.GetService<TimeProvider>() ?? TimeProvider.System;
    }

    /// <summary>
    /// Live snapshot of the seed's progress. Read by the opt-in <c>identity</c> health check.
    /// </summary>
    public IdentitySeedState State => _state;

    /// <summary>
    /// Completes when the seed converged (or when nothing was declared). Never faults — a failing seed keeps
    /// retrying, so this task simply stays incomplete. Intended for tests and start-up gates.
    /// </summary>
    public Task Completed => _completed.Task;

    /// <summary>
    /// The bounded retry backoff of requirement R4: 1, 2, 5, 10, 20, then 30 seconds for every further attempt.
    /// </summary>
    /// <param name="attempt">1-based attempt number that just failed.</param>
    /// <returns>How long to wait before the next attempt.</returns>
    public static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(attempt switch
    {
        <= 1 => 1,
        2 => 2,
        3 => 5,
        4 => 10,
        5 => 20,
        _ => 30
    });

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // R4: the outer catch is the invariant. Whatever happens below, the host stays up.
        try
        {
            await RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Identity seed cancelled (host stopping).");
        }
        catch (Exception ex)
        {
            _state = new IdentitySeedState(false, $"seed runner terminated: {ex.Message}", _state.Attempts, ex.Message);
            _logger.LogError(ex, "Identity seed runner terminated unexpectedly; the host stays up, /health reports identity Unhealthy.");
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var steps = _plan.Build();
        var waitUpTo = _plan.WaitUpTo;

        if (steps.Count == 0)
        {
            _logger.LogInformation("Identity seed: nothing declared; ready.");
            _state = new IdentitySeedState(true, "nothing declared", 0);
            _completed.TrySetResult();
            return;
        }

        _logger.LogInformation(
            "Identity seed: {Roles} role(s), {Users} user(s), {Custom} custom step(s); per-attempt bound {WaitUpTo}",
            steps.Count(s => s is RoleStep), steps.Count(s => s is UserStep), steps.Count(s => s is CustomStep), waitUpTo);

        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            var current = "identity read models";
            try
            {
                _state = new IdentitySeedState(false, $"attempt {attempt}: waiting for identity read models", attempt, _state.LastError);
                _logger.LogDebug(
                    "Identity seed attempt {Attempt}: waiting for identity read models (RolesModel, UsersModel, UserAuthorizationModel)",
                    attempt);

                var (roles, users, userAuth) = await WaitForReadModelsAsync(waitUpTo, stoppingToken);

                using var scope = _sp.CreateScope();
                var ctx = new IdentitySeedContext(
                    scope.ServiceProvider.GetRequiredService<UserManager<User>>(),
                    scope.ServiceProvider.GetRequiredService<RoleManager<Role>>(),
                    roles, users, userAuth, waitUpTo, _time, _logger);

                foreach (var step in steps)
                {
                    current = step.Label;
                    _state = new IdentitySeedState(false, $"attempt {attempt}: {step.Label}", attempt, _state.LastError);
                    await RunStepAsync(ctx, step, stoppingToken);
                }

                _state = new IdentitySeedState(true, $"{steps.Count} step(s) converged", attempt);
                _logger.LogInformation("Identity seed converged after {Attempts} attempt(s): {Summary}",
                    attempt, string.Join(", ", steps.Select(s => s.Label)));
                _completed.TrySetResult();
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Identity seed cancelled (host stopping) at attempt {Attempt}", attempt);
                _state = _state with { Description = "cancelled (host stopping)" };
                return;
            }
            catch (Exception ex)
            {
                var backoff = Backoff(attempt);
                _state = new IdentitySeedState(false,
                    $"attempt {attempt} failed at {current}: {ex.Message} — retry in {backoff}", attempt, ex.Message);
                _logger.LogError(ex,
                    "Identity seed attempt {Attempt} failed at {Step}: {Message}; the host stays up, /health reports identity Unhealthy; retrying in {Backoff}",
                    attempt, current, ex.Message, backoff);

                try
                {
                    await Task.Delay(backoff, _time, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Identity seed cancelled (host stopping) at attempt {Attempt}", attempt);
                    _state = _state with { Description = "cancelled (host stopping)" };
                    return;
                }
            }
        }

        _logger.LogInformation("Identity seed cancelled (host stopping) at attempt {Attempt}", _state.Attempts);
        _state = _state with { Description = "cancelled (host stopping)" };
    }

    private static Task RunStepAsync(IIdentitySeedContext ctx, IdentitySeedStep step, CancellationToken ct) => step switch
    {
        RoleStep r => ctx.EnsureRoleAsync(r.Name, ct),
        UserStep u => RunUserStepAsync(ctx, u, ct),
        CustomStep c => c.Action(ctx, ct),
        _ => throw new NotSupportedException($"Unknown seed step '{step.GetType().Name}'.")
    };

    private static async Task RunUserStepAsync(IIdentitySeedContext ctx, UserStep step, CancellationToken ct)
    {
        var user = await ctx.EnsureUserAsync(step.Email, step.UserName, step.Password, ct);
        foreach (var role in step.Roles)
            await ctx.EnsureInRoleAsync(user, role, ct);
    }

    /// <summary>
    /// Readiness wait (design.md §4.1). <c>Live</c> completes once and stays completed, so a later attempt after
    /// a compressed bound succeeds immediately.
    /// </summary>
    private async Task<(RolesModel Roles, UsersModel Users, UserAuthorizationModel UserAuth)> WaitForReadModelsAsync(
        TimeSpan waitUpTo, CancellationToken ct)
    {
        var roles = _sp.GetRequiredService<RolesModel>();
        var users = _sp.GetRequiredService<UsersModel>();
        var userAuth = _sp.GetRequiredService<UserAuthorizationModel>();

        try
        {
            await Task.WhenAll(roles.Live, users.Live, userAuth.Live).WaitAsync(waitUpTo, _time, ct);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"Waited {waitUpTo} for the identity read models (RolesModel, UsersModel, UserAuthorizationModel) to catch up; not there yet.");
        }

        return (roles, users, userAuth);
    }
}
