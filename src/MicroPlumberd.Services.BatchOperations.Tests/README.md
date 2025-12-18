# Unit Testing MicroPlumberd Read Models (EventHandlers)

This document explains how to write unit tests for MicroPlumberd read models that use the `[EventHandler]` attribute.

## The Generated Handle Method

When you decorate a class with `[EventHandler]`, the source generator creates:

```csharp
// Generated code
partial class MyModel : IEventHandler, ITypeRegister
{
    // Dispatches to the appropriate private Given method
    Task IEventHandler.Handle(Metadata m, object ev) => Given(m, ev);

    // Public dispatcher you can call directly in tests
    public async Task Given(Metadata m, object ev)
    {
        switch (ev)
        {
            case MyEvent1 e: await Given(m, e); break;
            case MyEvent2 e: await Given(m, e); break;
            default: throw new ArgumentException("Unknown event type", ev.GetType().Name);
        }
    }

    // Type registration for deserialization
    static IEnumerable<Type> ITypeRegister.Types => [typeof(MyEvent1), typeof(MyEvent2)];
}
```

## Testing Pattern

### 1. Create Test Metadata

```csharp
private static Metadata CreateMetadata(Guid id, DateTimeOffset? created = null)
{
    var eventId = Guid.NewGuid();
    var streamId = $"MyStream-{id}";

    var metadataJson = created.HasValue
        ? JsonDocument.Parse($"{{\"Created\":\"{created.Value:O}\"}}")
        : JsonDocument.Parse("{}");

    return new Metadata(
        id: id,
        eventId: eventId,
        sourceStreamPosition: 0,
        linkStreamPosition: null,
        sourceStreamId: streamId,
        data: metadataJson.RootElement
    );
}
```

### 2. Write Tests Using the Generated Given Method

```csharp
[Fact]
public async Task Given_SomeEvent_UpdatesState()
{
    // Arrange
    var model = new MyModel();
    var id = Guid.NewGuid();
    var metadata = CreateMetadata(id);
    var evt = new SomeEvent(id, "value");

    // Act - call the generated Given(Metadata, object) dispatcher
    await model.Given(metadata, evt);

    // Assert
    model.SomeProperty.Should().Be("value");
}
```

### 3. Test Full Event Lifecycle

```csharp
[Fact]
public async Task FullLifecycle_StartProgressComplete()
{
    // Arrange
    var model = new MyModel();
    var id = Guid.NewGuid();
    var metadata = CreateMetadata(id);

    // Act - Start
    await model.Given(metadata, new StartedEvent(id, "Test"));

    // Assert intermediate state
    model.Status.Should().Be(Status.Running);

    // Act - Progress
    await model.Given(metadata, new ProgressedEvent(id, 50, 100));

    model.Progress.Should().Be(0.5f);

    // Act - Complete
    await model.Given(metadata, new CompletedEvent(id, true));

    // Assert final state
    model.Status.Should().Be(Status.Completed);
}
```

## Benefits of This Approach

1. **No EventStore Required** - Tests run purely in-memory
2. **No Mocking of IPlumberInstance** - The model's state transitions are tested directly
3. **Fast Execution** - No I/O, no network calls
4. **Full Control** - You create the exact events you need

## What to Test

| Component | What to Test |
|-----------|--------------|
| State transitions | Each event type updates state correctly |
| Progress calculations | Ratios and percentages are computed correctly |
| Cleanup logic | Resources are released on completion/cancellation |
| Edge cases | Empty collections, null values, concurrent access |

## See Also

- `BatchOperationModelTests.cs` - Complete example of testing a read model
- `AppInstanceTests.cs` - Testing value objects with Parse/TryParse
- `AppContextTests.cs` - Testing context providers
