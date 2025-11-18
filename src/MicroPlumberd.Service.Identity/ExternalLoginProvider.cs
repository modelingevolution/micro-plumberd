using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Identifies an external authentication provider (e.g., Google, Microsoft, Facebook).
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<ExternalLoginProvider>))]
public readonly record struct ExternalLoginProvider : IParsable<ExternalLoginProvider>, IComparable<ExternalLoginProvider>
{
    /// <summary>
    /// Gets the name of the external login provider.
    /// </summary>
    /// <value>A normalized lowercase string representing the provider name.</value>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalLoginProvider"/> struct.
    /// </summary>
    /// <param name="name">The provider name. Cannot be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
    public ExternalLoginProvider(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Provider name cannot be empty", nameof(name));

        Name = name.ToLowerInvariant();
    }

    /// <summary>
    /// Returns the string representation of the external login provider.
    /// </summary>
    /// <returns>The provider name.</returns>
    public override string ToString() => Name;

    /// <summary>
    /// Gets the Google external login provider.
    /// </summary>
    public static ExternalLoginProvider Google => new("Google");

    /// <summary>
    /// Gets the Microsoft external login provider.
    /// </summary>
    public static ExternalLoginProvider Microsoft => new("Microsoft");

    /// <summary>
    /// Gets the Facebook external login provider.
    /// </summary>
    public static ExternalLoginProvider Facebook => new("Facebook");

    /// <summary>
    /// Gets the Twitter external login provider.
    /// </summary>
    public static ExternalLoginProvider Twitter => new("Twitter");

    /// <summary>
    /// Parses a string into an <see cref="ExternalLoginProvider"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <returns>A new <see cref="ExternalLoginProvider"/> instance.</returns>
    public static ExternalLoginProvider Parse(string s, IFormatProvider? provider)
    {
        return new ExternalLoginProvider(s);
    }

    /// <summary>
    /// Attempts to parse a string into an <see cref="ExternalLoginProvider"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <param name="result">The parsed <see cref="ExternalLoginProvider"/> if successful.</param>
    /// <returns>Always returns true.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ExternalLoginProvider result)
    {
        result = new ExternalLoginProvider(s);
        return true;
    }

    /// <summary>
    /// Compares this external login provider to another provider.
    /// </summary>
    /// <param name="other">The external login provider to compare to.</param>
    /// <returns>A value indicating the relative order of the providers.</returns>
    public int CompareTo(ExternalLoginProvider other)
    {
        return string.Compare(Name, other.Name, StringComparison.Ordinal);
    }
}