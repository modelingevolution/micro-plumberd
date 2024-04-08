using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
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
    void Increase();
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
public interface IId
{
    object Id { get; }

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


public interface IId<out T>  : IId
    where T:IParsable<T>
{
    T Id { get; }
    object IId.Id => Id;
}

public interface IIdAware
{
    object Id { set; }
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