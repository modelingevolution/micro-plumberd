# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Common Development Commands

### Build and Test
```bash
# Build the entire solution
dotnet build src/MicroPlumberd.sln

# Run all tests
dotnet test src/MicroPlumberd.sln

# Run specific test project
dotnet test src/MicroPlumberd.Tests/MicroPlumberd.Tests.csproj

# Run a single test
dotnet test src/MicroPlumberd.Tests/MicroPlumberd.Tests.csproj --filter "FullyQualifiedName~TestName"

# Build in Release mode
dotnet build src/MicroPlumberd.sln -c Release

# Pack NuGet packages
dotnet pack src/MicroPlumberd.sln -c Release
```

### Running Examples
```bash
# Run Cinema example
dotnet run --project src/MicroPlumberd.Examples.Cinema/MicroPlumberd.Examples.Cinema.csproj

# Run Identity Blazor example
dotnet run --project src/MicroPluberd.Examples.Blazor.Identity/MicroPluberd.Examples.Blazor.Identity.csproj
```

## High-Level Architecture

### Core Design Principles
MicroPlumberd is a CQRS/Event Sourcing framework built on EventStore that emphasizes convention over configuration and compile-time code generation for performance.

### Key Architectural Components

#### 1. Event Sourcing Layer
- **PlumberEngine** serves as the core abstraction over EventStore operations
- **IPlumber/IPlumberInstance** interfaces provide the main API surface
- **MetadataFactory** centralizes `Metadata` construction with proper JSON schema (`plumber.MetadataFactory` or standalone `new MetadataFactory()`)
- Events are stored in streams following naming conventions (e.g., `agg-{type}-{id}` for aggregates)
- Supports snapshots for aggregate performance optimization

#### 2. Aggregate Pattern
- **AggregateBase<TId, TState>** is the base class for all domain aggregates
- Aggregates encapsulate state and business logic, producing events via `AppendPendingChange()`
- State reconstruction happens through `Given()` methods that apply events
- The `[Aggregate]` attribute triggers source generation for dispatching and metadata

#### 3. Command Bus Architecture
- **CommandBus** provides async command dispatch with session-based routing
- Commands implement `ICommand` or `ICommand<TResult>`
- Command handlers implement `ICommandHandler<TCommand>` interface
- Supports both fire-and-forget and synchronous execution patterns
- Built-in error handling with fault exceptions propagation

#### 4. Event Handling System
- **IEventHandler** interface for processing events and updating read models
- **ITypeRegister** declares supported event types for proper deserialization
- Convention-based event routing with automatic type discovery
- Persistent subscriptions for guaranteed delivery

#### 5. Process Manager Pattern
- Long-running business process orchestration via `IProcessManager`
- State management with versioning for optimistic concurrency
- Event-driven state transitions with command generation
- Error handling and compensation logic support

#### 6. Source Generation
MicroPlumberd heavily uses source generators to reduce runtime reflection:
- **AggregateSourceGenerator** generates aggregate infrastructure
- **CommandHandlerSourceGenerator** creates handler registration code
- **ProcessManagerSourceGenerator** implements orchestration logic
- Generates `ITypeRegister` implementations automatically

### Stream Naming Conventions
- Aggregates: `agg-{type}-{id}`
- Snapshots: `agg-{type}-{id}-snapshot`
- Projections: `prj-{type}-{id}`
- Process Managers: `pm-{type}-{id}`
- Commands: `cmd-session-{sessionId}`

### Dependency Injection Integration
- Use `services.AddPlumberd()` to register all framework services
- Command handlers are resolved from DI container with proper scoping
- Supports both singleton and scoped service lifetimes

### Testing Approach
- Uses xUnit as the test framework
- **AggregateSpecs<T>** provides Given/When/Then testing pattern
- **EventStoreServer** provides in-memory EventStore for integration tests
- Test projects use FluentAssertions for readable assertions
- NSubstitute for mocking dependencies

### Important Patterns to Follow
1. **Immutable State**: Aggregate state should be immutable records
2. **Private Given Methods**: Event application methods should be private static
3. **Convention-Based Discovery**: Follow naming conventions for automatic discovery
4. **Source Generation**: Use attributes to trigger code generation
5. **Operational Context**: Always preserve causation/correlation IDs