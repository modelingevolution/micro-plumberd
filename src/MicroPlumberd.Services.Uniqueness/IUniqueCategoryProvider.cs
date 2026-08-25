namespace MicroPlumberd.Services.Uniqueness;

/// <summary>
/// Optional: implement on a category type to override the table name used for its
/// reservations. When not implemented, the table name defaults to the category type's name.
/// </summary>
public interface IUniqueCategoryProvider
{
    /// <summary>The table name used for this category's reservations.</summary>
    static abstract string Category { get; }
}
