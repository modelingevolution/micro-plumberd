using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace MicroPlumberd.SpecFlow.SourceGenerators;

[DebuggerDisplay("{EventType.Name}")]
class AggregateGivenDescriptor
{
    public ITypeSymbol EventType { get; set; }
}