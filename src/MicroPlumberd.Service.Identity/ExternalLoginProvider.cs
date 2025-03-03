using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MicroPlumberd.Services.Identity;

/// <summary>
/// Identifies an external login provider
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<ExternalLoginProvider>))]
public readonly record struct ExternalLoginProvider : IParsable<ExternalLoginProvider>, IComparable<ExternalLoginProvider>
{
    public string Name { get; }

    public ExternalLoginProvider(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Provider name cannot be empty", nameof(name));

        Name = name.ToLowerInvariant();
    }

        

    public override string ToString() => Name;

    // Common providers
    public static ExternalLoginProvider Google => new("Google");
    public static ExternalLoginProvider Microsoft => new("Microsoft");
    public static ExternalLoginProvider Facebook => new("Facebook");
    public static ExternalLoginProvider Twitter => new("Twitter");
    public static ExternalLoginProvider Parse(string s, IFormatProvider? provider)
    {
        return new ExternalLoginProvider(s);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ExternalLoginProvider result)
    {
        result = new ExternalLoginProvider(s);
        return true;
    }

    public int CompareTo(ExternalLoginProvider other)
    {
        return string.Compare(Name, other.Name, StringComparison.Ordinal);
    }
}