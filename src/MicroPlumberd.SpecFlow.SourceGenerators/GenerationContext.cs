using System.Collections.Generic;

namespace MicroPlumberd.SpecFlow.SourceGenerators;

class GenerationContext
{
    public readonly List<AggregateDescriptor> Aggregates = new List<AggregateDescriptor>();
    public readonly List<CommandHandlerDescriptor> CommandHandlers = new List<CommandHandlerDescriptor>();
    public readonly List<EventHandlerDescriptor> EventHandlers = new List<EventHandlerDescriptor>();
}