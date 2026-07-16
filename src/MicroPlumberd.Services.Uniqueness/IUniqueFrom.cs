namespace MicroPlumberd.Services.Uniqueness;

/// <summary>
/// Derives the unique key of <typeparamref name="TCategory"/> from a command or event.
/// </summary>
/// <remarks>DECLARATIVE ONLY — see <see cref="UniqueAttribute{TCategory}"/>; nothing invokes this yet.</remarks>
public interface IUniqueFrom<out TCategory, in TCommand>
{
    /// <summary>Projects the command onto its uniqueness key.</summary>
    static abstract TCategory From(TCommand cmd);
}
