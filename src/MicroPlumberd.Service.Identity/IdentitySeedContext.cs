using System.Collections.Concurrent;
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
    /// Ensures a user with this e-mail exists and has <c>EmailConfirmed = true</c>, then waits until that is
    /// visible in <see cref="UsersModel"/>. The confirm-only flip false→true is the ONLY write ever applied to an
    /// existing user — no password reset, no user-name change, no role removal (R2).
    /// </summary>
    /// <param name="email">E-mail; also the lookup key.</param>
    /// <param name="userName">User name; defaults to the e-mail when null.</param>
    /// <param name="password">Password; when null the user is created without one (external-login-only account).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The user as held by <see cref="UsersModel"/>. The returned instance is the read model's own; do not mutate.
    /// </returns>
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

    private readonly IUserStore<User> _userStore;
    private readonly IUserEmailStore<User>? _emailStore;
    private readonly RolesModel _roles;
    private readonly UsersModel _users;
    private readonly UserAuthorizationModel _userAuth;
    private readonly TimeSpan _waitUpTo;
    private readonly TimeProvider _time;

    /// <summary>
    /// Runner-scoped memory of writes that already succeeded, spanning attempts (design.md §4.3). Without it a
    /// retry after a timed-out visibility wait creates a duplicate: neither <c>RoleStore</c> nor <c>UserStore</c>
    /// dedupes against anything but the (still unfolded) read model.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _written;

    public IdentitySeedContext(
        UserManager<User> userManager,
        RoleManager<Role> roleManager,
        IUserStore<User> userStore,
        RolesModel roles,
        UsersModel users,
        UserAuthorizationModel userAuth,
        ConcurrentDictionary<string, string> written,
        TimeSpan waitUpTo,
        TimeProvider time,
        ILogger logger)
    {
        UserManager = userManager;
        RoleManager = roleManager;
        _userStore = userStore;
        _emailStore = userStore as IUserEmailStore<User>;
        _roles = roles;
        _users = users;
        _userAuth = userAuth;
        _written = written;
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
        var normalized = NormalizeRole(name);
        var key = $"role:{normalized}";
        var label = $"role '{name}'";

        if (_roles.GetByNormalizedName(normalized) is not null)
        {
            Logger.LogInformation("Identity seed: {Step} already present", label);
        }
        else if (_written.ContainsKey(key))
        {
            Logger.LogInformation("Identity seed: {Step} was already created by this runner; waiting for it to become visible", label);
        }
        else
        {
            Logger.LogInformation("Identity seed: ensuring {Step}", label);
            var role = new Role { Name = name };
            var result = await RoleManager.CreateAsync(role);
            if (result.Succeeded)
            {
                _written[key] = role.Id;
                Logger.LogInformation("Identity seed: created {Step}", label);
            }
            else if (IsAlreadyThere(result, label))
            {
                Logger.LogInformation("Identity seed: {Step} was created concurrently: {Errors}", label, Describe(result));
            }
            else
            {
                throw new InvalidOperationException($"Could not create {label}: {Describe(result)}");
            }
        }

        await UntilAsync(() => _roles.GetByNormalizedName(normalized) is not null,
            $"{label} to become visible in RolesModel", ct);
    }

    /// <inheritdoc/>
    public async Task<User> EnsureUserAsync(string email, string? userName = null, string? password = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalizedEmail = NormalizeEmail(email);
        var key = $"user:{normalizedEmail}";
        var label = $"user '{email}'";

        if (_users.GetByNormalizedEmail(normalizedEmail) is not null)
        {
            Logger.LogInformation("Identity seed: {Step} already present", label);
        }
        else if (_written.ContainsKey(key))
        {
            Logger.LogInformation("Identity seed: {Step} was already created by this runner; waiting for it to become visible", label);
        }
        else
        {
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
            {
                _written[key] = user.Id;
                Logger.LogInformation("Identity seed: created {Step}", label);
            }
            else if (IsAlreadyThere(result, label))
            {
                Logger.LogInformation("Identity seed: {Step} was created concurrently: {Errors}", label, Describe(result));
            }
            else
            {
                throw new InvalidOperationException($"Could not create {label}: {Describe(result)}");
            }
        }

        await UntilAsync(() => _users.GetByNormalizedEmail(normalizedEmail) is not null,
            $"{label} to become visible in UsersModel", ct);

        await EnsureEmailConfirmedAsync(normalizedEmail, label, ct);

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

        var normalizedRole = NormalizeRole(role);
        var userId = UserIdentifier.Parse(user.Id, null);
        var key = $"membership:{user.Id}:{normalizedRole}";
        var label = $"user '{user.Email}' in role '{role}'";

        if (_userAuth.IsInRole(userId, normalizedRole))
        {
            Logger.LogInformation("Identity seed: {Step} already present", label);
        }
        else if (_written.ContainsKey(key))
        {
            Logger.LogInformation("Identity seed: {Step} was already assigned by this runner; waiting for it to become visible", label);
        }
        else
        {
            Logger.LogInformation("Identity seed: ensuring {Step}", label);
            var result = await UserManager.AddToRoleAsync(user, role);
            if (result.Succeeded)
            {
                _written[key] = normalizedRole;
                Logger.LogInformation("Identity seed: created {Step}", label);
            }
            else if (IsAlreadyThere(result, label))
            {
                Logger.LogInformation("Identity seed: {Step} was assigned concurrently: {Errors}", label, Describe(result));
            }
            else
            {
                throw new InvalidOperationException($"Could not assign {label}: {Describe(result)}");
            }
        }

        await UntilAsync(() => _userAuth.IsInRole(userId, normalizedRole),
            $"{label} to become visible in UserAuthorizationModel", ct);
    }

    /// <summary>
    /// <c>UserStore.CreateAsync</c> does not carry <c>User.EmailConfirmed</c> into <c>UserProfileAggregate</c>, so a
    /// seeded user is created unconfirmed and needs its own write. This runs for an existing declared user too: the
    /// confirm-only flip false→true is part of the declared state (R2) and is the only write ever applied to one.
    /// The aggregate itself is idempotent (<c>ConfirmEmail</c> returns early when already confirmed), so a repeat
    /// after a timed-out visibility wait appends nothing.
    /// </summary>
    private async Task EnsureEmailConfirmedAsync(string normalizedEmail, string label, CancellationToken ct)
    {
        if (_users.GetByNormalizedEmail(normalizedEmail) is not { EmailConfirmed: false } unconfirmed)
            return;

        if (_emailStore is null)
        {
            Logger.LogWarning(
                "Identity seed: {Step} is not e-mail confirmed and the registered IUserStore<User> ({StoreType}) is not an IUserEmailStore<User>; leaving it unconfirmed",
                label, _userStore.GetType().FullName);
            return;
        }

        Logger.LogInformation("Identity seed: confirming the e-mail of {Step}", label);
        await _emailStore.SetEmailConfirmedAsync(unconfirmed, true, ct);
        await UntilAsync(() => _users.GetByNormalizedEmail(normalizedEmail) is { EmailConfirmed: true },
            $"{label} e-mail confirmation to become visible in UsersModel", ct);
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

    /// <summary>
    /// Normalizes a role name with the registered <see cref="ILookupNormalizer"/>, so the key matches what
    /// <c>RoleManager.CreateAsync</c> stored in <c>RoleCreated.NormalizedName</c>. Never <c>ToUpperInvariant()</c>:
    /// a consumer with a custom normalizer would otherwise make every visibility check fail forever.
    /// </summary>
    private string NormalizeRole(string name) => RoleManager.NormalizeKey(name) ?? name;

    /// <summary>Normalizes an e-mail with the registered <see cref="ILookupNormalizer"/>.</summary>
    private string NormalizeEmail(string email) => UserManager.NormalizeEmail(email) ?? email;

    /// <summary>
    /// A failed <see cref="IdentityResult"/> counts as "someone else already created it" only by
    /// <see cref="IdentityError.Code"/> — never by message text. A failure carrying no code is logged at Warning
    /// and treated as a real failure, so the visibility wait cannot mask it.
    /// </summary>
    private bool IsAlreadyThere(IdentityResult result, string label)
    {
        if (result.Errors.Any(e => e.Code is "DuplicateRoleName" or "DuplicateUserName" or "DuplicateEmail" or "UserAlreadyInRole"))
            return true;

        foreach (var e in result.Errors.Where(e => string.IsNullOrEmpty(e.Code)))
            Logger.LogWarning("Identity seed: {Step} failed with an error that carries no code: {Description}", label, e.Description);

        return false;
    }

    private static string Describe(IdentityResult result) =>
        string.Join(", ", result.Errors.Select(e => e.Description));
}
