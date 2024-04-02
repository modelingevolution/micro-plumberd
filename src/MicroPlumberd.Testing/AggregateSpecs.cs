using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using EventStore.Client;
using MicroPlumberd;
using Xunit;

public class AggregateSpecs<T>(SpecsRoot parent) where T : IAggregate<T>, ITypeRegister
{
    public IArgumentProvider ArgumentProvider => parent.ArgumentProvider;
    

    public async Task When(Action<T> mth)
    {
        var aggregateId = _subjects.Current();
        await When(aggregateId, mth);
    }

    private async Task When(Guid aggregateId, Action<T> mth)
    {
        var streamId = parent.Plumber.Config.Conventions.ProjectionCategoryStreamConvention(typeof(T));
        var bg = parent.Plumber.Client.ReadStreamAsync(Direction.Backwards, streamId, StreamPosition.End, 10, false);
        var first = await bg.FirstAsync();
        var pos = first.OriginalEventNumber;
        try
        {
            
            var a = await parent.Plumber.Get<T>(aggregateId);
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

    public Task Given<TEvent>(string id, TEvent ev) => Given<TEvent>(Guid.TryParse(id, out var g) ? g : id.ToGuid(), ev);
    public async Task Given<TEvent>(Guid id, TEvent ev)
    {
        var eventHandler = typeof(T);
        var streamId = parent.Plumber.Config.Conventions.GetStreamIdConvention(eventHandler, id);
        var eventName = parent.Plumber.Config.Conventions.GetEventNameConvention(eventHandler, typeof(TEvent));
        await parent.Plumber.AppendEvent(streamId, StreamState.Any, eventName, ev);
        parent.RegisterStepExecution<T>(StepType.Given, ev);
    }
    public Task Given<TEvent>(TEvent ev)
    {
        var id = _subjects.Current();
        return Given<TEvent>(id, ev);
    }

    public async Task ThenThrown<TException>() => await ExpectedThrown<TException>(x => true);

    public async Task ExpectedThrown<TException>(Expression<Predicate<TException>> ex)
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
    public async Task ExpectedPublished<TEvent>(TEvent ev)
    {
        if (ev == null) throw new ArgumentNullException("Event cannot be null");
        
        var prv = parent.ExecutedSteps.AsEnumerable()
            .Reverse()
            .TakeWhile(x => x.Type == StepType.When)
            .Last();

        var pos = prv.PreCategoryStreamPosition ?? (StreamPosition.Start);
        var evts = await parent.Plumber.Read<T>(pos+1).ToArrayAsync();
        if (evts.OfType<TEvent>().Any(x => ev.Equals(x)))
            return;
        StringBuilder sb = new StringBuilder($"No event found in the stream of type: {typeof(TEvent).Name} that are equal. Maybe you have forgotten to override Equals or use records instead of class???");
        if (evts.Any())
        {
            sb.Append(" But found: ");
            foreach (var e in evts)
            {
                sb.AppendLine(JsonSerializer.Serialize(e));
            }
        }
        Assert.Fail(sb.ToString());
    }

    public Task Then(Action<T> func)
    {
        var aggregateId = _subjects.Current();
        return Then(aggregateId, func);
    }
    public async Task Then(Guid aggregateId, Action<T> assertion)
    {
        var agg = await  parent.Plumber.Get<T>(aggregateId);
        assertion(agg);
    }
}