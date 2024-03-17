using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd;

public delegate bool TypeEventConverter(string type, out Type t);
public interface ITypeRegister
{
    static abstract IReadOnlyDictionary<string, Type> TypeRegister { get; }
    
}

public interface IAggregate : IId, IVersioned
{
    IReadOnlyList<object> PendingEvents { get; }
    Task Rehydrate(IAsyncEnumerable<object> events);
    void AckCommitted();
}


public record ExecutionContext(Metadata Metadata, object Event, Guid Id, ICommandRequest? Command, Exception Exception);

public record CommandInvocationFailed
{
    public Guid RecipientId { get; init; }
    public ICommandRequest Command { get; init; }
    public string Message { get; init; }
}

public interface ICommandRequest
{
    Guid RecipientId { get; }
    object Command { get; }
}
public interface ICommandRequest<out TCommand> : ICommandRequest
{
   new TCommand Command { get; }
}

public static class CommandRequest
{
    public static ICommandRequest Create(Guid recipientId, object command)
    {
        var t = typeof(CommandRequest<>).MakeGenericType(command.GetType());
        return (ICommandRequest)Activator.CreateInstance(t, recipientId, command);
    }
    public static ICommandRequest<TCommand> Create<TCommand>(Guid recipientId, TCommand command)
    {
        return new CommandRequest<TCommand>(recipientId, command);
    }
}

public record CommandRequest<TCommand> : ICommandRequest<TCommand>
{
    internal CommandRequest(Guid recipientId, TCommand command)
    {
        this.RecipientId = recipientId;
        this.Command = command;
    }
    public TCommand Command { get; init; }
    public Guid RecipientId { get; init; }
    object ICommandRequest.Command => Command;
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
public interface IProcessManager : IEventHandler, IVersioned, IId
{
    static abstract Type StartEvent { get; }
    Task<ICommandRequest?> HandleError(ExecutionContext executionContext);
    Task<ICommandRequest?> When(Metadata m, object evt);
    Task<ICommandRequest> StartWhen(Metadata m, object evt);
}

public interface ICommandEnqueued
{
    object Command { get; }
    Guid RecipientId { get; }
}
public static class CommandEnqueued
{
    public static ICommandEnqueued Create(Guid recipient, object command)
    {
        var type = typeof(CommandEnqueued<>).MakeGenericType(command.GetType());
        //TODO: Slow, should cache ctor.
        var cmd = (ICommandEnqueued)Activator.CreateInstance(type, recipient, command);
       
        return cmd;
    }
}
sealed class CommandEnqueued<TCommand>(Guid recipientId, TCommand command) : ICommandEnqueued
{
    object ICommandEnqueued.Command => command;
    public TCommand Command => command;
    public Guid RecipientId => recipientId;
}


public interface ICommandBus
{
    Task SendAsync(Guid recipientId, object command);
}

public interface IAggregate<out TSelf> : IAggregate
{
    static abstract TSelf New(Guid id);
   
}