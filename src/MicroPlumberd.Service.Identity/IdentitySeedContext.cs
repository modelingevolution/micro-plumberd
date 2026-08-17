using MicroPlumberd.Services.Identity.ReadModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// The API a seed step works against (feature-001 requirement R6). Every <c>Ensure…</c> is idempotent and
/// carries the read-your-own-write wait: it does not return until its own write is visible in the read model
/// the next step reads, bounded by the per-attempt bound (<see cref="IdentitySeedBuilder.WaitUpTo"/>).
/// </summary>
public interface IIdentitySeedContext
{
    /// <summary>The scoped <see cref="UserManager{TUser}"/> of the current attempt.</summary>
    UserManager<User> UserManager { get; }

    /// <summary>The scoped <see cref="RoleManager{TRole}"/> of the current attempt.</summary>
    RoleManager<Role> RoleManager { get; }

    /// <summary>The seed's logger, so a consumer step logs on the same seam as the library.</summary>
    ILogger Logger { get; }

    /// <summary>
    /// Ensures a role with this name exists, then waits until it is visible in <see cref="RolesModel"/>.
    /// </summary>
    /// <param name="name">Role name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TimeoutException">The write did not become visible within the per-attempt bound.</exception>
    Task EnsureRoleAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Ensures a user with this e-mail exists (<c>EmailConfirmed = true</c>), then waits until it is visible in
    /// <see cref="UsersModel"/>. An existing user is returned untouched — no password reset, no role removal (R2).
    /// </summary>
    /// <param name="email">E-mail; also the lookup key.</param>
    /// <param name="userName">User name; defaults to the e-mail when null.</param>
    /// <param name="password">Password; when null the user is created without one (external-login-only account).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The existing or freshly created user, as held by <see cref="UsersModel"/>.</returns>
    /// <exception cref="TimeoutException">The write did not become visible within the per-attempt bound.</exception>
    Task<User> EnsureUserAsync(string email, string? userName = null, string? password = null, CancellationToken ct = default);

    /// <summary>
    /// Ensures the user is a member of the role (ensuring the role itself first), then waits until the
    /// membership is visible in <c>UserAuthorizationModel</c>.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="role">Role name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TimeoutException">The write did not become visible within the per-attempt bound.</exception>
    Task EnsureInRoleAsync(User user, string role, CancellationToken ct = default);
}

/// <summary>
/// The per-attempt implementation of <see cref="IIdentitySeedContext"/>, living inside one DI scope
/// (feature-001 design.md §4).
/// </summary>
internal sealed class IdentitySeedContext : IIdentitySeedContext
{
    /// <summary>How often a read-your-write wait re-checks visibility.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly RolesModel _roles;
    private readonly UsersModel _users;
    private readonly UserAuthorizationModel _userAuth;
    private readonly TimeSpan _waitUpTo;
    private readonly TimeProvider _time;

    public IdentitySeedContext(
        UserManager<User> userManager,
        RoleManager<Role> roleManager,
        RolesModel roles,
        UsersModel users,
        UserAuthorizationModel userAuth,
        TimeSpan waitUpTo,
        TimeProvider time,
        ILogger logger)
    {
        UserManager = userManager;
        RoleManager = roleManager;
        _roles = roles;
        _users = users;
        _userAuth = userAuth;
        _waitUpTo = waitUpTo;
        _time = time;
        Logger = logger;
    }

    /// <inheritdoc/>
    public UserManager<User> UserManager { get; }

    /// <inheritdoc/>
    public RoleManager<Role> RoleManager { get; }

    /// <inheritdoc/>
    public ILogger Logger { get; }

    /// <inheritdoc/>
    public async Task EnsureRoleAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = Normalize(name);
        var label = $"role '{name}'";

        if (_roles.GetByNormalizedName(normalized) is null)
        {
            Logger.LogInformation("Identity seed: ensuring {Step}", label);
            var result = await RoleManager.CreateAsync(new Role { Name = name });
            if (result.Succeeded)
                Logger.LogInformation("Identity seed: created {Step}", label);
            else if (IsAlreadyThere(result))
                Logger.LogInformation("Identity seed: {Step} was created concurrently: {Errors}", label, Describe(result));
            else
                throw new InvalidOperationException($"Could not create {label}: {Describe(result)}");
        }
        else
        {
            Logger.LogInformation("Identity seed: {Step} already present", label);
        }

        await UntilAsync(() => _roles.GetByNormalizedName(normalized) is not null,
            $"{label} to become visible in RolesModel", ct);
    }

    /// <inheritdoc/>
    public async Task<User> EnsureUserAsync(string email, string? userName = null, string? password = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalizedEmail = Normalize(email);
        var label = $"user '{email}'";

        var existing = _users.GetByNormalizedEmail(normalizedEmail);
        if (existing is not null)
        {
            Logger.LogInformation("Identity seed: {Step} already present", label);
            return existing;
        }

        Logger.LogInformation("Identity seed: ensuring {Step}", label);
        var user = new User
        {
            UserName = userName ?? email,
            Email = email,
            EmailConfirmed = true
        };

        var result = password is null
            ? await UserManager.CreateAsync(user)
            : await UserManager.CreateAsync(user, password);

        if (result.Succeeded)
            Logger.LogInformation("Identity seed: created {Step}", label);
        else if (IsAlreadyThere(result))
            Logger.LogInformation("Identity seed: {Step} was created concurrently: {Errors}", label, Describe(result));
        else
            throw new InvalidOperationException($"Could not create {label}: {Describe(result)}");

        await UntilAsync(() => _users.GetByNormalizedEmail(normalizedEmail) is not null,
            $"{label} to become visible in UsersModel", ct);

        return _users.GetByNormalizedEmail(normalizedEmail)!;
    }

    /// <inheritdoc/>
    public async Task EnsureInRoleAsync(User user, string role, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        // The membership write reads the role out of RolesModel (UserStore.AddToRoleAsync); ensure it first so
        // "Role 'X' does not exist" cannot happen. Idempotent when the role step already ran.
        await EnsureRoleAsync(role, ct);

        var normalizedRole = Normalize(role);
        var userId = UserIdentifier.Parse(user.Id, null);
        var label = $"user '{user.Email}' in role '{role}'";

        if (!_userAuth.IsInRole(userId, normalizedRole))
        {
            Logger.LogInformation("Identity seed: ensuring {Step}", label);
            var result = await UserManager.AddToRoleAsync(user, role);
            if (result.Succeeded)
                Logger.LogInformation("Identity seed: created {Step}", label);
            else if (IsAlreadyThere(result))
                Logger.LogInformation("Identity seed: {Step} was assigned concurrently: {Errors}", label, Describe(result));
            else
                throw new InvalidOperationException($"Could not assign {label}: {Describe(result)}");
        }
        else
        {
            Logger.LogInformation("Identity seed: {Step} already present", label);
        }

        await UntilAsync(() => _userAuth.IsInRole(userId, normalizedRole),
            $"{label} to become visible in UserAuthorizationModel", ct);
    }

    /// <summary>
    /// Read-your-own-write wait (design.md §4): checks first, then polls until <paramref name="visible"/> holds
    /// or the per-attempt bound expires.
    /// </summary>
    private async Task UntilAsync(Func<bool> visible, string what, CancellationToken ct)
    {
        if (visible()) return;

        using var deadline = new CancellationTokenSource(_waitUpTo, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);

        while (true)
        {
            try
            {
                await Task.Delay(PollInterval, _time, linked.Token);
            }
            catch (OperationCanceledException)
            {
                if (visible()) return;
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException($"Waited {_waitUpTo} for {what}; not there yet.");
            }

            if (visible()) return;
        }
    }

    private static string Normalize(string value) => value.ToUpperInvariant();

    private static bool IsAlreadyThere(IdentityResult result) =>
        result.Errors.Any(e =>
            e.Code is "DuplicateRoleName" or "DuplicateUserName" or "DuplicateEmail" or "UserAlreadyInRole" ||
            (e.Description?.Contains("already", StringComparison.OrdinalIgnoreCase) ?? false));

    private static string Describe(IdentityResult result) =>
        string.Join(", ", result.Errors.Select(e => e.Description));
}
