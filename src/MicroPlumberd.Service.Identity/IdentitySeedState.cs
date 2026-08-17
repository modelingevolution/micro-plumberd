namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Immutable snapshot of the identity seed's progress, published by
/// <see cref="IdentityInitializerService.State"/> and read live by the optional
/// <c>identity</c> health check (feature-001 design.md §2, requirement R5).
/// </summary>
/// <param name="Ready">True once the declared seed converged (or nothing was declared).</param>
/// <param name="Description">Human readable description of what the seed is doing, or why it is not ready.</param>
/// <param name="Attempts">Number of attempts started so far. 0 when nothing was declared.</param>
/// <param name="LastError">Message of the last failure, or null if no attempt has failed.</param>
public sealed record IdentitySeedState(bool Ready, string Description, int Attempts, string? LastError = null);
