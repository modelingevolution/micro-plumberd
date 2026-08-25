using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// One <c>AddIdentitySeed</c> (or <c>AddIdentityInitializer</c>) call, kept in DI so several calls accumulate
/// in registration order (feature-001 design.md §1).
/// </summary>
internal sealed class IdentitySeedDeclaration
{
    private readonly Action<IdentitySeedBuilder>? _configure;

    /// <summary>Creates a declaration from an explicit fluent configuration.</summary>
    public IdentitySeedDeclaration(Action<IdentitySeedBuilder> configure)
        => _configure = configure ?? throw new ArgumentNullException(nameof(configure));

    private IdentitySeedDeclaration() => IsFromOptions = true;

    /// <summary>
    /// True for the legacy <c>AddIdentityInitializer</c> adapter: its contribution is read from
    /// <see cref="IdentityInitializerOptions"/> at run time, because consumers configure it with
    /// <c>services.Configure(...)</c> after the call.
    /// </summary>
    public bool IsFromOptions { get; }

    /// <summary>Creates the legacy adapter declaration (R7).</summary>
    public static IdentitySeedDeclaration FromOptions() => new();

    /// <summary>Applies this declaration to the shared builder.</summary>
    public void Apply(IdentitySeedBuilder builder, IServiceProvider sp)
    {
        if (!IsFromOptions)
        {
            _configure!(builder);
            return;
        }

        var o = sp.GetRequiredService<IOptions<IdentityInitializerOptions>>().Value;
        if (!o.SeedAdminUser)
            return; // R7: SeedAdminUser = false contributes nothing => empty seed => Ready immediately.

        builder.Role(o.AdminRoleName)
            .User(o.AdminEmail, u => u
                .WithUserName(o.AdminUserName)
                .WithPassword(o.AdminPassword)
                .InRoles(o.AdminRoleName))
            .WaitUpTo(o.ProjectionWaitTime);
    }
}

/// <summary>
/// Singleton that turns the accumulated <see cref="IdentitySeedDeclaration"/>s into an ordered, immutable plan.
/// Built lazily on first use inside <see cref="IdentityInitializerService"/> so options configured after the
/// registration call are honoured.
/// </summary>
internal sealed class IdentitySeedPlan
{
    private readonly IEnumerable<IdentitySeedDeclaration> _declarations;
    private readonly IServiceProvider _sp;
    private readonly Lock _gate = new();
    private IReadOnlyList<IdentitySeedStep>? _steps;
    private TimeSpan _waitUpTo = IdentitySeedBuilder.DefaultWaitUpTo;

    public IdentitySeedPlan(IEnumerable<IdentitySeedDeclaration> declarations, IServiceProvider sp)
    {
        _declarations = declarations;
        _sp = sp;
    }

    /// <summary>The per-attempt readiness bound of the built plan. Valid after <see cref="Build"/>.</summary>
    public TimeSpan WaitUpTo => _waitUpTo;

    /// <summary>Builds (once) and returns the ordered plan.</summary>
    public IReadOnlyList<IdentitySeedStep> Build()
    {
        lock (_gate)
        {
            if (_steps is not null) return _steps;

            var builder = new IdentitySeedBuilder();
            foreach (var d in _declarations)
                d.Apply(builder, _sp);

            _waitUpTo = builder.WaitUpToValue;
            return _steps = builder.Steps.ToArray();
        }
    }
}
