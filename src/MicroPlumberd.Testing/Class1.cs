using System.Reflection;
using Xunit;

namespace MicroPlumberd.Testing
{
    

    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class DslFromAssemblyAttribute(string assembly) : Attribute
    {

    }

    [CollectionDefinition("Specs")]
    public class TestCollection : ICollectionFixture<SpecsContext>
    {
        // This class has no code and is never created. Its purpose is solely
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }

    public class SpecsContext(IPlumber plumber)
    {
        
        private Dictionary<Type, Dictionary<string, object>> _index = new();
        
    }
}
