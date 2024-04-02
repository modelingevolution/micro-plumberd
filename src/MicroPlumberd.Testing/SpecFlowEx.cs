using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using MicroPlumberd.Testing;
using System.Formats.Asn1;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EventStore.Client;
using MicroPlumberd;
using MicroPlumberd.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpecFlow.Internal.Json;
using TechTalk.SpecFlow;
using TechTalk.SpecFlow.Bindings;
using Xunit.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;




public interface IArgumentProvider
{
    T RecognizeFromYaml<T>(string yaml);
    T RecognizeFromTable<T>(Table table);
    IReadOnlyList<IItem<T>> RecognizeManyFromTable<T>(Table table);
    T Recognize<T>(object arg);
    object Recognize(Table table);
    object Recognize(string yaml);
    object Recognize(object s);
}
public static class DynamicClassEmitter
{
    public static object ToAnonymousObject(this IDictionary<string, object> dict)
    {
        var type = EmitClassFromDictionary(dict);
        var instance = Activator.CreateInstance(type);

        foreach (var kvp in dict)
        {
            var property = type.GetProperty(kvp.Key);
            property.SetValue(instance, kvp.Value, null);
        }

        return instance;
    }

    private static int _counter = 0;
    public static Type EmitClassFromDictionary(IDictionary<string, object> dictionary)
    {
        var assemblyName = new AssemblyName("DynamicAssembly_" + Interlocked.Increment(ref _counter));
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");

        // Define a public class named 'DynamicClass'.
        var typeBuilder = moduleBuilder.DefineType("DynamicClass", TypeAttributes.Public);

        // For each key-value pair in the dictionary, define a public property.
        foreach (var kvp in dictionary)
        {
            var propertyName = kvp.Key;
            var propertyType = kvp.Value.GetType();

            // Define a private field.
            var fieldBuilder = typeBuilder.DefineField("_" + propertyName, propertyType, FieldAttributes.Private);
            // Define the property.
            var propertyBuilder = typeBuilder.DefineProperty(propertyName, PropertyAttributes.HasDefault, propertyType, null);

            // Define the 'get' accessor for the property.
            var getMethodBuilder = typeBuilder.DefineMethod("get_" + propertyName, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, propertyType, Type.EmptyTypes);
            var getIlGenerator = getMethodBuilder.GetILGenerator();
            getIlGenerator.Emit(OpCodes.Ldarg_0);
            getIlGenerator.Emit(OpCodes.Ldfld, fieldBuilder);
            getIlGenerator.Emit(OpCodes.Ret);
            propertyBuilder.SetGetMethod(getMethodBuilder);

            // Define the 'set' accessor for the property.
            var setMethodBuilder = typeBuilder.DefineMethod("set_" + propertyName, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, null, new Type[] { propertyType });
            var setIlGenerator = setMethodBuilder.GetILGenerator();
            setIlGenerator.Emit(OpCodes.Ldarg_0);
            setIlGenerator.Emit(OpCodes.Ldarg_1);
            setIlGenerator.Emit(OpCodes.Stfld, fieldBuilder);
            setIlGenerator.Emit(OpCodes.Ret);
            propertyBuilder.SetSetMethod(setMethodBuilder);
        }

        // Create the type.
        var resultType = typeBuilder.CreateTypeInfo().AsType();
        return resultType;
    }
}

public interface IItem<T>
{
    Guid Id { get; }
    T Data { get; }
}
class ArgumentProvider : IArgumentProvider
{
    public T RecognizeFromYaml<T>(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)  // see height_in_inches in sample yml 
            .Build();
        var obj = deserializer.Deserialize(yaml);
        var json = JsonSerializer.Serialize(obj);

        var ev = JsonSerializer.Deserialize<T>(json);
        return ev;
    }

    public T RecognizeFromTable<T>(Table table)
    {
        var obj = ToDictionary(table);
        return RecognizeDictionary<T>(obj);
    }

    private static T RecognizeDictionary<T>(IDictionary<string, object> obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonSerializer.Deserialize<T>(json);
    }

    public record Item<T>(Guid Id, T Data) : IItem<T>;
    public IReadOnlyList<IItem<T>> RecognizeManyFromTable<T>(Table table)
    {
        var props = table.Header.Except(["Id"]).ToArray();
        var result = new List<IItem<T>>();
        foreach (var r in table.Rows)
        {
            Guid id = Guid.NewGuid();
            if (table.ContainsColumn("Id"))
                id = Guid.TryParse(r["Id"], out var i) ? i : r["Id"].ToGuid();
            var data = props.ToDictionary(h => h, h => (object)r[h]);
            var tmp = new Item<T>(id, RecognizeDictionary<T>(data));
            result.Add(tmp);
        }

        return result;
    }

    private IDictionary<string, object> ToDictionary(Table table)
    {
        Dictionary<string, object> ret = new();
        if (table.Header.Count == 2 && table.Header.Contains("Property") && table.Header.Contains("Value"))
        {
            foreach (var i in table.Rows)
            {
                var prop = i["Property"];
                var value = i["Value"];
                ret.Add(prop, value);
            }
        }
        else if (table.RowCount == 1)
        {
            foreach (var i in table.Header)
            {
                ret.Add(i, table.Rows[0][i]);
            }
        }

        return ret;
    }
    public T Recognize<T>(object s) =>
        s switch
        {
            string s1 when !string.IsNullOrWhiteSpace(s1) => RecognizeFromYaml<T>(s1),
            Table t => RecognizeFromTable<T>(t),
            _ => default
        };
    public object Recognize(object s) =>
        s switch
        {
            string s1 when !string.IsNullOrWhiteSpace(s1) => Recognize(s1),
            Table t => Recognize(t),
            _ => default
        };

    public object Recognize(Table table)
    {
        var dict = ToDictionary(table);
        var anonymous = dict.ToAnonymousObject();
        return anonymous;
    }
    public object Recognize(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)  // see height_in_inches in sample yml 
            .Build();
        var dict = deserializer.Deserialize<Dictionary<string,object>>(yaml);
        var anonymous = dict.ToAnonymousObject();
        return anonymous;
    }
}

[Binding]
public class AppStepsContext(ITestOutputHelper output, ScenarioContext context)
{
    
    public ITestOutputHelper Output { get; } = output;
    public IHost? App { get; set; }
    public EventStoreServer? EventStore { get; set; }

    private SpecsRoot? _specs;
    
    public SpecsRoot SpecsRoot =>
        _specs ?? (_specs = new SpecsRoot(App.Services.GetRequiredService<IPlumber>(),
            new StepInfoProvider(context)));

    public AggregateSpecs<T> AggregateSpecs<T>() where T : IAggregate<T>, ITypeRegister => SpecsRoot.Aggregate<T>();
    public CommandHandlerSpecs<T> CommandHandlerSpecs<T>() where T : IServiceTypeRegister => SpecsRoot.CommandHandler<T>();
    
    [AfterScenario]
    public void Cleanup()
    {
        App?.Dispose();
        EventStore?.Dispose();
    }

    public ReadModelSpecs<T> ModelSpecs<T>() where T : IEventHandler
    {
        return SpecsRoot.ReadModel<T>();
    }
}

public interface IStepInfoProvider
{
    string CurrentStepName { get; }
    Table Table { get; }
    string Multiline { get; }
}

static class Extensions
{
    public static string Remove(this string t, string word) => t.Replace(word, string.Empty);
    public static byte[] ToHash(this string t)
    {
        using (SHA256 shA256 = SHA256.Create())
        {
            byte[] hash = shA256.ComputeHash(Encoding.Default.GetBytes(t));
            ulong uint64_1 = BitConverter.ToUInt64(hash, 0);
            ulong uint64_2 = BitConverter.ToUInt64(hash, 8);
            ulong uint64_3 = BitConverter.ToUInt64(hash, 16);
            ulong uint64_4 = BitConverter.ToUInt64(hash, 24);
            ulong num1 = uint64_1 ^ uint64_3;
            ulong num2 = uint64_2 ^ uint64_4;
            Memory<byte> memory = new Memory<byte>(new byte[16]);
            BitConverter.TryWriteBytes(memory.Span, num1);
            BitConverter.TryWriteBytes(memory.Slice(8, 8).Span, num2);
            return memory.ToArray();
        }
    }

    public static Guid ToGuid(this string t) => new Guid(t.ToHash());

    public static byte[] NameHash(this Type t1)
    {
        return _hashCache.GetOrAdd(t1, (Func<Type, byte[]>) (t2 => t2.FullName.ToHash()));
    }
    private static ConcurrentDictionary<Type, byte[]> _hashCache = new ConcurrentDictionary<Type, byte[]>();

    public static Guid NameId(this Type t) => new Guid(t.NameHash());
}
class StepInfoProvider(ScenarioContext scenarioContext) : IStepInfoProvider
{
    public string CurrentStepName => scenarioContext.StepContext.StepInfo.Text;
    public Table Table => scenarioContext.StepContext.StepInfo.Table;
    public string Multiline => scenarioContext.StepContext.StepInfo.MultilineText;
}

public class CommandHandlerSpecs<TCommandHandler>(SpecsRoot root) where TCommandHandler :IServiceTypeRegister
{
    private readonly ICommandBus _bus = root.Plumber.Config.ServiceProvider.GetRequiredService<ICommandBus>();

    public Task When<TCommand>(TCommand cmd)
    {
        var subject = root.Conventions.CommandHandlerTypeSubjectConvention(typeof(TCommandHandler));
        var id = root.SubjectPool.GetOrCreate(subject);
        return When(id, cmd);
    }
    public async Task When<TCommand>(Guid recipient, TCommand cmd)
    {
        if (cmd == null) throw new ArgumentNullException($"Command {typeof(TCommand).Name} cannot be null.");
        var subject = root.Conventions.CommandHandlerTypeSubjectConvention(typeof(TCommandHandler));
        root.SubjectPool.Store(subject, recipient);
        try
        {
            await _bus.SendAsync(recipient, cmd);
            root.RegisterStepExecution<TCommand>(StepType.When, cmd);
        }
        catch (Exception ex)
        {
            root.RegisterStepExecutionFailed<TCommand>(StepType.When, ex);
        }
    }
}

public interface ISubjectPool
{
    Guid Store(string subject, Guid id);
    Guid A(string subject);
    Guid The(string subject);
    Guid GetOrCreate(string subject);
}

class SubjectPool : ISubjectPool
{
    private readonly Dictionary<string, List<Guid>> _index = new();

    public Guid Store(string subject, Guid id)
    {
        if (!_index.TryGetValue(subject, out var l))
            return WithNewList(subject, id)[0];
        if(l.Last() != id)
            l.Add(id);
        return id;
    }

    public Guid A(string subject)
    {
        if (!_index.TryGetValue(subject, out var l)) 
            return WithNewList(subject);
        var r = Guid.NewGuid();
        l.Add(r);
        return r;
    }

    public Guid The(string subject)
    {
        if (_index.TryGetValue(subject, out var l))
            return l.Last();
        throw new ArgumentOutOfRangeException($"Subject named {subject} was not defined.");
    }
    private Guid WithNewList(string key) => WithNewList(key, Guid.NewGuid())[0];

    private Guid[] WithNewList(string key, params Guid[] ids)
    {
        _index.Add(key, [..ids]);
        return ids;
    }
    public Guid GetOrCreate(string subject) => _index.TryGetValue(subject, out var l) ? l.Last() : WithNewList(subject);
}

internal class Step
{
    public Type HandlerType { get; init; }
    public object[] Events { get; init; }
    public object? QueryResult { get; init; }
    public string Text { get; init; }
    public StepType Type { get; init; }
    public Exception? Exception { get; set; }
    public StreamPosition? PreCategoryStreamPosition { get; init; }
}
enum StepType
{
    Given, When, Then
}

public interface ISpecConventions
{
    Func<Type, string> AggregateTypeSubjectConvention { get; set; }
    Func<Type, string> CommandHandlerTypeSubjectConvention { get; set; }
}

class SpecConventions : ISpecConventions
{
    public Func<Type, string> AggregateTypeSubjectConvention { get; set; } = x => x.GetFriendlyName().Remove("Aggregate");
    public Func<Type, string> CommandHandlerTypeSubjectConvention { get; set; } = x => x.GetFriendlyName().Remove("CommandHandler");
}
public class SpecsRoot
{
    public ISpecConventions Conventions { get; private set; } = new SpecConventions();
    public ISubjectPool SubjectPool { get; private set; } = new SubjectPool();
    public IArgumentProvider ArgumentProvider { get; set; } 
    internal void RegisterStepExecution<T>(StepType type, StreamPosition pos, params object[] events){
        _executedSteps.Add(new Step()
        {
            PreCategoryStreamPosition = pos,
            Type=type,
            Events = events,
            Text=_stepInfoProvider.CurrentStepName, 
            HandlerType = typeof(T) 
        });
    }
    internal void RegisterStepExecution<T>(StepType type,params object[] events){
        _executedSteps.Add(new Step()
        {
            Type=type,
            Events = events,
            Text=_stepInfoProvider.CurrentStepName, 
            HandlerType = typeof(T) 
        });
    }
    internal void RegisterQueryStepExecution<T>(StepType type, object queryResult){
        _executedSteps.Add(new Step()
        {
            Type=type,
            QueryResult = queryResult,
            Text=_stepInfoProvider.CurrentStepName, 
            HandlerType = typeof(T) 
        });
    }
    internal void RegisterStepExecutionFailed<T>(StepType type, Exception ex, StreamPosition? pos=null){
        _executedSteps.Add(new Step()
        {
            Type=type,
            Exception = ex,
            Text=_stepInfoProvider.CurrentStepName, 
            HandlerType = typeof(T) ,
            PreCategoryStreamPosition = pos
        });
    }

    private readonly ConcurrentDictionary<Type, object> _specs = new();
    private readonly List<Step> _executedSteps = new();
    private readonly IPlumber _plumber;
    private readonly IStepInfoProvider _stepInfoProvider;
    
    internal IPlumber Plumber => _plumber;
    internal IStepInfoProvider StepInfoProvider => _stepInfoProvider;
    internal IEnumerable<Step> ExecutedSteps => _executedSteps;
    
    public SpecsRoot(IPlumber plumber, IStepInfoProvider stepInfoProvider)
    {
        _plumber = plumber;
        _stepInfoProvider = stepInfoProvider;
        ArgumentProvider = new ArgumentProvider();
    }
    public AggregateSpecs<T> Aggregate<T>() where T : IAggregate<T>, ITypeRegister
    {
        return (AggregateSpecs<T>)_specs.GetOrAdd(typeof(T), x => new AggregateSpecs<T>(this));
    }
    public CommandHandlerSpecs<T> CommandHandler<T>() where T : IServiceTypeRegister
    {
        return (CommandHandlerSpecs<T>)_specs.GetOrAdd(typeof(T), x => new CommandHandlerSpecs<T>(this));
    }

    public ReadModelSpecs<T> ReadModel<T>() where T : IEventHandler
    {
        return (ReadModelSpecs<T>)_specs.GetOrAdd(typeof(T), x => new ReadModelSpecs<T>(this));
    }
}