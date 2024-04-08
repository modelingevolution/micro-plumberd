using System;
using System.Diagnostics.CodeAnalysis;
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
public interface IAggregate : IId, IVersioned
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

public interface IId
{
    public Guid Id { get; }
}

public interface IIdAware
{
    Guid Id { set; }
}


/// <summary>
/// Generic version of IAggregate interface that contains factory method.
/// </summary>
/// <typeparam name="TSelf">The type of the self.</typeparam>
/// <seealso cref="MicroPlumberd.IId" />
/// <seealso cref="MicroPlumberd.IVersioned" />
public interface IAggregate<out TSelf> : IAggregate
{
    /// <summary>
    /// Factory method to create new Aggregates based on Guids.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <returns></returns>
    static abstract TSelf New(Guid id);
   
}