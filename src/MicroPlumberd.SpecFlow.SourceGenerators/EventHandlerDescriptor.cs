using System.Collections.Generic;
using System.Diagnostics;

namespace MicroPlumberd.SpecFlow.SourceGenerators;

[DebuggerDisplay("{Name}")]
class EventHandlerDescriptor
{
    public string Name { get; set; }
    public readonly List<ModelGivenDescriptor> Givens = new List<ModelGivenDescriptor>();
}