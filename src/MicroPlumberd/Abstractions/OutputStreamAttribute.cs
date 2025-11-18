namespace MicroPlumberd;

/// <summary>
/// Use this attribute on Models where you want to override stream name convention. 
/// </summary>
/// <remarks>
/// Your read-models/event-handlers typically subscribe to a stream that comes from a merge of event-type streams.
/// If you have a FooModel:
/// <code>
/// [EventHandler]
/// [OutputStream("Foo")]
/// class FooModel {
/// public async Task Given(Metadata m, FooEvent1 ev) { }
/// public async Task Given(Metadata m, FooEvent2 ev) { }
/// }
/// </code>
/// Without the OutputStream attribute a projection would be made in EventStoreDB that produces FooModel stream out of
/// $et-FooEvent1 and $et-FooEvent2. But, since there is the attribute the produced stream will be named 'Foo'.
/// </remarks>
/// <seealso cref="System.Attribute" />
[AttributeUsage(AttributeTargets.Class)]
public class OutputStreamAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutputStreamAttribute"/> class.
    /// </summary>
    /// <param name="outputStreamName">The name of the output stream.</param>
    /// <exception cref="ArgumentException">Thrown when outputStreamName is null or whitespace.</exception>
    public OutputStreamAttribute(string outputStreamName)
    {
        if (string.IsNullOrWhiteSpace(outputStreamName))
            throw new ArgumentException(nameof(outputStreamName));
        this.OutputStreamName = outputStreamName;
    }
    /// <summary>
    /// Gets or sets the name of the output stream, that is a joined stream from all event types used in model.
    /// </summary>
    /// <value>
    /// The name of the output stream.
    /// </value>
    public string OutputStreamName { get; }
}