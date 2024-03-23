namespace MicroPlumberd;

public interface IEventRecord
{
    Metadata Metadata { get; }
    object Event { get; }
}