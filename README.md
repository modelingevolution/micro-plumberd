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
[![MicroPlumberd.Services.Uniqueness.Postgres](https://img.shields.io/nuget/v/MicroPlumberd.Services.Uniqueness.Postgres.svg)](https://www.nuget.org/packages/MicroPlumberd.Services.Uniqueness.Postgres/)
[![MicroPlumberd.Services.Uniqueness.Sqlite](https://img.shields.io/nuget/v/MicroPlumberd.Services.Uniqueness.Sqlite.svg)](https://www.nuget.org/packages/MicroPlumberd.Services.Uniqueness.Sqlite/)
[![MicroPlumberd.Services.Uniqueness.SqlServer](https://img.shields.io/nuget/v/MicroPlumberd.Services.Uniqueness.SqlServer.svg)](https://www.nuget.org/packages/MicroPlumberd.Services.Uniqueness.SqlServer/)
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
var settings = KurrentDBClientSettings.Create(connectionString);
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
    
### MetadataFactory

`MetadataFactory` centralizes `Metadata` construction with proper JSON schema. Available via `plumber.MetadataFactory` or standalone with `new MetadataFactory()` (uses default conventions).

```csharp
// From an event object — conventions compute sourceStreamId and enrich metadata
var metadata = plumber.MetadataFactory.Create(context, evt, recipientId);

// With explicit sourceStreamId — for tests and simple scenarios
var factory = new MetadataFactory();
var metadata = factory.Create($"Category-{id}", created: DateTimeOffset.Now, userId: "admin");

// Typed — conventions compute sourceStreamId from event type
var metadata = factory.Create<OrderCreated, Guid>(orderId, evt, created: DateTimeOffset.Now);
```

The factory ensures `Metadata.StreamId<T>()`, `Metadata.Created()`, `Metadata.UserId()` and other accessors work correctly by building the proper JSON `Data` schema.

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

### Uniqueness (set-wide constraints)

An event store enforces invariants **within one stream**. Uniqueness — "no two companies share a tax id" —
spans streams, and there is no stream whose optimistic-concurrency check can express it. KurrentDB is not
designed for it, and the usual workaround (one stream per value, append-at-NoStream) does not survive
contact with reality: a *released* value's stream still exists, so it can never be re-reserved, and you get
one stream per possible value forever.

So put the constraint where constraints belong: a **relational unique index**, as a sidecar to the
aggregate write. `MicroPlumberd.Services.Uniqueness` implements the **reservation pattern**
(try / confirm / cancel).

```
1. Reserve(name, source, lease)   INSERT {name, source, validUntil, confirmed=false}
                                  the UNIQUE index arbitrates; losers learn who won
2. persist the aggregate to the event store
3. Confirm(source)                confirmed=true (permanent)
   on failure at 2: RollbackReservation(source)
```

**The lease is the point.** If the process dies between 1 and 3 the row is left unconfirmed and expires,
so the name frees itself — no distributed transaction, and a crash costs the lease duration, not the name.

#### Install

The core package is provider-neutral; add exactly one provider package. A dialect is **required** — it
supplies both the DDL and the database clock.

```
dotnet add package MicroPlumberd.Services.Uniqueness.Postgres    # recommended for a real server
dotnet add package MicroPlumberd.Services.Uniqueness.Sqlite      # light + serverless, shared volume
dotnet add package MicroPlumberd.Services.Uniqueness.SqlServer
```

#### Use

```csharp
// A category is just a marker type. It names its own table, and several categories may share one database.
record CompanyNip;

services.AddUniquenessPostgres<CompanyNip>(connectionString);   // table "CompanyNip"
services.AddUniquenessPostgres<InvoiceNumber>(connectionString); // table "InvoiceNumber", same database
```

```csharp
public partial class CompanyCommandHandler(IPlumberInstance plumber, IUniqueNameReservation<CompanyNip> unique)
{
    public async Task Handle(Guid id, RegisterCompany cmd)
    {
        var agg = CompanyAggregate.Empty(id);
        agg.Register(cmd);                       // validate BEFORE reserving

        await unique.Reserve(cmd.Nip, id);       // throws UniqueNameConflictException (its Code maps to 409)
        try
        {
            await plumber.SaveNew(agg);
        }
        catch
        {
            await unique.RollbackReservation(id);
            throw;
        }
        await unique.Confirm(id);                // see the warning below
    }
}
```

`TryReserve` is the non-throwing overload. `DeleteConfirmedNameReservation(source)` releases a confirmed
name; `Confirm` releases the source's previous name automatically when it renames.

#### ⚠️ If `Confirm` throws, you MUST compensate

**The lease buys liveness, not safety.** If a caller stalls past its lease, another source may take the
name — and the stalled caller's aggregate write can still land. Its `Confirm` then fails, but the aggregate
is *already persisted*. That failure is your signal to compensate (reject / soft-delete). It cannot be
fixed by reordering: `Reserve → Confirm → SaveNew` is strictly worse, because a crash between Confirm and
SaveNew burns the name **permanently** (confirmed rows never expire).

Size the lease to swamp your worst-case persist time. The default is 10 minutes against a sub-second
append: a stall that long means a dead process.

#### Choosing a provider

| Provider | When |
|---|---|
| **PostgreSQL** | Default for a real server. |
| **SQLite** | Light and serverless. **Requires a shared volume, and all instances MUST be on the SAME HOST** (docker named volume / bind mount — one kernel). Verified: 16 separate processes racing one file yield exactly one winner. **Never put the file on NFS/SMB or any cross-host share** — SQLite's locking is unreliable there and the failure mode is *database corruption*, which is worse than the bug this library prevents. WAL is enabled, which also makes that boundary structural: WAL needs shared memory, so it cannot work cross-host. |
| **SQL Server** | Supported. The dialect pins `COLLATE Latin1_General_100_BIN2` — SQL Server's *default* collation is case-INsensitive, which would otherwise make `"abc"` and `"ABC"` collide there but not on the other providers. |

#### Names are compared exactly

Comparison is **ordinal/binary on every provider**. There is no per-category case sensitivity: that would
be per-provider collation configuration, and normalisation is domain knowledge the library must not guess
(`ToLowerInvariant` and culture-aware casing disagree on the Turkish dotless i; Unicode folding has choices
only you can make). **Normalise before calling** if you want case-insensitive uniqueness.

#### This is NOT for document numbering

Numbering (`FV-12222/07/2024`) is a different problem: you **mint** the value rather than validating
someone else's, there is one counter per series rather than an unbounded candidate set, and the series key
is a real domain entity. Model it as an **aggregate** — the id is the template *without* the counter, the
sequence is state, and the versioned append gives you atomicity and gaplessness for free. No index, no
lease, no second store.

> Rule of thumb: if you would be creating **one stream per possible value**, you are not modelling an
> entity — you are building a lock, so use the index. A bounded domain entity is an aggregate.

#### `[Unique<T>]` / `IUniqueFrom<,>` — declarative only, NOT implemented

These types exist and are **inert**: nothing reads them. There is no source-generated wiring; reservation
is performed by calling `IUniqueNameReservation<T>` explicitly, as above. Do not decorate an event with
`[Unique<T>]` and assume anything is enforced.

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


