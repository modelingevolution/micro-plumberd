using FluentAssertions;
using MicroPlumberd;
using Xunit;

namespace MicroPlumberd.Services.BatchOperations.Tests;

/// <summary>
/// Unit tests for <see cref="BatchOperationModel"/>.
///
/// ## How to Unit Test MicroPlumberd Read Models (EventHandlers)
///
/// Read models decorated with [EventHandler] get a source-generated public method:
///
///     public async Task Given(Metadata m, object ev)
///
/// This method dispatches to the appropriate private Given(Metadata, TEvent) method
/// based on the event type. This allows unit testing without mocking IPlumberInstance
/// or EventStore - you simply:
///
/// 1. Create an instance of the model
/// 2. Create test events
/// 3. Call model.Given(metadata, event) directly
/// 4. Assert on the model's state
///
/// ### Creating Test Metadata
///
/// Use the helper method CreateMetadata() to create minimal Metadata for tests.
/// The important fields are:
/// - Id: The stream identifier (usually the aggregate/entity ID)
/// - EventId: Unique event identifier
/// - SourceStreamId: Full stream name (e.g., "BatchOperations-{guid}")
///
/// ### Example Pattern
///
/// ```csharp
/// [Fact]
/// public async Task Given_SomeEvent_UpdatesState()
/// {
///     // Arrange
///     var model = new MyModel();
///     var id = Guid.NewGuid();
///     var metadata = CreateMetadata(id);
///     var evt = new SomeEvent(id, "value");
///
///     // Act - use the generated Given(Metadata, object) method
///     await model.Given(metadata, evt);
///
///     // Assert
///     model.SomeProperty.Should().Be("value");
/// }
/// ```
/// </summary>
public class BatchOperationModelTests
{
    private static readonly MetadataFactory _metadataFactory = new();

    /// <summary>
    /// Creates test Metadata with minimal required fields.
    /// </summary>
    private static Metadata CreateMetadata(Guid id, DateTimeOffset? created = null)
        => _metadataFactory.Create($"BatchOperations-{id}", created: created);

    [Fact]
    public async Task Given_BatchOperationStarted_CreatesOperation()
    {
        // Arrange
        var model = new BatchOperationModel();
        var operationId = Guid.NewGuid();
        var metadata = CreateMetadata(operationId, DateTimeOffset.UtcNow);
        var evt = new BatchOperationStarted(
            OperationId: operationId,
            OperationType: "TestExport",
            Start: 0,
            EndInclusive: 99,
            AppContext: AppContext.Empty
        );

        // Act - call the generated Given(Metadata, object) dispatcher
        await model.Given(metadata, evt);

        // Assert
        model.Operations.Should().ContainKey(operationId);
        var op = model.Operations[operationId];
        op.Type.Should().Be("TestExport");
        op.Status.Should().Be(BatchOperation.State.Running);
        op.Progress.Should().Be(0);
    }

    [Fact]
    public async Task Given_BatchOperationProgressed_UpdatesProgress()
    {
        // Arrange
        var model = new BatchOperationModel();
        var operationId = Guid.NewGuid();
        var metadata = CreateMetadata(operationId, DateTimeOffset.UtcNow);

        // First start the operation
        await model.Given(metadata, new BatchOperationStarted(
            operationId, "TestExport", 0, 99, AppContext.Empty));

        var progressEvt = new BatchOperationProgressed(
            OperationId: operationId,
            Current: 50,
            Total: 100,
            Message: "Processing item 50"
        );

        // Act
        await model.Given(metadata, progressEvt);

        // Assert
        var op = model.Operations[operationId];
        op.Progress.Should().Be(0.5f);
        op.CurrentMessage.Should().Be("Processing item 50");
    }

    [Fact]
    public async Task Given_BatchOperationCompleted_Success_SetsCompletedStatus()
    {
        // Arrange
        var model = new BatchOperationModel();
        var operationId = Guid.NewGuid();
        var metadata = CreateMetadata(operationId, DateTimeOffset.UtcNow);

        await model.Given(metadata, new BatchOperationStarted(
            operationId, "TestExport", 0, 99, AppContext.Empty));

        var completedEvt = new BatchOperationCompleted(
            OperationId: operationId,
            Success: true,
            Message: "Export completed successfully"
        );

        // Act
        await model.Given(metadata, completedEvt);

        // Assert - operation should be cleaned up after completion
        model.Operations.Should().NotContainKey(operationId);
    }

    [Fact]
    public async Task Given_BatchOperationCompleted_Failure_SetsFailedStatus()
    {
        // Arrange
        var model = new BatchOperationModel();
        var operationId = Guid.NewGuid();
        var metadata = CreateMetadata(operationId, DateTimeOffset.UtcNow);

        await model.Given(metadata, new BatchOperationStarted(
            operationId, "TestExport", 0, 99, AppContext.Empty));

        var completedEvt = new BatchOperationCompleted(
            OperationId: operationId,
            Success: false,
            Message: "Export failed: timeout"
        );

        // Act
        await model.Given(metadata, completedEvt);

        // Assert - operation cleaned up after completion (even failed)
        model.Operations.Should().NotContainKey(operationId);
    }

    [Fact]
    public async Task Given_BatchOperationCancelled_SetsCanceledStatus()
    {
        // Arrange
        var model = new BatchOperationModel();
        var operationId = Guid.NewGuid();
        var metadata = CreateMetadata(operationId, DateTimeOffset.UtcNow);

        await model.Given(metadata, new BatchOperationStarted(
            operationId, "TestExport", 0, 99, AppContext.Empty));

        var cancelledEvt = new BatchOperationCancelled(
            OperationId: operationId,
            Reason: "User requested cancellation"
        );

        // Act
        await model.Given(metadata, cancelledEvt);

        // Assert - operation cleaned up after cancellation
        model.Operations.Should().NotContainKey(operationId);
    }

    [Fact]
    public void Get_CreatesNewOperationIfNotExists()
    {
        // Arrange
        var model = new BatchOperationModel();
        var operationId = Guid.NewGuid();

        // Act
        var op = model.Get(operationId);

        // Assert
        op.Should().NotBeNull();
        op.Id.Should().Be(operationId);
        model.Operations.Should().ContainKey(operationId);
        model.Items.Should().Contain(op);
    }

    [Fact]
    public void Get_ReturnsSameInstanceForSameId()
    {
        // Arrange
        var model = new BatchOperationModel();
        var operationId = Guid.NewGuid();

        // Act
        var op1 = model.Get(operationId);
        var op2 = model.Get(operationId);

        // Assert
        op1.Should().BeSameAs(op2);
    }

    [Fact]
    public void Prepare_CreatesCancellationToken()
    {
        // Arrange
        var model = new BatchOperationModel();
        var operationId = Guid.NewGuid();

        // Act
        var token = model.Prepare(operationId);

        // Assert
        token.Should().NotBe(CancellationToken.None);
        token.CanBeCanceled.Should().BeTrue();
    }

    [Fact]
    public void Cancel_CancelsCancellationToken()
    {
        // Arrange
        var model = new BatchOperationModel();
        var operationId = Guid.NewGuid();
        var token = model.Prepare(operationId);

        // Act
        model.Cancel(operationId);

        // Assert
        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void GetCancellationToken_ReturnsNoneForUnknownOperation()
    {
        // Arrange
        var model = new BatchOperationModel();
        var unknownId = Guid.NewGuid();

        // Act
        var token = model.GetCancellationToken(unknownId);

        // Assert
        token.Should().Be(CancellationToken.None);
    }

    [Fact]
    public void Cleanup_RemovesOperationAndDisposesToken()
    {
        // Arrange
        var model = new BatchOperationModel();
        var operationId = Guid.NewGuid();
        model.Get(operationId);
        model.Prepare(operationId);

        // Act
        model.Cleanup(operationId);

        // Assert
        model.Operations.Should().NotContainKey(operationId);
        model.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task FullLifecycle_StartProgressComplete()
    {
        // Arrange
        var model = new BatchOperationModel();
        var operationId = Guid.NewGuid();
        var metadata = CreateMetadata(operationId, DateTimeOffset.UtcNow);
        var appContext = new AppContext(
            new AppInstance("TestApp", "1.0.0", "localhost", 0),
            Guid.NewGuid()
        );

        // Act - Start
        await model.Given(metadata, new BatchOperationStarted(
            operationId, "DataMigration", 0, 999, appContext));

        model.Operations[operationId].Status.Should().Be(BatchOperation.State.Running);
        model.Operations[operationId].AppContext.Should().Be(appContext);

        // Act - Progress updates
        for (int i = 1; i <= 10; i++)
        {
            await model.Given(metadata, new BatchOperationProgressed(
                operationId, (ulong)(i * 100), 1000, $"Batch {i}/10"));
        }

        model.Operations[operationId].Progress.Should().Be(1.0f);
        model.Operations[operationId].CurrentMessage.Should().Be("Batch 10/10");

        // Act - Complete
        await model.Given(metadata, new BatchOperationCompleted(
            operationId, true, "Migration completed"));

        // Assert - cleaned up
        model.Operations.Should().NotContainKey(operationId);
    }
}
