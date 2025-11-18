using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

/// <summary>
/// A named delegate used for recognizing event type from a string.
/// </summary>
/// <param name="type">The type.</param>
/// <param name="t">The t.</param>
/// <returns></returns>
public delegate bool TypeEventConverter(string type, out Type t);

/// <summary>
/// ITypeRegister is used to indicate which event types are supported by the class.
/// </summary>
public interface ITypeRegister
{
    /// <summary>
    /// Supported event types.
    /// </summary>
    /// <value>
    /// Supported types.
    /// </value>
    static abstract IEnumerable<Type> Types { get; }
    
}


/// <summary>
/// Interface for an aggregate.
/// </summary>
/// <seealso cref="MicroPlumberd.IId" />
/// <seealso cref="MicroPlumberd.IVersioned" />
public interface IAggregate : IVersioned
{
    /// <summary>
    /// Pending events.
    /// </summary>
    /// <value>
    /// The pending events.
    /// </value>
    IReadOnlyList<object> PendingEvents { get; }

    /// <summary>
    /// Rehydrates aggregate with specified events.
    /// </summary>
    /// <param name="events">The events.</param>
    /// <returns></returns>
    Task Rehydrate(IAsyncEnumerable<object> events);

    /// <summary>
    /// Is used when all pending events are saved in eventstore.
    /// </summary>
    void AckCommitted();
}

/// <summary>
/// Simple interface for versioned objects.
/// </summary>
public interface IVersioned
{
    /// <summary>
    /// Gets the current version.
    /// </summary>
    /// <value>
    /// The version.
    /// </value>
    long Version { get; }
}


/// <summary>
/// Interface for increasing the version.
/// </summary>
/// <seealso cref="MicroPlumberd.IVersioned" />
public interface IVersionAware : IVersioned
{
    /// <summary>
    /// Gets or sets the current version number.
    /// </summary>
    /// <value>The version number.</value>
    long Version { get; set; }

    /// <summary>
    /// Increases the version number by one.
    /// </summary>
    void Increase() => this.Version += 1;
}
static class Extensions
{
    public static string Remove(this string t, string word) => t.Replace(word, string.Empty);
    public static byte[] ToHash(this string t)
    {
        using (SHA256 shA256 = SHA256.Create())
        {
            byte[] hash = shA256.ComputeHash(Encoding.Default.GetBytes(t));
            ulong uint64_1 = BitConverter.ToUInt64(hash, 0);
            ulong uint64_2 = BitConverter.ToUInt64(hash, 8);
            ulong uint64_3 = BitConverter.ToUInt64(hash, 16);
            ulong uint64_4 = BitConverter.ToUInt64(hash, 24);
            ulong num1 = uint64_1 ^ uint64_3;
            ulong num2 = uint64_2 ^ uint64_4;
            Memory<byte> memory = new Memory<byte>(new byte[16]);
            BitConverter.TryWriteBytes(memory.Span, num1);
            BitConverter.TryWriteBytes(memory.Slice(8, 8).Span, num2);
            return memory.ToArray();
        }
    }

    public static Guid ToGuid(this string t) => new Guid(t.ToHash());

    public static byte[] NameHash(this Type t1)
    {
        return _hashCache.GetOrAdd(t1, (Func<Type, byte[]>)(t2 => t2.FullName.ToHash()));
    }
    private static ConcurrentDictionary<Type, byte[]> _hashCache = new ConcurrentDictionary<Type, byte[]>();

    public static Guid NameId(this Type t) => new Guid(t.NameHash());
}
/// <summary>
/// Represents an object that has an identifier.
/// </summary>
public interface IId
{
    /// <summary>
    /// Gets the identifier of this object.
    /// </summary>
    /// <value>The identifier, which can be of any type.</value>
    object Id { get; }

    /// <summary>
    /// Gets the identifier as a <see cref="Guid"/>.
    /// </summary>
    /// <value>
    /// The identifier converted to a <see cref="Guid"/>. Returns <see cref="Guid.Empty"/> if the identifier is null,
    /// or the identifier itself if it's already a <see cref="Guid"/>, otherwise converts the string representation to a deterministic <see cref="Guid"/>.
    /// </value>
    Guid Uuid
    {
        get
        {
            if(Id is Guid g) return g;
            if(Id == null) return Guid.Empty;
            return Id.ToString().ToGuid();
        }
    }
}


/// <summary>
/// Represents an object that has a strongly-typed identifier.
/// </summary>
/// <typeparam name="T">The type of the identifier, which must implement <see cref="IParsable{T}"/>.</typeparam>
public interface IId<out T>  : IId
    where T:IParsable<T>
{
    /// <summary>
    /// Gets the strongly-typed identifier of this object.
    /// </summary>
    /// <value>The identifier of type <typeparamref name="T"/>.</value>
    new T Id { get; }

    /// <summary>
    /// Gets the identifier as an object.
    /// </summary>
    object IId.Id => Id;
}

/// <summary>
/// Represents an object that has a mutable identifier.
/// </summary>
public interface IIdAware
{
    /// <summary>
    /// Gets or sets the identifier of this object.
    /// </summary>
    /// <value>The identifier, which can be of any type.</value>
    object Id { get; set; }
}

/// <summary>
/// Represents an object that has a strongly-typed mutable identifier.
/// </summary>
/// <typeparam name="T">The type of the identifier, which must implement <see cref="IParsable{T}"/>.</typeparam>
public interface IIdAware<T> : IIdAware
    where T : IParsable<T>
{
    /// <summary>
    /// Gets or sets the strongly-typed identifier of this object.
    /// </summary>
    /// <value>The identifier of type <typeparamref name="T"/>.</value>
    new T Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier as an object.
    /// </summary>
    object IIdAware.Id
    {
        get => Id;
        set => Id = (T)value;
    }
}

/// <summary>
/// Generic version of IAggregate interface that contains factory method.
/// </summary>
/// <typeparam name="TSelf">The type of the self.</typeparam>
/// <seealso cref="MicroPlumberd.IVersioned" />
public interface IAggregate<out TSelf> : IAggregate
{
    /// <summary>
    /// Factory method to create new Aggregates based on identifiers.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <returns></returns>
    static abstract TSelf Empty(object id);
   
}