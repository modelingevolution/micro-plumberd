using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Reports <see cref="IdentityInitializerService.State"/> live (feature-001 requirement R5): Unhealthy naming the
/// current step or the last error until the seed converged, Healthy afterwards.
/// </summary>
internal sealed class IdentitySeedHealthCheck : IHealthCheck
{
    private readonly IdentityInitializerService _service;

    public IdentitySeedHealthCheck(IdentityInitializerService service) => _service = service;

    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = _service.State;
        var data = new Dictionary<string, object> { ["attempts"] = state.Attempts };
        if (state.LastError is not null)
            data["lastError"] = state.LastError;

        return Task.FromResult(state.Ready
            ? HealthCheckResult.Healthy(state.Description, data)
            : HealthCheckResult.Unhealthy(state.Description, data: data));
    }
}

/// <summary>
/// Registration of the opt-in identity seed health check.
/// </summary>
public static class IdentityHealthCheckExtensions
{
    /// <summary>
    /// Adds the identity seed health check. Opt-in and untagged, so a patch upgrade never adds a
    /// <c>/health</c> entry to a consumer that did not ask for one (ruling 2 of the requirements).
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">Health entry name. Defaults to <c>identity</c>.</param>
    /// <returns>The health checks builder for method chaining.</returns>
    public static IHealthChecksBuilder AddIdentitySeedHealthCheck(this IHealthChecksBuilder builder, string name = "identity")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ContainerExtensions.RegisterSeedRunner(builder.Services);
        builder.AddTypeActivatedCheck<IdentitySeedHealthCheck>(name);
        return builder;
    }
}
