using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

public delegate bool TypeEventConverter(string type, out Type t);
public interface ITypeRegister
{
    static abstract IEnumerable<Type> Types { get; }
    
}

public interface IAggregate : IId, IVersioned
{
    IReadOnlyList<object> PendingEvents { get; }
    Task Rehydrate(IAsyncEnumerable<object> events);
    void AckCommitted();
}


public interface IVersioned
{
    long Version { get; }
}

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



public interface IAggregate<out TSelf> : IAggregate
{
    static abstract TSelf New(Guid id);
   
}