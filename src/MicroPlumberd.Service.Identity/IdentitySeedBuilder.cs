namespace MicroPlumberd.Services.Identity;

/// <summary>
/// A single unit of the seed plan. Every step knows the human readable label used in
/// <see cref="IdentitySeedState.Description"/> and in the log (feature-001 design.md §2).
/// </summary>
/// <param name="Label">Human readable label, e.g. <c>role 'Administrator'</c>.</param>
internal abstract record IdentitySeedStep(string Label);

/// <summary>Ensures a role exists.</summary>
internal sealed record RoleStep(string Name) : IdentitySeedStep($"role '{Name}'");

/// <summary>Ensures a user exists and is a member of the declared roles.</summary>
internal sealed record UserStep(string Email, string UserName, string? Password, IReadOnlyList<string> Roles)
    : IdentitySeedStep($"user '{Email}'");

/// <summary>Runs a consumer supplied step under the same readiness, retry and health rules.</summary>
internal sealed record CustomStep(Func<IIdentitySeedContext, CancellationToken, Task> Action, string Label)
    : IdentitySeedStep(Label);

/// <summary>
/// Declarative, fluent collector of the identity state that must exist after start-up
/// (feature-001 requirement R1). The library knows no role name and no user name of its own:
/// everything is declared by the consumer.
/// </summary>
/// <example>
/// <code>
/// services.AddIdentitySeed(seed => seed
///     .Role("Administrator")
///     .User("admin@localhost", u => u.WithUserName("admin").WithPassword(pwd).InRoles("Administrator"))
///     .WaitUpTo(TimeSpan.FromSeconds(30)));
/// </code>
/// </example>
public sealed class IdentitySeedBuilder
{
    /// <summary>
    /// Default per-attempt readiness bound used when the consumer never calls <see cref="WaitUpTo"/>: 30 seconds.
    /// </summary>
    public static readonly TimeSpan DefaultWaitUpTo = TimeSpan.FromSeconds(30);

    private readonly List<IdentitySeedStep> _steps = new();
    private TimeSpan _waitUpTo = DefaultWaitUpTo;

    internal IReadOnlyList<IdentitySeedStep> Steps => _steps;

    internal TimeSpan WaitUpToValue => _waitUpTo;

    /// <summary>
    /// Declares that a role with this name must exist. Ensure semantics (R2): the role is created when absent,
    /// never renamed and never deleted. Removing the declaration leaves the role alone.
    /// </summary>
    /// <param name="name">Role name as the consumer wants it stored (the normalized name is derived from it).</param>
    /// <returns>The builder, for chaining.</returns>
    public IdentitySeedBuilder Role(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _steps.Add(new RoleStep(name));
        return this;
    }

    /// <summary>
    /// Declares that a user with this e-mail must exist and be a member of the roles configured on the
    /// <see cref="IdentitySeedUserBuilder"/>. Ensure semantics (R2): an existing user is returned untouched —
    /// no password reset, no role removal.
    /// </summary>
    /// <param name="email">The user's e-mail; also the lookup key.</param>
    /// <param name="configure">Optional configuration: user name, password, roles. User name defaults to the e-mail.</param>
    /// <returns>The builder, for chaining.</returns>
    public IdentitySeedBuilder User(string email, Action<IdentitySeedUserBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var b = new IdentitySeedUserBuilder(email);
        configure?.Invoke(b);
        _steps.Add(new UserStep(email, b.UserNameValue ?? email, b.PasswordValue, b.RolesValue));
        return this;
    }

    /// <summary>
    /// Escape hatch (R6): runs a consumer supplied step after the read models are live, under the same
    /// retry and health rules as the declarative steps.
    /// </summary>
    /// <param name="step">The step. Everything it throws fails the attempt and is retried with backoff.</param>
    /// <param name="label">Optional human readable label; defaults to <c>custom step #n</c>, counting custom steps only.</param>
    /// <returns>The builder, for chaining.</returns>
    public IdentitySeedBuilder Then(Func<IIdentitySeedContext, CancellationToken, Task> step, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Add(new CustomStep(step, label ?? $"custom step #{_steps.Count(x => x is CustomStep) + 1}"));
        return this;
    }

    /// <summary>
    /// Sets the per-attempt readiness bound (R3): how long one attempt may wait for the identity read models
    /// to catch up, and how long each write may wait to become visible in the read model the next step reads.
    /// Expiry fails the attempt, never the host. Last call wins when several declarations are accumulated.
    /// </summary>
    /// <param name="bound">The bound. Must be greater than zero.</param>
    /// <returns>The builder, for chaining.</returns>
    public IdentitySeedBuilder WaitUpTo(TimeSpan bound)
    {
        if (bound <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(bound), bound, "The per-attempt bound must be greater than zero.");
        _waitUpTo = bound;
        return this;
    }
}

/// <summary>
/// Fluent configuration of one declared user (see <see cref="IdentitySeedBuilder.User"/>).
/// </summary>
public sealed class IdentitySeedUserBuilder
{
    private readonly List<string> _roles = new();

    internal IdentitySeedUserBuilder(string email) => Email = email;

    internal string Email { get; }
    internal string? UserNameValue { get; private set; }
    internal string? PasswordValue { get; private set; }
    internal IReadOnlyList<string> RolesValue => _roles;

    /// <summary>Sets the user name. Defaults to the e-mail when not called.</summary>
    /// <param name="userName">The user name.</param>
    /// <returns>The builder, for chaining.</returns>
    public IdentitySeedUserBuilder WithUserName(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        UserNameValue = userName;
        return this;
    }

    /// <summary>
    /// Sets the password used when the user is created. When not called the user is created without a
    /// password — an external-login-only account.
    /// </summary>
    /// <param name="password">The password.</param>
    /// <returns>The builder, for chaining.</returns>
    public IdentitySeedUserBuilder WithPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        PasswordValue = password;
        return this;
    }

    /// <summary>
    /// Declares role membership. Additive: calling it twice accumulates. A role named here that was not
    /// declared with <see cref="IdentitySeedBuilder.Role"/> is ensured before the membership is assigned.
    /// </summary>
    /// <param name="roles">Role names.</param>
    /// <returns>The builder, for chaining.</returns>
    public IdentitySeedUserBuilder InRoles(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var r in roles)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(r);
            if (!_roles.Contains(r))
                _roles.Add(r);
        }
        return this;
    }
}
