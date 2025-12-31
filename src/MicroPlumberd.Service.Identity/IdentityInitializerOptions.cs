namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Configuration options for the identity initializer service that handles
/// initial admin user seeding on application startup.
/// </summary>
public class IdentityInitializerOptions
{
    /// <summary>
    /// The email address for the initial admin user.
    /// Default: "admin@localhost"
    /// </summary>
    public string AdminEmail { get; set; } = "admin@localhost";

    /// <summary>
    /// The username for the initial admin user.
    /// Default: "admin"
    /// </summary>
    public string AdminUserName { get; set; } = "admin";

    /// <summary>
    /// The password for the initial admin user.
    /// Should be overridden in production via configuration or environment variables.
    /// Default: "admin"
    /// </summary>
    public string AdminPassword { get; set; } = "admin";

    /// <summary>
    /// The name of the admin role to create and assign to the admin user.
    /// Default: "Admin"
    /// </summary>
    public string AdminRoleName { get; set; } = "Admin";

    /// <summary>
    /// Time to wait for projections to catch up before checking for users.
    /// This delay ensures read models are populated after EventStore starts.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan ProjectionWaitTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to seed the admin user on startup if no users exist.
    /// Set to false to disable automatic admin seeding.
    /// Default: true
    /// </summary>
    public bool SeedAdminUser { get; set; } = true;
}
