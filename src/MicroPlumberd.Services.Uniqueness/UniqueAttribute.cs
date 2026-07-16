namespace MicroPlumberd.Services.Uniqueness;

/// <summary>
/// Marks a property (or class) as unique within <typeparamref name="TCategory"/>.
/// </summary>
/// <remarks>
/// DECLARATIVE ONLY — nothing reads this attribute yet. Reservation is performed by calling
/// <see cref="IUniqueNameReservation{TCategory}"/> explicitly from a command handler. Automatic
/// wiring from this attribute (source-generated reserve/confirm around the aggregate write) is
/// not implemented.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public class UniqueAttribute<TCategory> : Attribute, IUniqueCategoryProvider
{
    /// <inheritdoc />
    public static string Category => typeof(TCategory).Name;
}
