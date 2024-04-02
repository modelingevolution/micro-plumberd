using MicroPlumberd;
using MicroPlumberd.Services;
using MicroPlumberd.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TechTalk.SpecFlow;
using Xunit.Abstractions;

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