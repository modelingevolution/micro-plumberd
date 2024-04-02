using System.Reflection;

namespace MicroPlumberd.Testing
{
    

    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class DslFromAssemblyAttribute(string assembly) : Attribute
    {

    }
}
