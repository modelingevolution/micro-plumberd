using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace MicroPlumberd.SpecFlow.SourceGenerators;

[DebuggerDisplay("{CommandType.Name}")]
class HandleDescriptor
{
    public ITypeSymbol CommandType => Method.Parameters[1].Type;
    public IMethodSymbol Method { get; set; }
}