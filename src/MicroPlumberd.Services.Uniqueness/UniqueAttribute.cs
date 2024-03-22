namespace MicroPlumberd.Services.Uniqueness;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public class UniqueAttribute<TCategory> : Attribute, IUniqueCategoryProvider
{
       
    public static string Category => typeof(TCategory).Name;
}