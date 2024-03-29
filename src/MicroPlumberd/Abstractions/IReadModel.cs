﻿using EventStore.Client;

namespace MicroPlumberd;

public delegate Task EventDispatcher(Metadata m, object evt);
public interface IEventHandler<TOwner> : IEventHandler 
where TOwner:IEventHandler{ }

public interface IEventHandler
{
    Task Handle(Metadata m, object ev);
}
public interface IReadModel : IEventHandler
{
    Task Given(Metadata m, object ev);
}
public interface ISubscriptionSet
{
    ISubscriptionSet With<TModel>(TModel model)
        where TModel : IEventHandler, ITypeRegister;

    Task SubscribePersistentlyAsync(string outputStream, string? groupName = null);
    Task SubscribeAsync(string name, FromStream start);
}