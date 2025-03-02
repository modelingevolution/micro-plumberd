# MicroPlumberd

MicroPlumberd is a lightweight, convention-based framework for implementing CQRS (Command Query Responsibility Segregation) and Event Sourcing patterns on top of EventStore. It provides a set of abstractions and utilities to simplify working with event-sourced systems in .NET applications.

The name "MicroPlumberd" suggests its main purpose - to provide the "plumbing" necessary to connect your domain model with EventStore, while maintaining a minimal footprint and maximizing developer productivity.

## Features

- 🔄 **Event Sourcing** - Store and retrieve domain events from EventStore
- 📦 **CQRS Implementation** - Separate command and query responsibilities
- 🧩 **Domain Abstractions** - `Aggregate`, `EventHandler`, and other building blocks
- 🚀 **Command Handling** - Command bus, dispatching, and error handling
- 📝 **Projections** - Automatically create and manage EventStore projections
- 📊 **Read Models** - Build and maintain query-optimized views of your data
- 🔒 **Encryption** - Support for encrypting sensitive data
- 🧬 **Code Generation** - Reduce boilerplate through attributes and partial classes
- 🧠 **Convention-Based** - Sensible defaults with flexibility to customize

## Getting Started

### Installation

```bash
dotnet add package MicroPlumberd
dotnet add package MicroPlumberd.Services  # For command handling capabilities
```

### Basic Setup

Add MicroPlumberd to your service collection:

```csharp
// Startup.cs or Program.cs
using MicroPlumberd;
using MicroPlumberd.Services;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Threading;

// Configure services
builder.Services.AddPlumberd(
    // Configure EventStore connection
    settings: EventStoreClientSettings.Create("esdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false"),
    // Optional additional configuration
    configure: (sp, config) => 
    {
        // Optional: Configure custom conventions
        // config.Conventions.GetEventNameConvention = (aggregate, evt) => evt.GetFriendlyName();
        
        // Optional: Enable encryption
        // config.EnableEncryption();
    });

// Add health checks
builder.Services.AddHealthChecks()
    .AddPlumberdHealthChecks();
```

## Core Concepts

### Aggregates

Aggregates are the building blocks of your domain model. They encapsulate business rules and maintain consistency boundaries.

```csharp
[Aggregate(SnapshotEvery = 100)] // Snapshots are optional and should be used sparingly
public partial class CustomerAggregate : AggregateBase<Guid, CustomerAggregate.CustomerState>
{
    public CustomerAggregate(Guid id) : base(id) { }
    
    // Define the state
    public record CustomerState
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public bool IsActive { get; set; }
    }
    
    // Event application methods - must be static and named "Given"
    private static CustomerState Given(CustomerState state, CustomerCreated ev) => 
        state with { Name = ev.Name, Email = ev.Email, IsActive = true };
    
    private static CustomerState Given(CustomerState state, CustomerEmailChanged ev) => 
        state with { Email = ev.Email };
    
    // Command methods
    public static CustomerAggregate Create(Guid id, string name, string email)
    {
        var aggregate = Empty(id);
        aggregate.AppendPendingChange(new CustomerCreated 
        { 
            Name = name, 
            Email = email 
        });
        return aggregate;
    }
    
    public void ChangeEmail(string email)
    {
        if (!State.IsActive)
            throw new InvalidOperationException("Cannot change email of inactive customer");
            
        AppendPendingChange(new CustomerEmailChanged { Email = email });
    }
}

// Event classes
// Each event must have a Guid Id for idempotency
// Events should be immutable
public record CustomerCreated
{
    public Guid Id { get; init; } = Guid.NewGuid(); // Required for idempotency
    public string Name { get; init; } // Use init-only properties for immutability
    public string Email { get; init; }
}

public record CustomerEmailChanged
{
    public Guid Id { get; init; } = Guid.NewGuid(); // Required for idempotency
    public string Email { get; init; }
}
```

**Important notes about aggregates:**
- Use strongly-typed, `IParsable` value types for IDs (Guid is just an example)
- Each event **must** have a Guid Id property for idempotency
- Implement `Given` methods for each event type
- Use snapshots sparingly as they can be a code smell, but may be necessary for performance

### Event Handlers (Read Models)

Event handlers build and maintain read models optimized for queries.

```csharp
[EventHandler]
[OutputStream("CustomerReadModel_v1")]
public partial class CustomerReadModel
{
    private readonly SortedSet<Item> _customers = new(Item.ByLastNameComparer);
    private readonly ConcurrentDictionary<Guid, Item> _customersById = new();
    private readonly ReaderWriterLockSlim _lock = new();
    
    // Internal record for the read model (nested class can simply be named "Item")
    public record Item
    {
        public Guid Id { get; init; }
        public string FirstName { get; init; }
        public string LastName { get; init; }
        public string Email { get; init; }
        public bool IsActive { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        
        // Custom comparer for SortedSet
        public static IComparer<Item> ByLastNameComparer { get; } = 
            Comparer<Item>.Create((a, b) => 
                string.Compare(a.LastName, b.LastName, StringComparison.OrdinalIgnoreCase));
    }
    
    // Event handling methods - must be named "Given"
    private async Task Given(Metadata m, CustomerCreated ev)
    {
        var item = new Item
        {
            Id = m.Id,
            FirstName = ev.Name.Split(' ')[0],
            LastName = ev.Name.Split(' ').Length > 1 ? ev.Name.Split(' ')[1] : "",
            Email = ev.Email,
            IsActive = true,
            CreatedAt = m.Created() ?? DateTimeOffset.Now
        };
        
        _lock.EnterWriteLock();
        try
        {
            _customers.Add(item);
            _customersById[m.Id] = item;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        
        await Task.CompletedTask;
    }
    
    private async Task Given(Metadata m, CustomerEmailChanged ev)
    {
        _lock.EnterWriteLock();
        try 
        {
            if (_customersById.TryGetValue(m.Id, out var existing))
            {
                var updated = existing with { Email = ev.Email };
                
                _customers.Remove(existing);
                _customers.Add(updated);
                _customersById[m.Id] = updated;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        
        await Task.CompletedTask;
    }
    
    // Query methods
    public Item GetCustomer(Guid id)
    {
        _customersById.TryGetValue(id, out var customer);
        return customer;
    }
    
    // Use ImmutableList/Array instead of IEnumerable for thread safety
    public ImmutableList<Item> GetCustomersByLastName()
    {
        _lock.EnterReadLock();
        try
        {
            // Return an immutable copy for thread safety
            return _customers.ToImmutableList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
    
    // For UI binding, can use ObservableCollection
    public ObservableCollection<Item> CustomersForBinding { get; } = new();
}
```

**Important notes about read models:**
- Register in-memory read models as singletons before calling `AddEventHandler`
- Read models should be decoupled from each other
- Use the "snowflake pattern" with:
  - A primary clustered index (like `SortedSet` protected by `ReaderWriterLockSlim`)
  - Lookup dictionaries (`ConcurrentDictionary`)
  - Observable collections for UI binding with MVVM pattern
- Return `ImmutableList/Array` instead of `IEnumerable` for thread safety
- Keep read flows as thin and small as possible
- Design read models to be directly bindable to UI with the MVVM pattern
- It's acceptable and often preferable to duplicate code between read models

### Command Handlers

Command handlers process commands and produce events.

```csharp
[CommandHandler]
public partial class CreateCustomerHandler
{
    private readonly IPlumber _plumber;
    
    public CreateCustomerHandler(IPlumber plumber)
    {
        _plumber = plumber;
    }
    
    // Command handlers should use strongly-typed ID parameters
    public async Task Handle(CustomerId id, CreateCustomer command)
    {
        // Validation
        if (string.IsNullOrEmpty(command.Name))
            throw new ValidationException("Name is required");
            
        if (string.IsNullOrEmpty(command.Email))
            throw new ValidationException("Email is required");
            
        // Create and save aggregate
        var customer = CustomerAggregate.Create(id, command.Name, command.Email);
        await _plumber.SaveNew(customer);
    }
}

// Command class
public class CreateCustomer
{
    // ID is accessed through duck typing - no interface needed
    public CustomerId Id { get; set; } = CustomerId.New();
    public string Name { get; set; }
    public string Email { get; set; }
}

// Strongly-typed ID
public readonly record struct CustomerId : IParsable<CustomerId>
{
    private readonly Guid _value;
    
    private CustomerId(Guid value) => _value = value;
    
    public static CustomerId New() => new(Guid.NewGuid());
    
    public static CustomerId Parse(string s, IFormatProvider provider) => 
        new(Guid.Parse(s));
    
    public static bool TryParse(string s, IFormatProvider provider, out CustomerId result)
    {
        if (Guid.TryParse(s, out var guid))
        {
            result = new CustomerId(guid);
            return true;
        }
        
        result = default;
        return false;
    }
    
    public override string ToString() => _value.ToString();
}
```

**Important notes about command handlers:**
- Use strongly-typed ID parameters (IParsable value types)
- Command IDs are accessed through duck typing (no need to implement interfaces)
- Register handlers in the DI container with `AddCommandHandler<T>()`

### Using the Command Bus

The command bus handles dispatching commands to their handlers. Here's an example using Blazor Server-Side:

```csharp
// CustomerPage.razor.cs
using MicroPlumberd.Services;

public partial class CustomerPage
{
    [Inject]
    private ICommandBus CommandBus { get; set; }
    
    [BindProperty]
    public CustomerViewModel Customer { get; set; } = new();
    
    public string ErrorMessage { get; private set; }
    public bool IsSuccess { get; private set; }
    public Guid? NewCustomerId { get; private set; }
    
    public async Task CreateCustomer()
    {
        try
        {
            var customerId = CustomerId.New();
            NewCustomerId = null;
            ErrorMessage = null;
            IsSuccess = false;
            
            await CommandBus.SendAsync(
                recipientId: customerId,
                command: new CreateCustomer
                {
                    Id = customerId,
                    Name = Customer.Name,
                    Email = Customer.Email
                }
            );
            
            NewCustomerId = customerId;
            IsSuccess = true;
            Customer = new CustomerViewModel(); // Reset form
        }
        catch (FaultException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
    
    public class CustomerViewModel
    {
        public string Name { get; set; }
        public string Email { get; set; }
    }
}
```

## Registration and Configuration

### Registering Components

```csharp
// Register event handlers
// For in-memory event handlers, register as singleton first
builder.Services.AddSingleton<CustomerReadModel>();
builder.Services.AddEventHandler<CustomerReadModel>();

// Alternative for in-memory event handlers (proposed)
// builder.Services.AddInMemoryEventHandler<CustomerReadModel>();

// Register command handlers
builder.Services.AddCommandHandler<CreateCustomerHandler>();
```

### Manually Working with Plumber

For more direct control, you can use `IPlumber` directly:

```csharp
public class CustomerService
{
    private readonly IPlumber _plumber;
    
    public CustomerService(IPlumber plumber)
    {
        _plumber = plumber;
    }
    
    public async Task<CustomerAggregate> GetCustomer(CustomerId id)
    {
        return await _plumber.Get<CustomerAggregate>(id);
    }
    
    public async Task SaveCustomerChanges(CustomerAggregate customer)
    {
        await _plumber.SaveChanges(customer);
    }
}
```

## Best Practices

1. **Strongly-Typed IDs**:
   - Use `IParsable` value types for all IDs
   - Avoid using primitive types like string or Guid directly

2. **Read Model Design**:
   - Keep read models focused and specialized
   - Use the "snowflake pattern" (clustered index + lookups)
   - Protect mutable collections with appropriate concurrency mechanisms
   - Don't be afraid to duplicate code between read models

3. **Aggregate Design**:
   - Keep aggregates small and focused
   - Use snapshots sparingly
   - Implement business rules at the aggregate level

4. **Event Design**:
   - Events must be immutable (use records or classes with init-only properties)
   - Always include a Guid Id property on every event for idempotency
   - Include only the data needed to describe what happened
   - Name events in past tense (e.g., `CustomerCreated`, not `CreateCustomer`)

5. **Command Design**:
   - Include validation rules
   - Use strongly-typed IDs
   - Consider using command validation attributes

## Advanced Topics

### Encryption

MicroPlumberd supports encryption of sensitive data:

```csharp
// Setup
builder.Services.AddEncryption();
builder.Services.AddPlumberd(settings, (sp, config) => {
    config.EnableEncryption();
});

// Usage - encrypt sensitive data
public record CustomerPersonalData
{
    public SecretObject<string> SocialSecurityNumber { get; set; }
    public SecretObject<string> TaxIdentifier { get; set; }
}
```

### Subscriptions

```csharp
// Subscribe to a stream
var subscription = _plumber.Subscribe(
    "CustomerStream", 
    FromRelativeStreamPosition.Start
);

await subscription.WithHandler<CustomerReadModel>();

// Persistent subscriptions
await _plumber.SubscribeEventHandlerPersistently<CustomerReadModel>();
```

### Projection Management

```csharp
// Create a join projection
await _plumber.TryCreateJoinProjection(
    "CustomerOrders",
    new[] { "CustomerCreated", "OrderPlaced" }
);

// Create a projection for a specific handler
await _plumber.TryCreateJoinProjection<CustomerOrderHandler>();
```

## License

Copyright © Rafal Maciag. All rights reserved.