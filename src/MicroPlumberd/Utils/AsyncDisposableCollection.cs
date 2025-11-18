using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using Guid = System.Guid;

namespace MicroPlumberd.Utils;

/// <summary>
/// Represents a collection of <see cref="IAsyncDisposable"/> objects that can be disposed asynchronously as a group.
/// </summary>
public class AsyncDisposableCollection : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _items = new();

    /// <summary>
    /// Creates a new instance of <see cref="AsyncDisposableCollection"/>.
    /// </summary>
    /// <returns>A new <see cref="AsyncDisposableCollection"/> instance.</returns>
    public static AsyncDisposableCollection New() => new AsyncDisposableCollection();

    /// <summary>
    /// Adds an <see cref="IAsyncDisposable"/> item to the collection.
    /// </summary>
    /// <param name="left">The collection to add to.</param>
    /// <param name="right">The item to add.</param>
    /// <returns>The collection with the added item.</returns>
    public static AsyncDisposableCollection operator +(AsyncDisposableCollection left, IAsyncDisposable right)
    {
        left._items.Add(right);
        return left;
    }

    /// <summary>
    /// Asynchronously disposes all items in the collection.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (var i in _items)
            await i.DisposeAsync();
    }

}

/// <summary>
/// Provides duck-typed access to Version properties on objects using reflection and compiled expressions.
/// </summary>
internal class VersionDuckTyping
{
    private readonly ConcurrentDictionary<Type, Action<object, long>> _setters = new();
    private readonly ConcurrentDictionary<Type, Func<object, long>> _getters = new();

    /// <summary>
    /// Gets the Version property value from an object instance.
    /// </summary>
    /// <param name="instance">The object instance to get the version from.</param>
    /// <returns>The version value, or -1 if the Version property doesn't exist.</returns>
    public long GetVersion(object instance)
    {
        return _getters.GetOrAdd(instance.GetType(), x =>
        {
            var prop = x.GetProperty("Version");
            if (prop != null) return DelegateHelper.CreateLongGetter(prop);
            return null;
        })?.Invoke(instance) ?? -1;
    }

    /// <summary>
    /// Sets the Version property value on an object instance.
    /// </summary>
    /// <param name="instance">The object instance to set the version on.</param>
    /// <param name="value">The version value to set.</param>
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
/// <summary>
/// Provides duck-typed access to Id properties on objects using reflection and compiled expressions.
/// </summary>
public class IdDuckTyping
{
    private readonly ConcurrentDictionary<Type, Action<object, object>> _setters = new();
    private readonly ConcurrentDictionary<Type, Func<object, object>> _getters = new();

    /// <summary>
    /// Gets the singleton instance of <see cref="IdDuckTyping"/>.
    /// </summary>
    public static readonly IdDuckTyping Instance = new IdDuckTyping();

    /// <summary>
    /// Attempts to get the Id property value as a Guid from an object instance.
    /// </summary>
    /// <param name="instance">The object instance to get the Id from.</param>
    /// <param name="id">When this method returns, contains the Id as a Guid if successful; otherwise, <see cref="Guid.Empty"/>.</param>
    /// <returns><c>true</c> if the Id was successfully retrieved; otherwise, <c>false</c>.</returns>
    public bool TryGetGuidId(object instance, out Guid id)
    {
        var tmp = GetId(instance);
        if (tmp == null)
        {
            id = Guid.Empty;
            return false;
        }
        id = (tmp is Guid guid ? guid : Guid.Parse(tmp.ToString()));
        return true;
    }

    /// <summary>
    /// Gets the Id property value from an object instance.
    /// </summary>
    /// <param name="instance">The object instance to get the Id from.</param>
    /// <returns>The Id value, or <c>null</c> if the Id property doesn't exist.</returns>
    public object? GetId(object instance)
    {
        return _getters.GetOrAdd(instance.GetType(), x =>
        {
            var prop = x.GetProperty("Id");
            if (prop != null) return DelegateHelper.CreateGetter(prop);
            return null;
        })?.Invoke(instance);
    }

    /// <summary>
    /// Sets the Id property value on an object instance.
    /// </summary>
    /// <param name="instance">The object instance to set the Id on.</param>
    /// <param name="value">The Id value to set.</param>
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
/// <summary>
/// Provides helper methods for creating compiled expression-based property getters and setters.
/// </summary>
internal static class DelegateHelper
{
    /// <summary>
    /// Creates a compiled setter delegate for a long property.
    /// </summary>
    /// <param name="propertyInfo">The property to create a setter for.</param>
    /// <returns>A compiled delegate that sets the property value.</returns>
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

    /// <summary>
    /// Creates a compiled getter delegate for a long property.
    /// </summary>
    /// <param name="propertyInfo">The property to create a getter for.</param>
    /// <returns>A compiled delegate that gets the property value.</returns>
    public static Func<object, long> CreateLongGetter(PropertyInfo propertyInfo)
    {
        // Create a parameter expression for the instance (object)
        var instanceParam = Expression.Parameter(typeof(object), "instance");

        // Create a property expression
        var propertyExpr = Expression.Property(Expression.Convert(instanceParam, propertyInfo.DeclaringType), propertyInfo);

        // Compile the lambda expression to create the getter delegate
        return Expression.Lambda<Func<object, long>>(propertyExpr, instanceParam).Compile();
    }

    /// <summary>
    /// Creates a compiled setter delegate for an object property.
    /// </summary>
    /// <param name="propertyInfo">The property to create a setter for.</param>
    /// <returns>A compiled delegate that sets the property value.</returns>
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

    /// <summary>
    /// Creates a compiled getter delegate for an object property.
    /// </summary>
    /// <param name="propertyInfo">The property to create a getter for.</param>
    /// <returns>A compiled delegate that gets the property value.</returns>
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