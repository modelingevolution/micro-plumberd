using System.Collections.Generic;
using System.Diagnostics;

namespace MicroPlumberd.SpecFlow.SourceGenerators;

[DebuggerDisplay("{Name}")]
class CommandHandlerDescriptor
{
    public readonly List<HandleDescriptor> Handles = new List<HandleDescriptor>();
    public string Name { get; set; }
}