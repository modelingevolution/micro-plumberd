using FluentAssertions;
using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroPlumberd.Tests.Unit;

public record TestInProcCommand(Guid Id, string Value);
public record TestRemoteCommand(Guid Id, string Value);

/// <summary>
/// A simple command handler that records calls for verification.
/// The [CommandHandler] source generator would normally generate IServiceTypeRegister,
/// but for tests we implement it manually.
/// </summary>
public class TestInProcCommandHandler :
    ICommandHandler<Guid, TestInProcCommand>,
    IServiceTypeRegister
{
    public List<(Guid Id, TestInProcCommand Command)> ReceivedCommands { get; } = new();

    public Task<object?> Execute(Guid id, TestInProcCommand command)
    {
        ReceivedCommands.Add((id, command));
        return Task.FromResult<object?>(null);
    }

    // IServiceTypeRegister — source generator would produce these
    public static IEnumerable<Type> ReturnTypes => [];
    public static IEnumerable<Type> FaultTypes => [];
    public static IEnumerable<Type> CommandTypes => [typeof(TestInProcCommand)];
    public static IServiceCollection RegisterHandlers(IServiceCollection services, bool scoped = true)
    {
        if (scoped)
            services.AddScoped<ICommandHandler<TestInProcCommand>, TestInProcCommandHandler>();
        else
            services.AddSingleton<ICommandHandler<TestInProcCommand>, TestInProcCommandHandler>();
        return services;
    }
}

/// <summary>
/// A fake ICommandBus that records calls to verify delegation.
/// </summary>
public class FakeCommandBus : ICommandBus
{
    public List<(object RecipientId, object Command)> SentCommands { get; } = new();
    public List<(object RecipientId, object Command)> QueuedCommands { get; } = new();

    public Task SendAsync(object recipientId, object command, TimeSpan? timeout = null,
        bool fireAndForget = false, CancellationToken token = default)
    {
        SentCommands.Add((recipientId, command));
        return Task.CompletedTask;
    }

    public Task QueueAsync(object recipientId, object command, TimeSpan? timeout = null,
        bool fireAndForget = true, CancellationToken token = default)
    {
        QueuedCommands.Add((recipientId, command));
        return Task.CompletedTask;
    }

    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class InProcCommandBusDecoratorTests
{
    [Fact]
    public async Task SendAsync_registered_command_executes_in_process()
    {
        // Arrange
        var handler = new TestInProcCommandHandler();
        var fakeBus = new FakeCommandBus();
        var registry = new InProcCommandRegistry();
        registry.Register(typeof(TestInProcCommand), isSingleton: true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICommandHandler<TestInProcCommand>>(handler);
        await using var sp = services.BuildServiceProvider();

        var decorator = new InProcCommandBusDecorator(
            fakeBus, registry, sp,
            sp.GetRequiredService<ILogger<InProcCommandBusDecorator>>());

        var recipientId = Guid.NewGuid();
        var command = new TestInProcCommand(Guid.NewGuid(), "hello");

        // Act
        await decorator.SendAsync(recipientId, command);

        // Assert — handler was called directly
        handler.ReceivedCommands.Should().HaveCount(1);
        handler.ReceivedCommands[0].Id.Should().Be(recipientId);
        handler.ReceivedCommands[0].Command.Should().Be(command);

        // Assert — inner bus was NOT called
        fakeBus.SentCommands.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_unregistered_command_delegates_to_inner_bus()
    {
        // Arrange
        var fakeBus = new FakeCommandBus();
        var registry = new InProcCommandRegistry();
        // TestRemoteCommand is NOT registered

        var services = new ServiceCollection();
        services.AddLogging();
        await using var sp = services.BuildServiceProvider();

        var decorator = new InProcCommandBusDecorator(
            fakeBus, registry, sp,
            sp.GetRequiredService<ILogger<InProcCommandBusDecorator>>());

        var recipientId = Guid.NewGuid();
        var command = new TestRemoteCommand(Guid.NewGuid(), "remote");

        // Act
        await decorator.SendAsync(recipientId, command);

        // Assert — inner bus was called
        fakeBus.SentCommands.Should().HaveCount(1);
        fakeBus.SentCommands[0].RecipientId.Should().Be(recipientId);
        fakeBus.SentCommands[0].Command.Should().Be(command);
    }

    [Fact]
    public async Task QueueAsync_registered_command_executes_in_process()
    {
        // Arrange
        var handler = new TestInProcCommandHandler();
        var fakeBus = new FakeCommandBus();
        var registry = new InProcCommandRegistry();
        registry.Register(typeof(TestInProcCommand), isSingleton: true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICommandHandler<TestInProcCommand>>(handler);
        await using var sp = services.BuildServiceProvider();

        var decorator = new InProcCommandBusDecorator(
            fakeBus, registry, sp,
            sp.GetRequiredService<ILogger<InProcCommandBusDecorator>>());

        var recipientId = Guid.NewGuid();
        var command = new TestInProcCommand(Guid.NewGuid(), "queued");

        // Act
        await decorator.QueueAsync(recipientId, command);

        // Assert — handler was called directly
        handler.ReceivedCommands.Should().HaveCount(1);

        // Assert — inner bus was NOT called
        fakeBus.QueuedCommands.Should().BeEmpty();
    }

    [Fact]
    public void AddCommandInProcExecutor_registers_decorator()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        // Register a fake ICommandBus as the "inner" bus
        services.AddSingleton<ICommandBus, FakeCommandBus>();
        services.AddSingleton<ICommandHandler<TestInProcCommand>, TestInProcCommandHandler>();

        // Act
        services.AddCommandInProcExecutor<TestInProcCommand>();

        // Assert — registry was created with the command type
        var registryDescriptor = services.FirstOrDefault(d => d.ImplementationInstance is InProcCommandRegistry);
        registryDescriptor.Should().NotBeNull();
        var registry = (InProcCommandRegistry)registryDescriptor!.ImplementationInstance!;
        registry.IsRegistered(typeof(TestInProcCommand)).Should().BeTrue();
        registry.IsRegistered(typeof(TestRemoteCommand)).Should().BeFalse();
    }

    [Fact]
    public void AddCommandInProcExecutorFor_registers_all_handler_commands()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICommandBus, FakeCommandBus>();

        // Act
        services.AddCommandInProcExecutorFor<TestInProcCommandHandler>();

        // Assert — all command types from the handler are registered
        var registryDescriptor = services.FirstOrDefault(d => d.ImplementationInstance is InProcCommandRegistry);
        registryDescriptor.Should().NotBeNull();
        var registry = (InProcCommandRegistry)registryDescriptor!.ImplementationInstance!;
        registry.IsRegistered(typeof(TestInProcCommand)).Should().BeTrue();
    }

    [Fact]
    public async Task Full_DI_integration_decorator_intercepts_registered_commands()
    {
        // Arrange — full DI wiring (without AddPlumberd, so we wire the decorator chain manually)
        var services = new ServiceCollection();
        services.AddLogging();

        // Inner bus
        var fakeBus = new FakeCommandBus();
        services.AddSingleton<ICommandBus>(fakeBus);

        // Handler
        var handler = new TestInProcCommandHandler();
        services.AddSingleton<ICommandHandler<TestInProcCommand>>(handler);

        // Register for in-proc execution (populates the registry)
        services.AddCommandInProcExecutor<TestInProcCommand>();

        // In production, AddPlumberd() registers the decorator chain.
        // For tests without AddPlumberd, add the decorator manually.
        services.TryDecorate<ICommandBus, InProcCommandBusDecorator>();

        await using var sp = services.BuildServiceProvider();
        var bus = sp.GetRequiredService<ICommandBus>();

        // Bus should be the decorator, not the fake
        bus.Should().BeOfType<InProcCommandBusDecorator>();

        // Act — send registered command
        var recipientId = Guid.NewGuid();
        var inProcCmd = new TestInProcCommand(Guid.NewGuid(), "in-proc");
        await bus.SendAsync(recipientId, inProcCmd);

        // Assert — handled in-process
        handler.ReceivedCommands.Should().HaveCount(1);
        fakeBus.SentCommands.Should().BeEmpty();

        // Act — send unregistered command
        var remoteCmd = new TestRemoteCommand(Guid.NewGuid(), "remote");
        await bus.SendAsync(recipientId, remoteCmd);

        // Assert — delegated to inner bus
        fakeBus.SentCommands.Should().HaveCount(1);
        handler.ReceivedCommands.Should().HaveCount(1); // no change
    }
}
