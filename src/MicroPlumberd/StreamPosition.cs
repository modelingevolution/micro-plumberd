using EventStore.Client;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MicroPlumberd;

/// <summary>
/// Represents a position in a specific event stream
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<StreamVersion>))]
public readonly record struct StreamVersion : IParsable<StreamVersion>, IComparable<StreamVersion>
{
    /// <summary>
    /// Gets the name of the stream.
    /// </summary>
    public string StreamName { get; }

    /// <summary>
    /// Gets the version number (position) in the stream.
    /// </summary>
    public long Version { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamVersion"/> struct.
    /// </summary>
    /// <param name="streamName">The name of the stream.</param>
    /// <param name="version">The version number in the stream (must be >= -1).</param>
    /// <exception cref="ArgumentException">Thrown when streamName is empty or version is less than -1.</exception>
    public StreamVersion(string streamName, long version)
    {
        if (string.IsNullOrWhiteSpace(streamName))
            throw new ArgumentException("Stream name cannot be empty", nameof(streamName));

        if (version < -1)
            throw new ArgumentException("Version cannot be less than -1", nameof(version));

        StreamName = streamName;
        Version = version;
    }

    /// <summary>
    /// Creates a new stream version with the version updated from the provided metadata.
    /// </summary>
    /// <param name="metadata">The metadata containing the new stream position.</param>
    /// <returns>A new <see cref="StreamVersion"/> with the updated version.</returns>
    public StreamVersion With(Metadata metadata)
    {

        return new StreamVersion(
            StreamName,
            metadata.SourceStreamPosition);
    }

    /// <summary>
    /// Returns a string representation of the stream version in the format "streamName:version".
    /// </summary>
    /// <returns>A string in the format "streamName:version".</returns>
    public override string ToString() => $"{StreamName}:{Version}";

    /// <summary>
    /// Parses a string representation of a stream version.
    /// </summary>
    /// <param name="s">The string to parse in the format "streamName:version".</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <returns>The parsed <see cref="StreamVersion"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when the input string is empty.</exception>
    /// <exception cref="FormatException">Thrown when the format is invalid.</exception>
    public static StreamVersion Parse(string s, IFormatProvider? provider = null)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("Input string cannot be empty", nameof(s));

        var parts = s.Split(':');
        if (parts.Length != 2)
            throw new FormatException("Stream position must be in format 'streamName:version'");

        if (!long.TryParse(parts[1], out var version))
            throw new FormatException("Version must be a valid long integer");

        return new StreamVersion(parts[0], version);
    }

    /// <summary>
    /// Tries to parse a string representation of a stream version.
    /// </summary>
    /// <param name="s">The string to parse in the format "streamName:version".</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <param name="result">The parsed result if successful, otherwise default.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise, <c>false</c>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out StreamVersion result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(s))
            return false;

        var parts = s.Split(':');
        if (parts.Length != 2)
            return false;

        if (!long.TryParse(parts[1], out var version))
            return false;

        result = new StreamVersion(parts[0], version);
        return true;
    }

    /// <summary>
    /// Compares this stream version to another, first by stream name then by version number.
    /// </summary>
    /// <param name="other">The other stream version to compare to.</param>
    /// <returns>A value indicating the relative order of the objects being compared.</returns>
    public int CompareTo(StreamVersion other)
    {
        var streamNameComparison = string.Compare(StreamName, other.StreamName, StringComparison.Ordinal);
        if (streamNameComparison != 0)
            return streamNameComparison;

        return Version.CompareTo(other.Version);
    }
}
/// <summary>
/// Represents a composite version of multiple event streams
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<CompositeStreamVersion>))]
public readonly record struct CompositeStreamVersion : IParsable<CompositeStreamVersion>, IEquatable<CompositeStreamVersion>
{
    /// <summary>
    /// Gets the immutable sorted dictionary of stream positions keyed by stream name.
    /// </summary>
    public ImmutableSortedDictionary<string, long> Positions { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeStreamVersion"/> struct from a collection of stream versions.
    /// </summary>
    /// <param name="positions">The collection of stream versions to include.</param>
    public CompositeStreamVersion(IEnumerable<StreamVersion> positions)
    {
        Positions = positions?.ToImmutableSortedDictionary(x=>x.StreamName, x=>x.Version) ?? ImmutableSortedDictionary<string,long>.Empty;
    }

    /// <summary>
    /// Determines whether the current composite version equals another composite version.
    /// </summary>
    /// <param name="other">The other composite version to compare.</param>
    /// <returns><c>true</c> if the versions are equal; otherwise, <c>false</c>.</returns>
    public bool Equals(CompositeStreamVersion? other)
    {
        if (other == null) return false;
        return Positions.SequenceEqual(other.Value.Positions);
    }

    /// <summary>
    /// Creates an empty composite version
    /// </summary>
    public static CompositeStreamVersion Empty => new(ImmutableSortedSet<StreamVersion>.Empty);

    /// <summary>
    /// Gets the version for a specific stream
    /// </summary>
    public long GetVersionFor(string streamName) => Positions[streamName];

    /// <summary>
    /// Creates a new composite version with the specified stream position updated
    /// </summary>
    public CompositeStreamVersion WithPosition(StreamVersion position)
    {
        if (Positions.ContainsKey(position.StreamName))
        {
            var updatedPositions = Positions.SetItem(position.StreamName, position.Version);
            return new CompositeStreamVersion() { Positions = updatedPositions };
        }
        else
            return new CompositeStreamVersion() { Positions = Positions.Add(position.StreamName, position.Version) };

    }

    /// <summary>
    /// Returns a string representation of the composite version, with individual stream versions separated by dots.
    /// </summary>
    /// <returns>A string representation of all stream versions, or an empty string if there are no positions.</returns>
    public override string ToString() => Positions.IsEmpty ? string.Empty : string.Join('.', this.Positions.Select(x=> new StreamVersion(x.Key, x.Value)));

    /// <summary>
    /// Parses a string representation of a composite stream version.
    /// </summary>
    /// <param name="s">The string to parse, with stream versions separated by dots.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <returns>The parsed <see cref="CompositeStreamVersion"/>, or <see cref="Empty"/> if the string is empty.</returns>
    public static CompositeStreamVersion Parse(string s, IFormatProvider? provider = null)
    {
        if (string.IsNullOrWhiteSpace(s))
            return Empty;

        var parts = s.Split('.');
        var positions = new List<StreamVersion>();

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            positions.Add(StreamVersion.Parse(part, provider));
        }

        return new CompositeStreamVersion(positions);
    }

    /// <summary>
    /// Tries to parse a string representation of a composite stream version.
    /// </summary>
    /// <param name="s">The string to parse, with stream versions separated by dots.</param>
    /// <param name="provider">An optional format provider (not used).</param>
    /// <param name="result">The parsed result if successful, otherwise <see cref="Empty"/>.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise, <c>false</c>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CompositeStreamVersion result)
    {
        result = Empty;

        if (string.IsNullOrWhiteSpace(s))
            return true; // Empty string is a valid empty composite version

        var parts = s.Split('.');
        var positions = new List<StreamVersion>();

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            if (!StreamVersion.TryParse(part, provider, out var position))
                return false;

            positions.Add(position);
        }

        result = new CompositeStreamVersion(positions);
        return true;
    }
}
/// <summary>
/// Extension methods for working with CompositeStreamVersion
/// </summary>
public static class CompositeStreamVersionExtensions
{
    /// <summary>
    /// Updates a composite version with the metadata from an event
    /// </summary>
    /// <typeparam name="TEvent">The type of event</typeparam>
    /// <param name="CompositeStreamVersion">The current composite version</param>
    /// <param name="metadata">The metadata from the event</param>
    /// <param name="plumber">The plumber instance to access conventions</param>
    /// <returns>An updated composite version</returns>
    public static CompositeStreamVersion With<TEvent>(
        this CompositeStreamVersion CompositeStreamVersion,
        Metadata metadata,
        IPlumber plumber)
    {
        
        if (plumber == null)
            throw new ArgumentNullException(nameof(plumber));

        // Get stream name from metadata or determine from event type if not present
        string streamName = plumber.Config.Conventions.GetEventNameConvention(null, typeof(TEvent));

        // Create stream position with the stream name and version from metadata
        var position = new StreamVersion(streamName, metadata.SourceStreamPosition);

        // Update the composite version with the new position
        return CompositeStreamVersion.WithPosition(position);
    }

    
}