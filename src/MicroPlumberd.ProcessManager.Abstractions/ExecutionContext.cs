namespace MicroPlumberd;

public record ExecutionContext(Metadata Metadata, object Event, Guid Id, ICommandRequest? Command, Exception Exception);