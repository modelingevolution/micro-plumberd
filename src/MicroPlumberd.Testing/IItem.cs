public interface IItem<T>
{
    Guid Id { get; }
    T Data { get; }
}