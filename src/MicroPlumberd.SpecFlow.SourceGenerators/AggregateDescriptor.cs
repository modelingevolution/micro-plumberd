using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace MicroPlumberd.SpecFlow.SourceGenerators;

[DebuggerDisplay("{Name}")]
class AggregateDescriptor
{
    public ITypeSymbol StateType { get; set; }
    public string Name { get; set; }
    public readonly List<AggregateGivenDescriptor> Givens = new List<AggregateGivenDescriptor>();
    public readonly List<IMethodSymbol> PublicMethods = new List<IMethodSymbol>();

    public AggregateDescriptor(INamedTypeSymbol type)
    {
        this.Type = type;
    }

    public INamedTypeSymbol Type { get; set; }
}