using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

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

class VersionDuckTyping
{
    private readonly ConcurrentDictionary<Type, Action<object, long>> _setters = new();
    private readonly ConcurrentDictionary<Type, Func<object, long>> _getters = new();

    public long GetVersion(object instance)
    {
        return _getters.GetOrAdd(instance.GetType(), x =>
        {
            var prop = x.GetProperty("Version");
            if (prop != null) return DelegateHelper.CreateLongGetter(prop);
            return null;
        })?.Invoke(instance) ?? -1;
    }


    public void SetVersion(object instance, long value)
    {
        _setters.GetOrAdd(instance.GetType(), x =>
        {
            var prop = x.GetProperty("Version");
            if (prop != null) return DelegateHelper.CreateLongSetter(prop);
            return null;
        })?.Invoke(instance, value);
    }
}
class IdDuckTyping
{
    private readonly ConcurrentDictionary<Type, Action<object, object>> _setters = new();
    private readonly ConcurrentDictionary<Type, Func<object, object>> _getters = new();

    public object GetId(object instance)
    {
        return _getters.GetOrAdd(instance.GetType(), x =>
        {
            var prop = x.GetProperty("Id");
            if (prop != null) return DelegateHelper.CreateGetter(prop);
            return null;
        })?.Invoke(instance);
    }
    
    
    public void SetId(object instance, object value)
    {
        _setters.GetOrAdd(instance.GetType(), x =>
        {
            var prop = x.GetProperty("Id");
            if (prop != null) return DelegateHelper.CreateSetter(prop);
            return null;
        })?.Invoke(instance, value);
    }
}
internal static class DelegateHelper
{
    public static Action<object, long> CreateLongSetter(PropertyInfo propertyInfo)
    {
        // Create a parameter expression for the instance (object)
        var instanceParam = Expression.Parameter(typeof(object), "instance");

        // Create a parameter expression for the value (long)
        var valueParam = Expression.Parameter(typeof(long), "value");

        // Create a property expression
        var propertyExpr = Expression.Property(Expression.Convert(instanceParam, propertyInfo.DeclaringType), propertyInfo);

        // Create an assignment expression
        var assignExpr = Expression.Assign(propertyExpr, valueParam);

        // Compile the lambda expression to create the setter delegate
        return Expression.Lambda<Action<object, long>>(assignExpr, instanceParam, valueParam).Compile();
    }
    public static Func<object, long> CreateLongGetter(PropertyInfo propertyInfo)
    {
        // Create a parameter expression for the instance (object)
        var instanceParam = Expression.Parameter(typeof(object), "instance");

        // Create a property expression
        var propertyExpr = Expression.Property(Expression.Convert(instanceParam, propertyInfo.DeclaringType), propertyInfo);

        // Compile the lambda expression to create the getter delegate
        return Expression.Lambda<Func<object, long>>(propertyExpr, instanceParam).Compile();
    }
    public static Action<object, object> CreateSetter(PropertyInfo propertyInfo)
    {
        // Create a parameter expression for the instance (object)
        var instanceParam = Expression.Parameter(typeof(object), "instance");

        // Create a parameter expression for the value (object)
        var valueParam = Expression.Parameter(typeof(object), "value");

        // Create a property expression
        var propertyExpr = Expression.Property(Expression.Convert(instanceParam, propertyInfo.DeclaringType), propertyInfo);

        // Create an assignment expression
        var assignExpr = Expression.Assign(propertyExpr, Expression.Convert(valueParam, propertyInfo.PropertyType));

        // Compile the lambda expression to create the setter delegate
        return Expression.Lambda<Action<object, object>>(assignExpr, instanceParam, valueParam).Compile();
    }

    public static Func<object, object> CreateGetter(PropertyInfo propertyInfo)
    {
        // Create a parameter expression for the instance (object)
        var instanceParam = Expression.Parameter(typeof(object), "instance");

        // Create a property expression
        var propertyExpr = Expression.Property(Expression.Convert(instanceParam, propertyInfo.DeclaringType), propertyInfo);

        // Compile the lambda expression to create the getter delegate
        return Expression.Lambda<Func<object, object>>(Expression.Convert(propertyExpr, typeof(object)), instanceParam).Compile();
    }
}