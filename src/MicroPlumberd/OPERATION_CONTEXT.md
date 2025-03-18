# OperationContext: The flow


## Flow.CommandHandler: UI -> Command -> Domain Service

1. Blazor App UI
2. Calls CommandBus
3. CommandBus saves context information to Command metadata.
4. CommandService reads metadata
5. CommandService creates Scope and pass information, we mark the flow as CommandHandler.
6. CommandExecuter<T> resolves Scope from container. We copy info from AsyncScope.
7. DomainService uses IPlumber/IPlumberInstance (scoped/singleton). 
  - If singleton then we use ThreadAsyncLocalScope, PlumberInstance calls PlumberEngine.
  - If scoped we use OperationContext stored in Plumber, that calls PlumberEngine.

## Flow.Direct: UI -> Domain Service (Scoped)
1. Blazor App UI
2. VM resolves DomainService (Scoped), that resolves IPlumber (scoped), that resolves OperationContext scoped. 
3. Since there is no AsyncLocalScope, we know that this is direct flow.
4. UserId is taken from AuthnticationStateProvider, through an factory - the factory registeres resolve methods based on flow. This is an extension point.
5. Operations are performed on IPlumberd. (Events can be appended). OperationContext is passed from the scope. 
6. CausationId and CorrelationId cannot be aquired, because there is not source of them. They will be generated new.
7. SessionId is taken from the Scope.

## Flow.Direct: UI -> Domain Service (Singleton)

this is rather unusual, because it it makes all calls anonymous. But for simple apps that require high perf, might be handy
TODO...


## Flow.Api: REST/GRPC -> Domain Service (Scoped/Singleton)
1. Token is used for auth.
2. Infra creates service
    - Singleton: we need to use middlewhere to set the AsyncLocalScope.  
    - Scoped: we also use middlewere to set the AsyncLocalScope.
3. When service is resolved, domain service shall be resolved. It will use either IPlumber or IPlumberInstance.
4. With IPlumber, OperationScope will be resolved. But it won't add anything to the context. It will just copy stuff from AsyncThreadLocal at this is set from the middlewere.
5. With IPlumberInstance, no operationscope will be resolved (by design). All operations will pass stuff from AsyncOperationContext.

## Flow.EventHandler: Event was appended, processor reacts, possibly invokes ICommandBus

1. EventHandlerService creates Scope and pass information from EventMetadata. The flow is EventHandler.
2. EventHandlerExecutor resolves Scope from container. We copy data from AsyncScope, we know that this is EventHandler flow.
3. EventHandler is resolved using container:
  - All the information is taken from AsynLocal.Scope 