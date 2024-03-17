namespace MicroPlumberd.Utils;

public class AsyncDisposableCollection : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _items = new();
    public static AsyncDisposableCollection New() => new AsyncDisposableCollection();
    public static AsyncDisposableCollection operator +(AsyncDisposableCollection left, IAsyncDisposable right)
    {
        left._items.Add(right);
        return left;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var i in _items)
            await i.DisposeAsync();
    }

}