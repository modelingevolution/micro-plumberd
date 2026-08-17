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
    /// Per-attempt readiness bound; maps onto <see cref="IdentitySeedBuilder.WaitUpTo"/>.
    /// It is not a delay: the seed starts as soon as the identity read models report
    /// <see cref="ICaughtUpHandler.CaughtUp"/>. This value bounds one attempt — how long it may wait for that
    /// catch-up, and how long each write may take to become visible in the read model the next step reads.
    /// Expiry fails the attempt (logged at Error, retried with backoff), never the host.
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
