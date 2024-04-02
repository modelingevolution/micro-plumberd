using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using EventStore.Client;
using MicroPlumberd;
using Xunit;

public class AggregateSpecs<T>(SpecsRoot root) where T : IAggregate<T>, ITypeRegister
{
    public IArgumentProvider ArgumentProvider => root.ArgumentProvider;
    

    public async Task When(Action<T> mth)
    {
        var subject = root.Conventions.AggregateTypeSubjectConvention(typeof(T));
        var aggregateId = root.SubjectPool.GetOrCreate(subject);
        await When(aggregateId, mth);
    }

    private async Task When(Guid aggregateId, Action<T> mth)
    {
        var streamId = root.Plumber.Config.Conventions.ProjectionCategoryStreamConvention(typeof(T));
        var bg = root.Plumber.Client.ReadStreamAsync(Direction.Backwards, streamId, StreamPosition.End, 10, false);
        var first = await bg.FirstAsync();
        var pos = first.OriginalEventNumber;
        try
        {
            
            var a = await root.Plumber.Get<T>(aggregateId);
            mth(a);
            var publishedEvents = a.PendingEvents;
            await root.Plumber.SaveChanges(a);
            root.RegisterStepExecution<T>(StepType.When, pos, publishedEvents);
        }
        catch(Exception ex)
        {
            root.RegisterStepExecutionFailed<T>(StepType.When, ex, pos);
        }
    }

    public Task Given<TEvent>(string id, TEvent ev) => Given<TEvent>(Guid.TryParse(id, out var g) ? g : id.ToGuid(), ev);
    public async Task Given<TEvent>(Guid id, TEvent ev)
    {
        var subject = root.Conventions.AggregateTypeSubjectConvention(typeof(T));
        root.SubjectPool.Store(subject, id);
        var eventHandler = typeof(T);
        var streamId = root.Plumber.Config.Conventions.GetStreamIdConvention(eventHandler, id);
        var eventName = root.Plumber.Config.Conventions.GetEventNameConvention(eventHandler, typeof(TEvent));
        await root.Plumber.AppendEvent(streamId, StreamState.Any, eventName, ev);
        root.RegisterStepExecution<T>(StepType.Given, ev);
    }
    public Task Given<TEvent>(TEvent ev)
    {
        var subject = root.Conventions.AggregateTypeSubjectConvention(typeof(T));
        var aggregateId = root.SubjectPool.GetOrCreate(subject);
        return Given<TEvent>(aggregateId, ev);
    }

    public async Task ThenThrown<TException>() => await ExpectedThrown<TException>(x => true);

    public async Task ExpectedThrown<TException>(Expression<Predicate<TException>> ex)
    {
        var prv = root.ExecutedSteps.AsEnumerable()
            .Reverse()
            .TakeWhile(x => x.Type == StepType.When)
            .Where(x => x.Exception != null)
            .Select(x => x.Exception)
            .ToArray();

        var func = ex.Compile();

        if (prv.OfType<TException>().Any(e => func(e)))
            return;

        StringBuilder sb = new StringBuilder();
        sb.Append($"No exception was thrown of type: {typeof(TException).GetFriendlyName()} with {ex.ToString()} predicate");

        if (prv.Any())
        {
            sb.Append(" But those exceptions were thrown:\n");
            foreach (var i in prv.Reverse())
            {
                sb.Append($"Exception of type {i.GetType().GetFriendlyName()}: {i.Message}.");
                if (i is TException e && !func(e))
                {
                    sb.Append(" Provided predicate returned FALSE.");
                }

                sb.AppendLine();
            }
        }
        
        // Should construct message with what happend, just like in FluentAssertions.
        Assert.Fail(sb.ToString());
    }
    public async Task ExpectedPublished<TEvent>(TEvent ev)
    {
        if (ev == null) throw new ArgumentNullException("Event cannot be null");
        
        var prv = root.ExecutedSteps.AsEnumerable()
            .Reverse()
            .TakeWhile(x => x.Type == StepType.When)
            .Last();

        var pos = prv.PreCategoryStreamPosition ?? (StreamPosition.Start);
        var evts = await root.Plumber.Read<T>(pos+1).ToArrayAsync();
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
        var subject = root.Conventions.AggregateTypeSubjectConvention(typeof(T));
        var aggregateId = root.SubjectPool.GetOrCreate(subject);
        return Then(aggregateId, func);
    }
    public async Task Then(Guid aggregateId, Action<T> assertion)
    {
        var subject = root.Conventions.AggregateTypeSubjectConvention(typeof(T));
        root.SubjectPool.Store(subject, aggregateId);
        var agg = await root.Plumber.Get<T>(aggregateId);
        assertion(agg);
    }

    public Guid AnotherSubject()
    {
        var subject = root.Conventions.AggregateTypeSubjectConvention(typeof(T));
        return root.SubjectPool.A(subject);
    }
}