# micro-plumberd
Micro library for EventStore, CQRS and EventSourcing.
Just eXtreamly simple.

## NuGet Packages

### Core Packages
[![MicroPlumberd](https://img.shields.io/nuget/v/MicroPlumberd.svg)](https://www.nuget.org/packages/MicroPlumberd/)
[![MicroPlumberd.SourceGenerators](https://img.shields.io/nuget/v/MicroPlumberd.SourceGenerators.svg)](https://www.nuget.org/packages/MicroPlumberd.SourceGenerators/)
[![MicroPlumberd.Testing](https://img.shields.io/nuget/v/MicroPlumberd.Testing.svg)](https://www.nuget.org/packages/MicroPlumberd.Testing/)

### Service Packages
[![MicroPlumberd.Services](https://img.shields.io/nuget/v/MicroPlumberd.Services.svg)](https://www.nuget.org/packages/MicroPlumberd.Services/)
[![MicroPlumberd.CommandBus.Abstractions](https://img.shields.io/nuget/v/MicroPlumberd.CommandBus.Abstractions.svg)](https://www.nuget.org/packages/MicroPlumberd.CommandBus.Abstractions/)
[![MicroPlumberd.Services.Cron](https://img.shields.io/nuget/v/MicroPlumberd.Services.Cron.svg)](https://www.nuget.org/packages/MicroPlumberd.Services.Cron/)
[![MicroPlumberd.Services.Cron.Ui](https://img.shields.io/nuget/v/MicroPlumberd.Services.Cron.Ui.svg)](https://www.nuget.org/packages/MicroPlumberd.Services.Cron.Ui/)

### Process Manager
[![MicroPlumberd.ProcessManager.Abstractions](https://img.shields.io/nuget/v/MicroPlumberd.ProcessManager.Abstractions.svg)](https://www.nuget.org/packages/MicroPlumberd.ProcessManager.Abstractions/)
[![MicroPlumberd.Services.ProcessManagers](https://img.shields.io/nuget/v/MicroPlumberd.Services.ProcessManagers.svg)](https://www.nuget.org/packages/MicroPlumberd.Services.ProcessManagers/)

### Additional Services
[![MicroPlumberd.Encryption](https://img.shields.io/nuget/v/MicroPlumberd.Encryption.svg)](https://www.nuget.org/packages/MicroPlumberd.Encryption/)
[![MicroPlumberd.Protobuf](https://img.shields.io/nuget/v/MicroPlumberd.Protobuf.svg)](https://www.nuget.org/packages/MicroPlumberd.Protobuf/)
[![MicroPlumberd.Services.Uniqueness](https://img.shields.io/nuget/v/MicroPlumberd.Services.Uniqueness.svg)](https://www.nuget.org/packages/MicroPlumberd.Services.Uniqueness/)
[![MicroPlumberd.Services.Grpc.DirectConnect](https://img.shields.io/nuget/v/MicroPlumberd.Services.Grpc.DirectConnect.svg)](https://www.nuget.org/packages/MicroPlumberd.Services.Grpc.DirectConnect/)
[![MicroPlumberd.Services.Identity](https://img.shields.io/nuget/v/MicroPlumberd.Services.Identity.svg)](https://www.nuget.org/packages/MicroPlumberd.Services.Identity/)

---

Quick "how to" section is [here](#quick-how-to-section)
Documentation can be found here:
[MicroPlumberd Documentation](https://modelingevolution.github.io/micro-plumberd/)

## Getting started

### Install nugets: 

```powershell
dotnet add package MicroPlumberd                      # For your domain
dotnet add package MicroPlumberd.Services             # For IoC integration and CommandBus
dotnet add package MicroPlumberd.SourceGenerators     # Code generators for Aggregates, EventHandlers and more.
```

### Configure plumber

```csharp
// Vanilla
string connectionString = $"esdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";
var settings = EventStoreClientSettings.Create(connectionString);
var plumber = Plumber.Create(settings);
```

However, typicly you would add plumberd to your app:
```csharp
services.AddPlumberd();
```

## Features

### State

Suppose you want to save some small "state" to your EventStoreDB. For example. current configuration of your Raspherry PI Camera. You can expect that state would be dependend only on previous state.

```csharp
record class CameraConfiguration : IVersionAware: {
    public int Shutter {get;set;}
    public float Contrast {get;set;}
    // ...
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Version { get; set; } = -1;
}

// To save the state:
var state = new CameraConfiguration { /* ... */ };
plumber.AppendState(state); // because CameraConfiguration implements IVersionAware, 
                            // optimistic concurrency check will be performed.

// To retrive latest state:
var id = state.Id; // We need to have Id from somewhere...
var actual = plumber.GetState<CameraConfiguration>(id);
```

### Aggregates

Event-sourced aggregates are the guardians of transaction. They encapsulate object(s) that we want to threat in isolation to 
the rest of the world because we want its data to be consistent. 

Event-sourced aggregates are "rehydrated" from history (its stream) every time we need them. This means that theirs streams should be relatively short ~1K events max, to accomplish this usually you would you "close-the-books" pattern. 

For performance reasons, sometimes you would want to have "snapshots". Snapshots are saved in related snapshot stream. When you want to retrive an aggregate:

1) plumber would try to read latest event from shaphost stream.
2) retrive and apply all the events that were from latest snahshot till now.

```csharp
[Aggregate(SnapshotEvery = 50)]
public partial class FooAggregate(Guid id) : AggregateBase<Guid,FooAggregate.FooState>(id)
{
    public record FooState { public string Name { get; set; } };
    private static FooState Given(FooState state, FooCreated ev) => state with { Name = ev.Name };
    private static FooState Given(FooState state, FooRefined ev) => state with { Name =ev.Name };
    public void Open(string msg) => AppendPendingChange(new FooCreated() { Name = msg });
    public void Change(string msg) => AppendPendingChange(new FooRefined() { Name = msg });
}
// And events:
public record FooCreated { public string? Name { get; set; } }
public record FooRefined { public string? Name { get; set; } }
```
Comments:

- State is encapsulated in nested class FooState. 
- Given methods, that are used when loading aggregate from the EventStoreDB are private and static. State is encouraged to be immutable.
- [Aggregate] attribute is used by **SourceGenerator** that will generate dispatching code and handy metadata.

2) Consume an aggregate.

If you want to create a new aggregate and save it to EventStoreDB:
```csharp

FooAggregate aggregate = FooAggregate.New(Guid.NewGuid());
aggregate.Open("Hello");

await plumber.SaveNew(aggregate);

```

If you want to load aggregate from EventStoreDB, change it and save back to EventStoreDB

```csharp
var aggregate = await plumber.Get<FooAggregate>("YOUR_ID");
aggregate.Change("World");
await plumber.SaveChanges(aggregate);
```

### Write a read-model/processor

1) Read-Models
```csharp
[EventHandler]
public partial class FooModel
{
    private async Task Given(Metadata m, FooCreated ev)
    {
        // your code
    }
    private async Task Given(Metadata m, FooRefined ev)
    {
         // your code
    }
}
```

Comments:

- ReadModels have private async Given methods. Since they are async, you can invoke SQL here, or othere APIs to store your model.
- Metadata contains standard stuff (Created, CorrelationId, CausationId), but can be reconfigured.

```csharp
var fooModel = new FooModel();
var sub= await plumber.SubscribeEventHandler(fooModel);

// or if you want to persist progress of your subscription
var sub2= await plumber.SubscribeEventHandlerPersistently(fooModel);
```

With **SubscribeModel** you can subscribe from start, from certain moment or from the end of the stream. If you want to use DI and have your model as a scoped one, you can configure plumber at the startup and don't need to invoke SubscribeEventHandler manually.
Here you have an example with EF Core.

```csharp
// Program.cs
services
    .AddPlumberd()
    .AddEventHandler<FooModel>();

// FooModel.cs
[EventHandler]
public partial class FooModel : DbContext
{
    private async Task Given(Metadata m, FooCreated ev)
    {
        // your code
    }
    private async Task Given(Metadata m, FooRefined ev)
    {
         // your code
    }
    // other stuff, DbSet... etc...
}
```

2) Processors

```csharp
[EventHandler]
public partial class FooProcessor(IPlumber plumber)
{
    private async Task Given(Metadata m, FooRefined ev)
    {
        var agg = FooAggregate.New(Guid.NewGuid());
        agg.Open(ev.Name + " new");
        await plumber.SaveNew(agg);
    }
}
```

Implementing a processor is technically the same as implementing a read-model, but inside the Given method you would typically invoke a command or execute an aggregate. A process would subscribe persistently. 

### Read-Model with EF (EntityFramework)

Let's analyse this example:

1. You create a read-model that subscribes persistently.
2. You subscribe it with plumber.
3. You changed something in the event and want to see the new model.
4. Instead of re-creating old read-model, you can easily create new one. Just change MODEL_VER to reflect new version.

*Please note that Sql schema create/drop auto-generation script will be covered in a different article. (For now we leave it for developers.)*

Comments:
- By creating a new read-model you can always compare the differences with the previous one.
- You can leverage canary-deployment strategy and have 2 versions of your system running in parallel.

```csharp
[OutputStream(FooModel.MODEL_NAME)]
[EventHandler]
public partial class FooModel : DbContext
{
    internal const string MODEL_VER = "_v1";
    internal const string MODEL_NAME = $"FooModel{MODEL_VER}";
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
           .Entity<FooEntity>()
           .ToTable($"FooEntities{MODEL_VER}");
    }
    private async Task Given(Metadata m, FooCreated ev)
    {
        // your code
    }
    private async Task Given(Metadata m, FooRefined ev)
    {
        // your code
    }
}
```

### Read-Model with LiteDB

With LiteDB you can easily achive the same without a hassle of schema recreation.


```csharp
[OutputStream(DbReservationModel.MODEL_NAME)]
[EventHandler]
public partial class DbReservationModel(LiteDatabase db)
{
    internal const string MODEL_VER = "_v2";
    internal const string MODEL_NAME = $"Reservations{MODEL_VER}";
    public ILiteCollection<Reservation> Reservations { get; } = db.Reservations();

    private async Task Given(Metadata m, TicketReserved ev)
    {
        Reservations.Insert(new Reservation() { RoomName = ev.RoomName, MovieName = ev.MovieName });
        
    }
}

public static class DbExtensions
{
    public static ILiteCollection<Reservation> Reservations(this LiteDatabase db) => db.GetCollection<Reservation>(DbReservationModel.MODEL_NAME);
}
public record Reservation
{
    public ObjectId ReservationId { get; set; }
    public string RoomName { get; set; }
    public string MovieName { get; set; }
}

/// Configure your app:
services.AddEventHandler<DbReservationModel>(true)

```

### Command-Handlers & Message Bus

If you want to start as quickly as possible, you can start with EventStoreDB as command-message-bus.
```csharp

services.AddPlumberd()
        .AddCommandHandler<FooCommandHandler>()

// on the client side:
ICommandBus bus; // from DI
bus.SendAsync(Guid.NewGuid(), new CreateFoo() { Name = "Hello" });
```

#### Scaling considerations
If you are running many replicas of your service, you need to switch command-execution to persistent mode:

```csharp

services.AddPlumberd(configure: c => c.Conventions.ServicesConventions().AreHandlersExecutedPersistently = () => true)
        .AddCommandHandler<FooCommandHandler>()

```
This means, that once your microservice subscribes to commands, it will execute all. So if your service is down, and commands are saved, once your service is up, they will be executed.
To skip old commands, you can configure a filter.

```csharp
services.AddPlumberd(configure: c => {
    c.Conventions.ServicesConventions().AreHandlersExecutedPersistently = () => true;
    c.Conventions.ServicesConventions().CommandHandlerSkipFilter = (m,ev) => DateTimeOffset.Now.Substract(m.Created()) > TimeSpan.FromSeconds(60);
    })
    .AddCommandHandler<FooCommandHandler>()
```
    
### Conventions
  - SteamNameConvention - from aggregate type, and aggregate id
  - EventNameConvention - from aggregate? instance and event instance
  - MetadataConvention - to enrich event with metadata based on aggregate instance and event instance
  - EventIdConvention - from aggregate instance and event instance
  - OutputStreamModelConvention - for output stream name from model-type
  - GroupNameModelConvention - for group name from model-type


### Subscription Sets
  - You can easily create a stream that joins events together by event-type, and subscribe many read-models at once. Here it is named 'MasterStream', which is created out of events used to create DimentionLookupModel and MasterModel.
  - In this way, you can easily manage the composition and decoupling of read-models. You can nicely composite your read-models. And if you don't wish to decouple read-models, you can reuse your existing one. 


/// Given simple models, where master-model has foreign-key used to obtain value from dimentionLookupModel

```csharp
var dimentionTable = new DimentionLookupModel();
var factTable = new MasterModel(dimentionTable);

await plumber.SubscribeSet()
    .With(dimentionTable)
    .With(factTable)
    .SubscribeAsync("MasterStream", FromStream.Start);
```

### Integration tests support

### Specflow/Ghierkin step-files generation

Given you have written your domain, you can generate step files that would populate Ghierkin API to your domain. 


### EXPERIMENTAL GRPC Direct communication

If you'd like to use direct dotnet-dotnet communication to execute command-handlers install MicroPlumberd.DirectConnect

```powershell

dotnet add package MicroPlumberd.Services.Grpc.DirectConnect
```


If you prefer direct communication (like REST-API, but without the hassle for contract generation/etc.) you can use direct communication where client invokes command handle using grpc.
Command is not stored in EventStore.

```csharp
/// Let's configure server:
services.AddCommandHandler<FooCommandHandler>().AddServerDirectConnect();

/// Add mapping to direct-connect service
app.MapDirectConnect();
```

Here is an example of a command handler code:

```csharp
[CommandHandler]
public partial class FooCommandHandler(IPlumber plumber)
{

    [ThrowsFaultException<BusinessFault>]
    public async Task Handle(Guid id, CreateFoo cmd)
    {
        if (cmd.Name == "error")
            throw new BusinessFaultException("Foo");

        var agg = FooAggregate.New(id);
        agg.Open(cmd.Name);

        await plumber.SaveNew(agg);
    }

    [ThrowsFaultException<BusinessFault>]
    public async Task<HandlerOperationStatus> Handle(Guid id, ChangeFoo cmd)
    {
        if (cmd.Name == "error")
            throw new BusinessFaultException("Foo");

        var agg = await plumber.Get<FooAggregate>(id);
        agg.Change(cmd.Name);

        await plumber.SaveChanges(agg);
        return HandlerOperationStatus.Ok();
    }
}
```

And how on the client side:
```csharp
service.AddClientDirectConnect().AddCommandInvokers();

// And invocation
 var clientPool = sp.GetRequiredService<IRequestInvokerPool>();
 var invoker = clientPool.Get("YOUR_GRPC_URL");
 await invoker.Execute(Guid.NewId(), new CreateFoo(){});
```

### EXPERIMENTAL Process-Manager

Given diagram:
![Saga](./pm.png)

```powershell
# Add required packages:
dotnet add package MicroPlumberd.Services.ProcessManagers
```

The code of Order Process Manager looks like this:

```csharp
// Let's configure stuff beforehand
services.AddPlumberd(eventStoreConfig)
    .AddCommandHandler<OrderCommandHandler>() // handles PlaceOrder command.
    .AddProcessManager<OrderProcessManager>();

// And process manager.
[ProcessManager]
public class OrderProcessManager(IPlumberd plumberd)
{
    public async Task<ICommandRequest<MakeReservation>> StartWhen(Metadata m, OrderCreated e) 
    {
        return CommandRequest.Create(Guid.NewId(), new MakeReservation());
    }
    public async Task<ICommandRequest<MakePayment>> When(Metadata m, SeatsReserved e)
    {
        return CommandRequest.Create(Guid.NewId(), new MakePayment());
    }
    public async Task When(Metadata m, PaymentAccepted e)
    {
        var order = await plumberd.Get<Order>(this.Id);
        order.Confirm();
        await plumberd.SaveChanges(order);
    }
    // Optional
    private async Task Given(Metadata m, OrderCreated v){
        // this will be used to rehydrate state of process-manager
        // So that when(SeatsReserved) you can adjust the response.
    }
    // Optional 2
    private async Task Given(Metadata m, CommandEnqueued<MakeReservation> e){
        // same here.
    }
}

```

### EXPERIMENTAL Uniqueness support

Uniqueness support in EventSourcing is not out-of-the-box, especially in regards to EventStoreDB. You can use some "hacks" but at the end of the day, you want uniqueness to be enforced by some kind of database. EventStoreDB is not designed for that purpose. 

However, you can leverage typical reservation patterns. At the moment the library supports only the first option:

- At domain-layer, a domain-service usually would enforce uniqueness. This commonly requires a round-trip to a database. So just before actual event(s) are saved in a stream, a check against uniqueness constraints should be evaluated - thus reservation is made. When the event is appended to the stream, a confirmation is done automatically (on db).

- At a app-layer, command-handler would typically reserve a name. And when aggregate, which is being executed by the handler, saves its events successfully, then the reservation is confirmed. If the handler fails, then the reservation is deleted. Seems simple? Under the hood, it is not that simple, because what if the process is terminated while the command-handler is executing? We need to make sure, that we can recover successfully from this situation.

Let's see the API proposal:

```csharp
// Let's define unique-category name
record FooCategory;


public class FooCreated 
    // and apply it to one fo the columns.
    [Unique<FooCategory>]
    public string? Name { get; set; }
    
    // other stuff   
}
```

For complex types, we need more flexibility.

```csharp
// Let's define unique-category name, this will be mapped to columns in db
// If you'd opt for domain-layer enforcment, you need to change commands to events.
record BooCategory(string Name, string OtherName) : IUniqueFrom<BooCategory, BooCreated>, IUniqueFrom<BooCategory, BooRefined>
{
    public static BooCategory From(BooCreated x) => new(x.InitialName, x.OtherName);
    public static BooCategory From(BooRefined x) => new(x.NewName, x.OtherName);
}

[Unique<BooCategory>]
public record BooCreated(string InitialName, string OtherName);

[Unique<BooCategory>]
public record BooRefined(string NewName, string OtherName);
```

# How-to

All "How to's" are in [MicroPlumber.Tests](https://github.com/modelingevolution/micro-plumberd/tree/master/src/MicroPlumberd.Tests/) projects, so you can easily run it!

## How to append event to its default stream

Example event:
```csharp
[OutputStream("ReservationStream")]
public record TicketReserved { 
    public string? MovieName { get; set; } 
    public string? RoomName { get; set; }
}
```
Code:
```csharp
public async Task HowToAppendEventToHisDefaultStream(IPlumber plumber)
{
 var ourLovelyEvent = new TicketReserved();
 var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
 await plumber.AppendEvent(ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
}
```
## How to append event to specific stream

Code:
```csharp
public async Task HowToAppendEventToSpecificStream(IPlumber plumber)
{
  var streamIdentifier = Guid.NewGuid();
  var ourLovelyEvent = new TicketReserved();

  await plumber.AppendEventToStream($"VIPReservationStream-{streamIdentifier}", ourLovelyEvent);
}
```

## How to subscribe a model

Model:
```csharp
[OutputStream("ReservationModel_v1")]
[EventHandler]
public partial class ReservationModel(InMemoryModelStore assertionModelStore)
{
    public InMemoryModelStore ModelStore => assertionModelStore;
    public bool EventHandled{ get; set; } = false;
    private async Task Given(Metadata m, TicketReserved ev)
    {
        EventHandled = true;
        assertionModelStore.Given(m, ev);
        await Task.Delay(0);
    }
   

}
```

Code:
```csharp
public async Task HowToMakeAModelSubscribe(IPlumber plumber)
{
    var fromWhenShouldWeSubscribeOurStream = FromRelativeStreamPosition.Start;
    var modelThatWantToSubscribeToStream = new ReservationModel(new InMemoryModelStore());
          
    await plumber.SubscribeEventHandler(modelThatWantToSubscribeToStream, start: fromWhenShouldWeSubscribeOurStream);

    var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
    var ourLovelyEvent = new TicketReserved();

    await plumber.AppendEvent(ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
    await Task.Delay(1000);

    modelThatWantToSubscribeToStream.EventHandled.Should().BeTrue();
}
```

## How to make a model subscribe but from last event

Code:
```csharp 
public async Task HowToMakeAModelSubscribeButFromLastEvent(IPlumber plumber)
{
    var modelThatWantToSubscribeToStream = new ReservationModel(new InMemoryModelStore());
    var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
    var someVeryOldEvent = new TicketReserved();

    await plumber.AppendEvent(someVeryOldEvent, suffixOfStreamWhereOurEventWillBeAppend);
    await Task.Delay(1000);

    await plumber.SubscribeEventHandler(modelThatWantToSubscribeToStream, start: FromRelativeStreamPosition.End-1);
    modelThatWantToSubscribeToStream.EventHandled.Should().BeFalse();

    var ourLovelyEvent = new TicketReserved();

    await plumber.AppendEvent(ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
    await Task.Delay(1000);
    modelThatWantToSubscribeToStream.EventHandled.Should().BeTrue();
}
```
## How to link events to another stream

Projection:
```csharp
[EventHandler]
public partial class TicketProjection(IPlumber plumber)
{
    private async Task Given(Metadata m, TicketReserved ev)
    {
        await plumber.AppendLink($"RoomOccupancy-{ev.RoomName}", m);
    }
}
```
Model:

```csharp
[EventHandler]
public partial class RoomOccupancy
{
    public int HowManySeatsAreReserved { get; set; }
    private async Task Given(Metadata m, TicketReserved ev)
    {
        HowManySeatsAreReserved++;
    }
}
```
Code:
```csharp
public async Task HowToLinkEventsToAnotherStream(IPlumber plumber)
{
var ourLovelyEvent = new TicketReserved()
{
    MovieName = "Scream",
    RoomName = "Venus"
};
await plumber.SubscribeEventHandlerPersistently(new TicketProjection(plumber));
await plumber.AppendEvent(ourLovelyEvent);

await plumber.Subscribe($"RoomOccupancy-{ourLovelyEvent.RoomName}", FromRelativeStreamPosition.Start)
    .WithHandler(new RoomOccupancy());
}
```

## How to rehydrate model (run all events again)

Model:
```csharp
[OutputStream("ReservationModel_v1")]
[EventHandler]
public partial class ReservationModel(InMemoryModelStore assertionModelStore)
{
    public InMemoryModelStore ModelStore => assertionModelStore;
    public bool EventHandled{ get; set; } = false;
    private async Task Given(Metadata m, TicketReserved ev)
    {
        EventHandled = true;
        assertionModelStore.Given(m, ev);
        await Task.Delay(0);
    }
   

}
```

Code:
```csharp
public async Task HowToRehydrateModel(IPlumber plumber)
{
   var modelThatWantToSubscribeToStream = new ReservationModel(new InMemoryModelStore());
   var suffixOfStreamWhereOurEventWillBeAppend = Guid.NewGuid();
   var ourLovelyEvent = new TicketReserved();

   await plumber.AppendEvent( ourLovelyEvent, suffixOfStreamWhereOurEventWillBeAppend);
   await Task.Delay(1000);

   await plumber.SubscribeEventHandler(modelThatWantToSubscribeToStream, start: FromRelativeStreamPosition.Start);

   await Task.Delay(1000);

   modelThatWantToSubscribeToStream.EventHandled.Should().BeTrue();
   modelThatWantToSubscribeToStream.EventHandled = false;
   modelThatWantToSubscribeToStream.EventHandled.Should().BeFalse();

   await plumber.Rehydrate(modelThatWantToSubscribeToStream,
       $"ReservationStream-{suffixOfStreamWhereOurEventWillBeAppend}");

   modelThatWantToSubscribeToStream.EventHandled.Should().BeTrue();
}
```


