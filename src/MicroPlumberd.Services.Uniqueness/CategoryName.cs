using System.Reflection;

namespace MicroPlumberd.Services.Uniqueness;

/// <summary>Resolves a category type's table name once: <see cref="IUniqueCategoryProvider.Category"/>
/// when implemented, otherwise the type's name.</summary>
static class CategoryName<TCategory>
{
    public static readonly string Value =
        typeof(TCategory).IsAssignableTo(typeof(IUniqueCategoryProvider))
            ? (string)typeof(TCategory).GetProperty(nameof(IUniqueCategoryProvider.Category),
                  BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!
            : typeof(TCategory).Name;
}
