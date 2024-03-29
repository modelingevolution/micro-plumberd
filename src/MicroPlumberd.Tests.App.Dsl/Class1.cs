using System.Collections.Concurrent;
using MicroPlumberd.Testing;
using MicroPlumberd.Tests.AppSrc;
using System.Formats.Asn1;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using EventStore.Client;
using MicroPlumberd;
using MicroPlumberd.Services;
using MicroPlumberd.Tests.App.Dsl;
using MicroPlumberd.Tests.App.Srv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpecFlow.Internal.Json;
using TechTalk.SpecFlow;
using TechTalk.SpecFlow.Bindings;
using Xunit;
using Xunit.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

[assembly: DslFromAssembly("MicroPlumberd.Tests.App")]

[Binding]
public class AppSteps
{
    private readonly AppStepsContext _context;

    public AppSteps(AppStepsContext context)
    {
        _context = context;
    }
    
    [Given(@"the app is up and running")]
    public async Task GivenTheAppIsUpAndRunning()
    {
        _context.EventStore = await EventStoreServer.Create().StartInDocker();

        _context.App = new AppHost(_context.Output)
                .Configure(x => x
                    .AddPlumberd(_context.EventStore.GetEventStoreSettings()));
        
        await _context.App.StartAsync();
    }

    
}

public interface IArgumentProvider
{
    T RecognizeYaml<T>(string yaml);
    T RecognizeTable<T>(Table table);
    T Recognize<T>(object arg);
}

class ArgumentProvider : IArgumentProvider
{
    public T RecognizeYaml<T>(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)  // see height_in_inches in sample yml 
            .Build();
        var obj = deserializer.Deserialize(yaml);
        var json = JsonSerializer.Serialize(obj);

        var ev = JsonSerializer.Deserialize<T>(json);
        return ev;
    }

    public T RecognizeTable<T>(Table table)
    {
        StringBuilder sb = new StringBuilder();

        if (table.Header.Count == 2 && table.Header.Contains("Property") && table.Header.Contains("Value"))
        {
            foreach (var i in table.Rows)
            {
                var prop = i["Property"];
                var value = i["Value"];
                sb.AppendLine($"{prop}: {value}");
            }
        }
        else if (table.RowCount == 1)
        {
            foreach (var i in table.Header)
            {
                sb.AppendLine($"{i}: {table.Rows[0][i]}");
            }
        }
        else
            throw new NotSupportedException(
                "2 options are supported: 2 column table with Property and Value is supported and table with one row, where headers are name of the properties. ");

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)  // see height_in_inches in sample yml 
            .Build();
        var obj = deserializer.Deserialize(sb.ToString());

        var json = JsonSerializer.Serialize(obj);

        return JsonSerializer.Deserialize<T>(json);
    }

    public T Recognize<T>(object s) =>
        s switch
        {
            string s1 when !string.IsNullOrWhiteSpace(s1) => RecognizeYaml<T>(s1),
            Table t => RecognizeTable<T>(t),
            _ => default
        };
}

[Binding]
public class AppStepsContext(ITestOutputHelper output, ScenarioContext context)
{
    
    public ITestOutputHelper Output { get; } = output;
    public AppHost? App { get; set; }
    public EventStoreServer? EventStore { get; set; }

    private SpecsRoot? _specs;
    
    public SpecsRoot SpecsRoot =>
        _specs ?? (_specs = new SpecsRoot(App.Host.Services.GetRequiredService<IPlumber>(),
            new StepInfoProvider(context)));

    public Specs<T> Specs<T>() where T : IAggregate<T>, ITypeRegister => SpecsRoot.For<T>();
    [AfterScenario]
    public async Task Cleanup()
    {
        App?.Dispose();
        if (EventStore != null)
            await EventStore.DisposeAsync();
    }
}

public interface IStepInfoProvider
{
    string CurrentStepName { get; }
    Table Table { get; }
    string Multiline { get; }
}


class StepInfoProvider(ScenarioContext scenarioContext) : IStepInfoProvider
{
    public string CurrentStepName => scenarioContext.StepContext.StepInfo.Text;
    public Table Table => scenarioContext.StepContext.StepInfo.Table;
    public string Multiline => scenarioContext.StepContext.StepInfo.MultilineText;
}

public class Specs<T>(SpecsRoot parent) where T : IAggregate<T>, ITypeRegister
{
    private readonly VariablePool _variables = new VariablePool();
    public IArgumentProvider ArgumentProvider => parent.ArgumentProvider;
    class VariablePool
    {
        private Dictionary<string, Guid> _index = new();
        private List<Guid> _list = new();

        public VariablePool()
        {
            var defaultId = Guid.NewGuid();
            _list.Add(defaultId);
            _index.Add("default", defaultId);
        }
        public Guid Current() => _list.Last();
    }

     public async Task When(Action<T> mth)
    {
        var streamId = parent.Plumber.Config.Conventions.ProjectionCategoryStreamConvention(typeof(T));
        var bg = parent.Plumber.Client.ReadStreamAsync(Direction.Backwards, streamId, StreamPosition.End, 10, false);
        var first = await bg.FirstAsync();
        var pos = first.OriginalEventNumber;
        try
        {
            var a = await parent.Plumber.Get<T>(_variables.Current());
            mth(a);
            var publishedEvents = a.PendingEvents;
            await parent.Plumber.SaveChanges(a);
            parent.RegisterStepExecution<T>(StepType.When, pos, publishedEvents);
        }
        catch(Exception ex)
        {
            parent.RegisterStepExecutionFailed<T>(StepType.When, ex, pos);
        }
    }
    public async Task Given<TEvent>(TEvent ev)
    {
        var eventHandler = typeof(T);
        var id = _variables.Current();
        var streamId = parent.Plumber.Config.Conventions.GetStreamIdConvention(eventHandler, id);
        var eventName = parent.Plumber.Config.Conventions.GetEventNameConvention(eventHandler, typeof(TEvent));
        await parent.Plumber.AppendEvent(streamId, StreamState.Any, eventName, ev);
        parent.RegisterStepExecution<T>(StepType.Given, ev);
    }

    public async Task ThenThrown<TException>() => await ThenThrown<TException>(x => true);

    public async Task ThenThrown<TException>(Expression<Predicate<TException>> ex)
    {
        var prv = parent.ExecutedSteps.AsEnumerable()
            .Reverse()
            .TakeWhile(x => x.Type == StepType.When)
            .Where(x => x.Exception != null)
            .Select(x => x.Exception)
            .ToArray();

        var func = ex.Compile();

        if (prv.OfType<TException>().Any(e => func(e)))
            return;

        StringBuilder sb = new StringBuilder();
        sb.Append($"No exception was thrown of type: {typeof(TException).Name} with {ex.ToString()} predicate.");

        if (prv.Any())
        {
            sb.Append(" But those exceptions were thrown:\n");
            foreach (var i in prv.Reverse())
                sb.AppendLine($"Exception of type {i.GetType().Name}: {i.Message}");
        }
        
        // Should construct message with what happend, just like in FluentAssertions.
        Assert.Fail(sb.ToString());
    }
    public async Task Then<TEvent>(TEvent ev)
    {
        if (ev == null) throw new ArgumentNullException("Event cannot be null");
        
        var prv = parent.ExecutedSteps.AsEnumerable()
            .Reverse()
            .TakeWhile(x => x.Type == StepType.When)
            .Last();

        var pos = prv.PreCategoryStreamPosition ?? (StreamPosition.Start);
        var evts = await parent.Plumber.Read<FooAggregate>(pos+1).ToArrayAsync();
        if (evts.OfType<TEvent>().Any(x => ev.Equals(x)))
            return;
        Assert.Fail("No event found in the stream.");
    }
}

internal class Step
{
    public Type HandlerType { get; init; }
    public object[] Events { get; init; }
    public string Text { get; init; }
    public StepType Type { get; init; }
    public Exception? Exception { get; set; }
    public StreamPosition? PreCategoryStreamPosition { get; init; }
}
enum StepType
{
    Given, When, Then
}

public class SpecsRoot
{
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
    internal void RegisterStepExecutionFailed<T>(StepType type, Exception ex, StreamPosition pos){
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

    public Specs<T> For<T>() where T : IAggregate<T>, ITypeRegister
    {
        return (Specs<T>)_specs.GetOrAdd(typeof(T), x => new Specs<T>(this));
    }
   
}

public partial class AppHost : IDisposable
{
    public IHost Host { get; private set; }
    private readonly ITestOutputHelper logger;

    public AppHost(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    public virtual AppHost Configure(Action<IServiceCollection>? configure = null)
    {
        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureLogging(x => x.SetMinimumLevel(LogLevel.Trace)
                .AddDebug()
                .AddXunit(logger))
            .ConfigureServices(services =>
            {
                configure(services);
            })
            .Build();

        return this;
    }

    public void Dispose()
    {
        Host?.Dispose();
    }


    public async Task<IServiceProvider> StartAsync()
    {
        await Host.StartAsync();
        await Task.Delay(1000);
        return Host.Services;
    }
}