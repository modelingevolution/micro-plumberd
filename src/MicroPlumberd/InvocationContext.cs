using System.Collections.Concurrent;
using System.Dynamic;
using MicroPlumberd;


//public class InvocationContext
//{
//    public InvocationContext SetCorrelation(Guid correlationId) => Set("$correlationId", correlationId);

//    public InvocationContext SetCausation(Guid causationId) => Set("$causationId", causationId);

//    public InvocationContext SetUserId(string? userId)
//    {
//        if (userId != null)
//        {
//            Value.UserId = userId;
//        }

//        return this;
        
//    }
//    public string? UserId() => TryGetValue<string>("UserId", out var v) ? v : null;
//    public Guid? CausactionId() => TryGetValue<Guid>("$causationId", out var v) ? v : null;
    
//    private static AsyncLocal<InvocationContext> _current = new AsyncLocal<InvocationContext>();
//    public static InvocationContext Current
//    {
//        get => _current.Value ?? (_current.Value = new InvocationContext());
//        set => _current.Value = value;
//    }

//    private readonly ExpandoObject _data;
//    private InvocationContext()
//    {
//        _data = new ExpandoObject();
//    }
//    private InvocationContext(ExpandoObject data)
//    {
//        _data = data;
//    }

//    public dynamic Value => _data;
    
//    public InvocationContext Set(string key, object value)
//    {
//        var dict  = (IDictionary<string, object>)_data!;
//        dict[key] = value;
//        return this;
//    }
//    public bool ContainsProperty(string propertyName) => ((IDictionary<string, object>)_data!).ContainsKey(propertyName);

//    public bool TryGetValue<TValue>(string propertyName, out TValue value)
//    {
//        var dict = (IDictionary<string, object>)_data!;
//        if (dict.TryGetValue(propertyName, out var v))
//        {
//            value = (TValue)v;
//            return true;
//        }

//        value = default;
//        return false;
//    }

    
//    public void Clear()
//    {
//        IDictionary<string, object> obj = _data!;
//        obj.Clear();
//    }

//    public void ClearCorrelation()
//    {
//        var dict = (IDictionary<string, object>)_data!;
//        dict.Remove("$correlationId");
//    }

//    public Guid? CorrelationId() => TryGetValue<Guid>("$correlationId", out var v) ? v : null;

//    //public static void Build(InvocationContext context, Metadata metadata)
//    //{
//    //    if (metadata.CorrelationId() != null)
//    //        context.SetCorrelation(metadata.CorrelationId()!.Value);
//    //    else context.ClearCorrelation();
//    //    context.SetCausation(metadata.CausationId() != null ? metadata.CausationId()!.Value : metadata.EventId);
//    //}

//    public InvocationContext Clone()
//    {
//        var dictOriginal = _data as IDictionary<string, object>; // ExpandoObject supports IDictionary
//        var dst = new ExpandoObject();
//        var dictClone = dst as IDictionary<string, object>;

//        // Shallow copy, for deep copy you need a different approach
//        foreach (var kvp in dictOriginal) dictClone[kvp.Key] = kvp.Value; 

//        return new InvocationContext(dst);
//    }
//}