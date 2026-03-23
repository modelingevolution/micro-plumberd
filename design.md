# Design: OutputStream Conflict Validation

## Overview

Add startup-time validation to MicroPlumberd that detects when an event handler's `[OutputStream]` attribute uses the same name as any of its events' `[OutputStream]` attributes. This conflict silently corrupts EventStore merge streams today. The solution adds a shared validation method called from all event handler registration paths, throwing `InvalidOperationException` with a clear diagnostic message before the application can start.

## Problem Statement

In MicroPlumberd, `[OutputStream("X")]` serves two distinct purposes depending on where it is applied:

1. **On events** -- Controls the EventStore stream category name. When `PlumberEngine` writes an event, `Conventions.StreamNameFromEventConvention` reads the event type's `[OutputStream]` attribute to compute the stream name as `"{OutputStreamName}-{id}"` (see `Conventions.cs` line 488-496).

2. **On event handlers (read models)** -- Controls the merge stream name. When `PlumberEngine.SubscribeEventHandler` subscribes a handler, `Conventions.OutputStreamModelConvention` reads the handler type's `[OutputStream]` attribute to name the merge projection stream (see `Conventions.cs` line 498).

When these names collide, EventStore creates a projection that writes events into a stream with the same category prefix as the event's own stream. This produces a circular or conflicting projection that corrupts event delivery -- events may be duplicated, lost, or the projection may enter an error state. The corruption is silent and extremely difficult to debug.

### Example of the Bug

```csharp
[OutputStream("McpToolCall")]  // event stream category = "McpToolCall-{id}"
public record ToolCallRequested(...);

[OutputStream("McpToolCall")]  // merge stream also named "McpToolCall" -- CONFLICT!
[EventHandler]
public partial class McpActivityModel
{
    private async Task Given(Metadata m, ToolCallRequested ev) { ... }
}
```

The projection would try to merge `$et-ToolCallRequested` events into a stream named `McpToolCall`, but events are already being written to streams like `McpToolCall-{guid}`. The category stream `$ce-McpToolCall` and the merge projection stream `McpToolCall` collide.

## Architecture

### Components

- **`OutputStreamConflictValidator`** (new static class in `MicroPlumberd.Services`): Contains the validation logic as a single static method. All registration extension methods call this validator.
- **`ContainerExtensions`** (modified, in `MicroPlumberd.Services`): All `AddEventHandler`, `AddSingletonEventHandler`, `AddScopedEventHandler`, `AddStateEventHandler` overloads call the validator.
- **`ContainerExtensions`** (modified, in `MicroPlumberd.Services.EventAggregator`): `AddScopedEventAggregatorHandler` and `AddSingletonEventAggregatorHandler` call the validator.
- **Source generator diagnostic** (enhancement to `EventHandlerSourceGenerator`): Emits a compile-time error `MPLUMB004` when the conflict is detectable at compile time (handler and event types in the same compilation unit).

### Validation Flow

```
AddSingletonEventHandler<T>() ──┐
AddScopedEventHandler<T>()   ──┤
AddEventHandler<T>()          ──┼──> OutputStreamConflictValidator.Validate<T>()
AddStateEventHandler<T>()     ──┤         │
AddScopedEAHandler<T,TId>()  ──┤         ├── Get handler's [OutputStream] name
AddSingletonEAHandler<T,TId>()─┘         ├── Get each event type's [OutputStream] name via T.Types
                                          ├── Compare names
                                          └── Throw InvalidOperationException on match
```

### Why Runtime Validation (Not Only Compile-Time)

The source generator enhancement is a best-effort compile-time check but has limitations:

1. Event types may come from different assemblies (NuGet packages) where the source generator cannot inspect their attributes.
2. The `OutputStreamModelConvention` delegate is customizable at runtime -- the user could override the default convention. Only runtime validation can account for this.
3. The source generator processes each class in isolation and resolves event types by syntax name, not by semantic symbol. Resolving `[OutputStream]` attributes on event types requires semantic model access, which adds complexity and may not cover all cases (partial types, type aliases, etc.).

The runtime check in the DI registration path is the authoritative validation. The source generator diagnostic is a supplementary fast-feedback mechanism for the common case.

## Implementation Details

### New File: `OutputStreamConflictValidator.cs`

Location: `/mnt/d/source/modelingevolution/micro-plumberd/src/MicroPlumberd.Services/OutputStreamConflictValidator.cs`

```csharp
using System.Reflection;

namespace MicroPlumberd.Services;

/// <summary>
/// Validates that an event handler's [OutputStream] name does not collide
/// with any of its events' [OutputStream] names. Such a collision corrupts
/// the EventStore merge projection.
/// </summary>
static class OutputStreamConflictValidator
{
    /// <summary>
    /// Checks <typeparamref name="THandler"/> for OutputStream name conflicts
    /// with any event type declared in <c>THandler.Types</c>.
    /// Throws <see cref="InvalidOperationException"/> if a conflict is found.
    /// </summary>
    public static void Validate<THandler>() where THandler : ITypeRegister
    {
        var handlerType = typeof(THandler);
        var handlerAttr = handlerType.GetCustomAttribute<OutputStreamAttribute>();
        if (handlerAttr is null)
            return; // no handler-level OutputStream -- no conflict possible

        var handlerStreamName = handlerAttr.OutputStreamName;

        var conflictingEventTypes = THandler.Types
            .Select(eventType => (
                EventType: eventType,
                Attr: eventType.GetCustomAttribute<OutputStreamAttribute>()))
            .Where(x => x.Attr is not null
                && string.Equals(x.Attr.OutputStreamName, handlerStreamName, StringComparison.Ordinal))
            .Select(x => x.EventType)
            .ToList();

        if (conflictingEventTypes.Count == 0)
            return;

        var eventTypeNames = string.Join(", ", conflictingEventTypes.Select(t => t.FullName ?? t.Name));

        throw new InvalidOperationException(
            $"OutputStream name conflict detected. " +
            $"Event handler '{handlerType.FullName}' has [OutputStream(\"{handlerStreamName}\")] " +
            $"which matches the [OutputStream] attribute on event type(s): {eventTypeNames}. " +
            $"The handler's OutputStream must differ from its events' OutputStream to avoid " +
            $"corrupting the EventStore merge projection. " +
            $"Typical fix: rename the handler's [OutputStream] to include a version suffix, " +
            $"e.g. [OutputStream(\"{handlerStreamName}_v1\")].");
    }
}
```

### Modifications to `ContainerExtensions.cs` (MicroPlumberd.Services)

Each registration method calls `OutputStreamConflictValidator.Validate<TEventHandler>()` as its first statement, before any DI registrations occur.

**`AddSingletonEventHandler<TEventHandler>(... bool persistently, FromStream? start)`** (line 177):
```csharp
public static IServiceCollection AddSingletonEventHandler<TEventHandler>(this IServiceCollection services,
    bool persistently = false, FromStream? start = null) where TEventHandler : class, IEventHandler, ITypeRegister
{
    OutputStreamConflictValidator.Validate<TEventHandler>();  // <-- ADD THIS
    services.AddSingleton<TEventHandler>();
    // ... rest unchanged
}
```

**`AddScopedEventHandler<TEventHandler>(... bool persistently, FromStream? start)`** (line 161):
```csharp
public static IServiceCollection AddScopedEventHandler<TEventHandler>(this IServiceCollection services,
    bool persistently = false, FromStream? start = null) where TEventHandler : class, IEventHandler, ITypeRegister
{
    OutputStreamConflictValidator.Validate<TEventHandler>();  // <-- ADD THIS
    return services.AddScoped<TEventHandler>().AddEventHandler<TEventHandler>(persistently, start);
}
```

**`AddEventHandler<TEventHandler>(... bool persistently, FromStream? start)`** (line 199):
```csharp
public static IServiceCollection AddEventHandler<TEventHandler>(this IServiceCollection services,
    bool persistently = false, FromStream? start = null) where TEventHandler : class, IEventHandler, ITypeRegister
{
    OutputStreamConflictValidator.Validate<TEventHandler>();  // <-- ADD THIS
    services.AddSingleton<EventHandlerStarter<TEventHandler>>();
    // ... rest unchanged
}
```

**`AddStateEventHandler<TEventHandler>()`** (line 216):
```csharp
public static IServiceCollection AddStateEventHandler<TEventHandler>(this IServiceCollection services)
    where TEventHandler : class, IEventHandler, ITypeRegister
{
    OutputStreamConflictValidator.Validate<TEventHandler>();  // <-- ADD THIS
    // ... rest unchanged
}
```

**`AddEventHandler<TEventHandler>(... FromRelativeStreamPosition start)`** (line 232):
```csharp
public static IServiceCollection AddEventHandler<TEventHandler>(this IServiceCollection services,
    FromRelativeStreamPosition start) where TEventHandler : class, IEventHandler, ITypeRegister
{
    OutputStreamConflictValidator.Validate<TEventHandler>();  // <-- ADD THIS
    // ... rest unchanged
}
```

**`AddScopedEventHandler<TEventHandler>(... FromRelativeStreamPosition start)`** (line 248):
```csharp
public static IServiceCollection AddScopedEventHandler<TEventHandler>(this IServiceCollection services,
    FromRelativeStreamPosition start) where TEventHandler : class, IEventHandler, ITypeRegister
{
    OutputStreamConflictValidator.Validate<TEventHandler>();  // <-- ADD THIS
    return services.AddScoped<TEventHandler>().AddEventHandler<TEventHandler>(start);
}
```

**`AddSingletonEventHandler<TEventHandler>(... FromRelativeStreamPosition start)`** (line 261):
```csharp
public static IServiceCollection AddSingletonEventHandler<TEventHandler>(this IServiceCollection services,
    FromRelativeStreamPosition start) where TEventHandler : class, IEventHandler, ITypeRegister
{
    OutputStreamConflictValidator.Validate<TEventHandler>();  // <-- ADD THIS
    return services.AddSingleton<TEventHandler>().AddEventHandler<TEventHandler>(start);
}
```

**Note on call placement**: Some methods like `AddScopedEventHandler` delegate to `AddEventHandler`. In those chains, the validation will be called once at the entry point. The `AddEventHandler` overloads that are called directly also validate. This means in delegation chains (e.g., `AddScopedEventHandler` -> `AddEventHandler`) the validation runs twice -- this is acceptable because the method is pure reflection and very cheap. Alternatively, the validation call could be placed only in `AddEventHandler` (the leaf methods), but having it at entry points gives clearer stack traces when the exception is thrown.

**Design decision**: Place the validation call in the **leaf** `AddEventHandler` overloads only (the ones at lines 199 and 232 that actually register `EventHandlerStarter`). The methods `AddScopedEventHandler` and `AddSingletonEventHandler` that delegate to `AddEventHandler` do not need their own check. The standalone `AddSingletonEventHandler` at line 177 that does its own registration (not delegating) also needs the check. This avoids double validation.

### Modifications to `ContainerExtensions.cs` (MicroPlumberd.Services.EventAggregator)

The EventAggregator extension methods also register event handlers and must validate:

**`AddScopedEventAggregatorHandler<THandler, TId>()`** (line 31):
```csharp
public static IServiceCollection AddScopedEventAggregatorHandler<THandler, TId>(
    this IServiceCollection services)
    where THandler : class, IEventHandler, ITypeRegister
    where TId : IParsable<TId>
{
    OutputStreamConflictValidator.Validate<THandler>();  // <-- ADD THIS
    services.TryAddScoped<THandler>();
    // ... rest unchanged
}
```

**`AddSingletonEventAggregatorHandler<THandler, TId>()`** (line 60):
```csharp
public static IServiceCollection AddSingletonEventAggregatorHandler<THandler, TId>(
    this IServiceCollection services)
    where THandler : class, IEventHandler, ITypeRegister
    where TId : IParsable<TId>
{
    OutputStreamConflictValidator.Validate<THandler>();  // <-- ADD THIS
    services.AddSingleton<THandler>();
    // ... rest unchanged
}
```

Since `MicroPlumberd.Services.EventAggregator` project already has `InternalsVisibleTo` for tests and references `MicroPlumberd.Services`, `OutputStreamConflictValidator` (which is `internal` / file-scoped to `MicroPlumberd.Services`) is accessible because the EventAggregator project already has `[assembly: InternalsVisibleTo("MicroPlumberd.Services.EventAggregator")]` declared in `EventHandlerService.cs` line 9.

### Source Generator Enhancement (Supplementary)

Add a new diagnostic `MPLUMB004` to `EventHandlerSourceGenerator` that checks for OutputStream conflicts at compile time when possible.

In `GetEventHandlerResult`, after the existing `Given` method discovery:

```csharp
// Check for OutputStream conflict (best-effort compile-time check)
var handlerOutputStreamAttr = classSymbol.GetAttributes()
    .FirstOrDefault(a => a.AttributeClass?.Name == "OutputStreamAttribute");

if (handlerOutputStreamAttr is not null
    && handlerOutputStreamAttr.ConstructorArguments.Length > 0)
{
    var handlerStreamName = handlerOutputStreamAttr.ConstructorArguments[0].Value as string;
    if (handlerStreamName is not null)
    {
        foreach (var givenMethod in givenMethods)
        {
            var eventParamType = context.SemanticModel
                .GetTypeInfo(givenMethod.ParameterList.Parameters[1].Type!).Type;
            if (eventParamType is null) continue;

            var eventOutputStreamAttr = eventParamType.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "OutputStreamAttribute");

            if (eventOutputStreamAttr is not null
                && eventOutputStreamAttr.ConstructorArguments.Length > 0
                && eventOutputStreamAttr.ConstructorArguments[0].Value is string eventStreamName
                && eventStreamName == handlerStreamName)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    OutputStreamConflictRule,
                    classDecl.Identifier.GetLocation(),
                    classSymbol.Name,
                    handlerStreamName,
                    eventParamType.Name));
            }
        }
    }
}
```

New diagnostic descriptor:

```csharp
private static readonly DiagnosticDescriptor OutputStreamConflictRule = new DiagnosticDescriptor(
    id: "MPLUMB004",
    title: "OutputStream name conflict between handler and event",
    messageFormat: "EventHandler '{0}' has [OutputStream(\"{1}\")] which conflicts with event type '{2}' that uses the same OutputStream name. This will corrupt the EventStore merge projection. Use a different name for the handler's OutputStream (e.g., append '_v1').",
    category: "MicroPlumberd.SourceGenerator",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

## Error Message Format

The runtime exception message follows this template:

```
OutputStream name conflict detected. Event handler '{HandlerFullName}' has [OutputStream("{StreamName}")] which matches the [OutputStream] attribute on event type(s): {EventType1FullName}, {EventType2FullName}. The handler's OutputStream must differ from its events' OutputStream to avoid corrupting the EventStore merge projection. Typical fix: rename the handler's [OutputStream] to include a version suffix, e.g. [OutputStream("{StreamName}_v1")].
```

Concrete example:

```
OutputStream name conflict detected. Event handler 'MyApp.ReadModels.McpActivityModel' has [OutputStream("McpToolCall")] which matches the [OutputStream] attribute on event type(s): MyApp.Events.ToolCallRequested. The handler's OutputStream must differ from its events' OutputStream to avoid corrupting the EventStore merge projection. Typical fix: rename the handler's [OutputStream] to include a version suffix, e.g. [OutputStream("McpToolCall_v1")].
```

## Dependencies

| Dependency | Purpose | Failure Handling |
|------------|---------|------------------|
| `System.Reflection` | Reading `[OutputStream]` attributes from handler and event types | Already available; no failure scenario |
| `ITypeRegister.Types` | Discovering event types the handler processes | Static abstract member; guaranteed by generic constraint |
| `OutputStreamAttribute` | Defined in `MicroPlumberd` core | Already a dependency of `MicroPlumberd.Services` |

## Test Scenarios

All tests should be placed in `/mnt/d/source/modelingevolution/micro-plumberd/src/MicroPlumberd.Tests/OutputStreamConflictValidatorTests.cs`.

### Test 1: Conflict detected -- handler and event share same OutputStream name

```csharp
[OutputStream("Shared")]
public record ConflictEvent(string Data);

[OutputStream("Shared")]
public class ConflictHandler : IEventHandler, ITypeRegister
{
    public Task Handle(Metadata m, object ev) => Task.CompletedTask;
    public static IEnumerable<Type> Types => [typeof(ConflictEvent)];
}

[Fact]
public void Validate_ThrowsOnConflict()
{
    var act = () => OutputStreamConflictValidator.Validate<ConflictHandler>();
    act.Should().Throw<InvalidOperationException>()
       .WithMessage("*ConflictHandler*")
       .WithMessage("*Shared*")
       .WithMessage("*ConflictEvent*");
}
```

### Test 2: No conflict -- handler and event have different OutputStream names

```csharp
[OutputStream("EventStream")]
public record SafeEvent(string Data);

[OutputStream("SafeModel_v1")]
public class SafeHandler : IEventHandler, ITypeRegister
{
    public Task Handle(Metadata m, object ev) => Task.CompletedTask;
    public static IEnumerable<Type> Types => [typeof(SafeEvent)];
}

[Fact]
public void Validate_NoThrowWhenDifferentNames()
{
    var act = () => OutputStreamConflictValidator.Validate<SafeHandler>();
    act.Should().NotThrow();
}
```

### Test 3: No conflict -- handler has OutputStream but events do not

```csharp
public record PlainEvent(string Data);  // no [OutputStream]

[OutputStream("MyModel_v1")]
public class HandlerWithPlainEvents : IEventHandler, ITypeRegister
{
    public Task Handle(Metadata m, object ev) => Task.CompletedTask;
    public static IEnumerable<Type> Types => [typeof(PlainEvent)];
}

[Fact]
public void Validate_NoThrowWhenEventsLackAttribute()
{
    var act = () => OutputStreamConflictValidator.Validate<HandlerWithPlainEvents>();
    act.Should().NotThrow();
}
```

### Test 4: No conflict -- handler has no OutputStream attribute

```csharp
[OutputStream("SomeStream")]
public record DecoratedEvent(string Data);

public class HandlerWithoutOutputStream : IEventHandler, ITypeRegister
{
    public Task Handle(Metadata m, object ev) => Task.CompletedTask;
    public static IEnumerable<Type> Types => [typeof(DecoratedEvent)];
}

[Fact]
public void Validate_NoThrowWhenHandlerLacksAttribute()
{
    var act = () => OutputStreamConflictValidator.Validate<HandlerWithoutOutputStream>();
    act.Should().NotThrow();
}
```

### Test 5: Conflict with one of multiple events

```csharp
[OutputStream("Orders")]
public record OrderCreated(Guid Id);

public record OrderUpdated(Guid Id);  // no [OutputStream]

[OutputStream("Orders")]
public class OrderHandler : IEventHandler, ITypeRegister
{
    public Task Handle(Metadata m, object ev) => Task.CompletedTask;
    public static IEnumerable<Type> Types => [typeof(OrderCreated), typeof(OrderUpdated)];
}

[Fact]
public void Validate_ThrowsWhenOneOfManyEventsConflicts()
{
    var act = () => OutputStreamConflictValidator.Validate<OrderHandler>();
    act.Should().Throw<InvalidOperationException>()
       .WithMessage("*OrderCreated*")
       .And.Message.Should().NotContain("OrderUpdated");
}
```

### Test 6: DI registration throws (integration test)

```csharp
[Fact]
public void AddSingletonEventHandler_ThrowsOnOutputStreamConflict()
{
    var services = new ServiceCollection();
    var act = () => services.AddSingletonEventHandler<ConflictHandler>();
    act.Should().Throw<InvalidOperationException>()
       .WithMessage("*OutputStream name conflict*");
}

[Fact]
public void AddScopedEventHandler_ThrowsOnOutputStreamConflict()
{
    var services = new ServiceCollection();
    var act = () => services.AddScopedEventHandler<ConflictHandler>();
    act.Should().Throw<InvalidOperationException>()
       .WithMessage("*OutputStream name conflict*");
}
```

### Test 7: Case sensitivity -- different case is not a conflict

```csharp
[OutputStream("orders")]
public record LowercaseEvent(string Data);

[OutputStream("Orders")]
public class CaseSensitiveHandler : IEventHandler, ITypeRegister
{
    public Task Handle(Metadata m, object ev) => Task.CompletedTask;
    public static IEnumerable<Type> Types => [typeof(LowercaseEvent)];
}

[Fact]
public void Validate_CaseSensitive_NoConflict()
{
    var act = () => OutputStreamConflictValidator.Validate<CaseSensitiveHandler>();
    act.Should().NotThrow();
}
```

### Test 8: Source generator compile-time diagnostic (unit test)

Add to `GeneratorOutputTests.cs`:

```csharp
[Fact]
public void EventHandler_ReportsOutputStreamConflict()
{
    var source = @"
using System;
using System.Threading.Tasks;
using MicroPlumberd;

namespace TestNamespace
{
    [OutputStream(""Shared"")]
    public record ConflictEvent(string Name);

    [OutputStream(""Shared"")]
    [EventHandler]
    public partial class ConflictHandler
    {
        private async Task Given(Metadata m, ConflictEvent ev)
        {
            await Task.CompletedTask;
        }
    }
}";

    // Run generator and check diagnostics contain MPLUMB004
    // (implementation depends on test infrastructure for resolving
    //  MicroPlumberd attribute types in the test compilation)
}
```

## Implementation Notes

1. **Comparison is ordinal (case-sensitive)**. EventStore stream names are case-sensitive. `"Orders"` and `"orders"` are different streams. The validation uses `StringComparison.Ordinal` to match EventStore behavior.

2. **Validation runs at DI registration time**, not at runtime event processing time. This means the application fails to start immediately if misconfigured, rather than silently corrupting data for hours before someone notices.

3. **The validator is a pure static method** with no state or dependencies beyond reflection. It does not need DI registration itself.

4. **Performance**: The validation uses `GetCustomAttribute<OutputStreamAttribute>()` which is a reflection call. This runs once per handler type during DI setup -- not on a hot path. The cost is negligible.

5. **The source generator diagnostic is supplementary**. The runtime check is the authoritative guard. The source generator check provides earlier feedback (at compile time) but cannot cover all cases (cross-assembly event types, custom conventions).

6. **Existing codebase is safe**. Reviewed all `[OutputStream]` usages in the repository. Handlers consistently use versioned suffixes (e.g., `"ReservationModel_v1"`, `"FooModel_v1"`) while events use unversioned category names (e.g., `"ReservationStream"`, `"UserProfile"`). No existing code would trigger this validation.

7. **The `MicroPlumberd.Services.EventAggregator` project** already has `InternalsVisibleTo` access to `MicroPlumberd.Services` (declared in `EventHandlerService.cs`), so it can call the `internal` `OutputStreamConflictValidator` directly.

## Validation Status
- [x] Completeness (event-modeling): PASS -- Every registration path (6 overloads in Services + 2 in EventAggregator) is covered. The validation traces: intention (register handler) -> check (reflect on types) -> outcome (throw or pass). Every field in the error message has a defined source.
- [x] Testability: PASS -- 8 test scenarios covering: conflict detection, no-conflict cases, partial conflicts, DI integration, case sensitivity, and source generator diagnostics. All components are testable in isolation.
- [x] Dependencies: PASS -- Only depends on `System.Reflection` and `OutputStreamAttribute` (already in scope). No new packages needed.
- [x] Conflicts: PASS -- No conflicting design decisions. The runtime vs. compile-time approaches are complementary, not contradictory. The source generator enhancement is additive and does not change existing behavior.
