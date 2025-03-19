# Command Handler Flow


1. AddCommandHandler
2. CommandHandlerStarter subscribes with EventHandlerExecutorAdapter
3. Creates internal ICommandHandleExecutor<THandle> for lifetime management
    
    1. Can be CommandHandlerScopedExecutor or CommandHandlerSingletonExecutor
    2. Supports Validation, Exception handling and User Identity propagation

4. Dispatches flow to ICommandHandler<TCommand> by TCommand and can be decorated.