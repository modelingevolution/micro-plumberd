using EventStore.Client;

internal class Step
{
    public Type HandlerType { get; init; }
    public object[] Events { get; init; }
    public object? QueryResult { get; init; }
    public string Text { get; init; }
    public StepType Type { get; init; }
    public Exception? Exception { get; set; }
    public StreamPosition? PreCategoryStreamPosition { get; init; }
}