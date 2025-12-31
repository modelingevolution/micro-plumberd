using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Service that evaluates whether a user is allowed to register new accounts
/// based on the configured registration policy.
/// </summary>
public class RegistrationPolicyService
{
    private readonly RegistrationPolicyOptions _options;

    public RegistrationPolicyService(IOptions<RegistrationPolicyOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Determines whether the specified user is allowed to register new accounts.
    /// </summary>
    /// <param name="user">The ClaimsPrincipal of the current user, or null for anonymous users.</param>
    /// <returns>True if registration is allowed; otherwise, false.</returns>
    public bool CanRegister(ClaimsPrincipal? user)
    {
        // If anonymous registration is allowed, anyone can register
        if (_options.AllowAnonymousRegistration)
            return true;

        // Anonymous users cannot register when AllowAnonymousRegistration is false
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        // If no specific role is required, any authenticated user can register
        if (string.IsNullOrEmpty(_options.RequiredRoleForRegistration))
            return true;

        // Check if user has the required role
        return user.IsInRole(_options.RequiredRoleForRegistration);
    }

    /// <summary>
    /// Gets whether anonymous registration is currently allowed.
    /// </summary>
    public bool AllowAnonymousRegistration => _options.AllowAnonymousRegistration;

    /// <summary>
    /// Gets the role required for registration, or null if no specific role is required.
    /// </summary>
    public string? RequiredRoleForRegistration => _options.RequiredRoleForRegistration;
}
