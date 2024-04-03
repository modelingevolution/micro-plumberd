using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace MicroPlumberd.SpecFlow.SourceGenerators;

static class Extensions
{
    public static bool HasAttribute(this INamedTypeSymbol type, string name)
    {
        var attrs = type.GetAttributes();
        return attrs.Any(attr => attr.AttributeClass?.Name.Contains(name) ?? false);
    }

    public static IEnumerable<INamedTypeSymbol> GetAcceptedTypes(this IMethodSymbol method)
    {
        var acceptedTypeAttributes= method.GetAttributes()
            .Where(attr => attr.AttributeClass?.Name.StartsWith("AcceptedType") ?? false);
        foreach(var acceptedTypeAttribute in acceptedTypeAttributes){
                
            var acceptedTypeArgument = acceptedTypeAttribute?.ConstructorArguments.FirstOrDefault();

            if (acceptedTypeArgument?.Value is INamedTypeSymbol acceptedTypeSymbol)
                yield return acceptedTypeSymbol;
        }
    }
}