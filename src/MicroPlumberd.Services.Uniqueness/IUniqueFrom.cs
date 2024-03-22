namespace MicroPlumberd.Services.Uniqueness;

public interface IUniqueFrom<out TCategory, in TCommand>
{
    public static abstract TCategory From(TCommand cmd);
}