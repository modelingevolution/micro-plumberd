using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// A composite key for looking up users by external login provider and key.
/// The provider name is always normalized to uppercase for consistent lookups.
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<ExternalLoginLookupKey>))]
public readonly record struct ExternalLoginLookupKey : IParsable<ExternalLoginLookupKey>, IComparable<ExternalLoginLookupKey>
{
    private const char Separator = '|';

    /// <summary>
    /// Gets the normalized provider name (uppercase).
    /// </summary>
    public string ProviderName { get; }

    /// <summary>
    /// Gets the provider-specific key.
    /// </summary>
    public string ProviderKey { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalLoginLookupKey"/> struct.
    /// </summary>
    /// <param name="providerName">The provider name. Will be normalized to uppercase.</param>
    /// <param name="providerKey">The provider-specific key.</param>
    /// <exception cref="ArgumentException">Thrown when providerName or providerKey is null or whitespace.</exception>
    public ExternalLoginLookupKey(string providerName, string providerKey)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider name cannot be empty", nameof(providerName));
        if (string.IsNullOrWhiteSpace(providerKey))
            throw new ArgumentException("Provider key cannot be empty", nameof(providerKey));

        ProviderName = providerName.ToUpperInvariant();
        ProviderKey = providerKey;
    }

    /// <summary>
    /// Creates a lookup key from an ExternalLoginProvider and ExternalLoginKey.
    /// </summary>
    public static ExternalLoginLookupKey From(ExternalLoginProvider provider, ExternalLoginKey key)
    {
        return new ExternalLoginLookupKey(provider.Name, key.Value);
    }

    /// <summary>
    /// Returns the string representation of the lookup key (ProviderName|ProviderKey).
    /// </summary>
    public override string ToString() => $"{ProviderName}{Separator}{ProviderKey}";

    /// <summary>
    /// Parses a string in the format "ProviderName|ProviderKey" into an <see cref="ExternalLoginLookupKey"/>.
    /// The provider name will be normalized to uppercase.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <returns>A new <see cref="ExternalLoginLookupKey"/> instance.</returns>
    /// <exception cref="FormatException">Thrown when the string is not in the correct format.</exception>
    public static ExternalLoginLookupKey Parse(string s, IFormatProvider? provider)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new FormatException("Cannot parse empty string to ExternalLoginLookupKey");

        var separatorIndex = s.IndexOf(Separator);
        if (separatorIndex < 0)
            throw new FormatException($"Invalid ExternalLoginLookupKey format. Expected 'ProviderName{Separator}ProviderKey'");

        var providerName = s[..separatorIndex];
        var providerKey = s[(separatorIndex + 1)..];

        return new ExternalLoginLookupKey(providerName, providerKey);
    }

    /// <summary>
    /// Attempts to parse a string into an <see cref="ExternalLoginLookupKey"/>.
    /// The provider name will be normalized to uppercase.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <param name="result">The parsed <see cref="ExternalLoginLookupKey"/> if successful.</param>
    /// <returns>True if parsing succeeded; otherwise, false.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ExternalLoginLookupKey result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(s))
            return false;

        var separatorIndex = s.IndexOf(Separator);
        if (separatorIndex < 0)
            return false;

        var providerName = s[..separatorIndex];
        var providerKey = s[(separatorIndex + 1)..];

        if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(providerKey))
            return false;

        result = new ExternalLoginLookupKey(providerName, providerKey);
        return true;
    }

    /// <summary>
    /// Compares this lookup key to another.
    /// </summary>
    public int CompareTo(ExternalLoginLookupKey other)
    {
        var providerComparison = string.Compare(ProviderName, other.ProviderName, StringComparison.Ordinal);
        if (providerComparison != 0)
            return providerComparison;

        return string.Compare(ProviderKey, other.ProviderKey, StringComparison.Ordinal);
    }
}
