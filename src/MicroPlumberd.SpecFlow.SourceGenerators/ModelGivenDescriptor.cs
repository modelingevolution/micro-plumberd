using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace MicroPlumberd.SpecFlow.SourceGenerators;

[DebuggerDisplay("{EventType.Name}")]
class ModelGivenDescriptor
{
    public IMethodSymbol Method { get; set; }
    public ITypeSymbol EventType => Method.Parameters[1].Type;
}