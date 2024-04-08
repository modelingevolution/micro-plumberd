using System.Collections.Concurrent;
using EventStore.Client;
using MicroPlumberd;
using MicroPlumberd.Services;

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
    public AggregateSpecs<T> Aggregate<T>() where T : IAggregate<T>, ITypeRegister, IId
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