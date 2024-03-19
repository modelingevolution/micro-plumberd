﻿using MicroPlumberd.DirectConnect;

namespace MicroPlumberd.Services;

class CommandHandlerStarter<THandler>(IPlumber plumber) : ICommandHandlerStarter
    where THandler : ICommandHandler, IServiceTypeRegister
{
    public async Task Start()
    {
        await plumber.SubscribeCommandHandler<THandler>();
    }

    public IEnumerable<Type> CommandTypes => THandler.CommandTypes;
    public Type HandlerType => typeof(THandler);
}