namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Configuration options for controlling user registration policy.
/// </summary>
public class RegistrationPolicyOptions
{
    /// <summary>
    /// Whether anonymous (unauthenticated) users can register new accounts.
    /// When false, only authenticated users with appropriate permissions can create new users.
    /// Default: true (allow self-registration)
    /// </summary>
    public bool AllowAnonymousRegistration { get; set; } = true;

    /// <summary>
    /// The role required to register new users when anonymous registration is disabled.
    /// If null or empty, any authenticated user can register new users.
    /// Example: "Admin"
    /// Default: null
    /// </summary>
    public string? RequiredRoleForRegistration { get; set; }
}
