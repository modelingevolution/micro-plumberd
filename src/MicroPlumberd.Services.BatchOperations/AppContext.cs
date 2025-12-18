// NOTE: AppInstance, AppContext, IAppContextProvider, and AppContextProvider<T> types
// are candidates for extraction to a separate shared library (e.g., MicroPlumberd.Abstractions
// or EventPi.Abstractions). They are currently duplicated here from the platform to avoid
// a circular dependency. When extracted, update this package to reference the shared library
// and remove these type definitions.
//
// These types are used for orphan detection - tracking which application instance started
// a batch operation so that orphaned operations (from crashed app instances) can be detected
// and cleaned up when the app restarts with a new session.

using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.BatchOperations;

/// <summary>
/// Represents an application instance with name, version, host, and node information.
/// </summary>
/// <param name="Name">The application name.</param>
/// <param name="Version">The application version.</param>
/// <param name="Host">The host machine name.</param>
/// <param name="Node">The node number (optional, default 0).</param>
[JsonConverter(typeof(AppInstanceJsonConverter))]
public readonly record struct AppInstance(string Name, string Version, string Host, uint Node = 0) : IParsable<AppInstance>
{
    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Name}:{Version}/{Host}.{Node}";
    }

    /// <summary>
    /// Parses a string to an AppInstance.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">Format provider (unused).</param>
    /// <returns>The parsed AppInstance.</returns>
    /// <exception cref="FormatException">Thrown when the string cannot be parsed.</exception>
    public static AppInstance Parse(string s, IFormatProvider? provider = null)
    {
        if (TryParse(s, provider, out var result))
        {
            return result;
        }

        throw new FormatException($"Could not parse '{s}' as an AppInstance.");
    }

    /// <summary>
    /// Tries to parse a string to an AppInstance.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">Format provider (unused).</param>
    /// <param name="result">The parsed AppInstance.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParse(string? s, IFormatProvider? provider, out AppInstance result)
    {
        result = default;

        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        try
        {
            // Parse in format "Name:Version/Host.Node"
            // Split by '/' first
            var hostSplit = s.Split('/');
            if (hostSplit.Length != 2)
            {
                return false;
            }

            // Split left part by ':'
            var nameSplit = hostSplit[0].Split(':');
            if (nameSplit.Length != 2)
            {
                return false;
            }

            string name = nameSplit[0];
            string version = nameSplit[1];

            // Split right part by '.'
            var nodeSplit = hostSplit[1].Split('.');
            string host = nodeSplit[0];
            uint node = 0;

            // Node is optional, default to 0
            if (nodeSplit.Length > 1 && !uint.TryParse(nodeSplit[1], out node))
            {
                return false;
            }

            result = new AppInstance(name, version, host, node);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Represents the application context including instance information and session ID.
/// </summary>
/// <param name="AppInstance">The application instance information.</param>
/// <param name="AppSession">The unique session identifier.</param>
public readonly record struct AppContext(AppInstance AppInstance, Guid AppSession)
{
    /// <summary>
    /// An empty application context.
    /// </summary>
    public static readonly AppContext Empty = new AppContext(new AppInstance(string.Empty, string.Empty, string.Empty, 0), Guid.Empty);
}

/// <summary>
/// Provides access to the current application context.
/// </summary>
public interface IAppContextProvider
{
    /// <summary>
    /// Gets the current application context.
    /// </summary>
    AppContext Context { get; }
}

/// <summary>
/// Default implementation of IAppContextProvider that derives application information from a type.
/// </summary>
/// <typeparam name="T">The type used to determine application metadata.</typeparam>
public class AppContextProvider<T> : IAppContextProvider
{
    private readonly Func<uint>? _nodeProvider;
    private readonly Guid _session = Guid.NewGuid();
    private AppContext? _context;

    /// <summary>
    /// Creates a new AppContextProvider.
    /// </summary>
    /// <param name="nodeProvider">Optional function to provide the node number.</param>
    public AppContextProvider(Func<uint>? nodeProvider = null)
    {
        _nodeProvider = nodeProvider;
    }

    /// <inheritdoc />
    public AppContext Context
    {
        get
        {
            if (_context.HasValue) return _context.Value;
            var assemblyName = typeof(T).Assembly.GetName();
            _context = new AppContext(
                new AppInstance(
                    assemblyName.Name ?? typeof(T).Name,
                    assemblyName.Version?.ToString() ?? "0.0.0",
                    Environment.MachineName,
                    _nodeProvider?.Invoke() ?? 0),
                _session);
            return _context.Value;
        }
    }
}
