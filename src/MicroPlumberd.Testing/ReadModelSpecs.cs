using Microsoft.Extensions.DependencyInjection;

public class ReadModelSpecs<T>(SpecsRoot root)
{
    public async Task When<TValue>(Func<T, Task<TValue>> valueExtractor)
    {
        await using var scope = root.Plumber.Config.ServiceProvider.CreateAsyncScope();
        var model = scope.ServiceProvider.GetRequiredService<T>();
        await Task.Delay(5000);
        var value = await valueExtractor(model);
        root.RegisterQueryStepExecution<T>(StepType.When, value);
    }

    public void ThenQueryResult(Action<object> assertion)
    {
        var queryResults = root.ExecutedSteps
            .Reverse()
            .TakeWhile(x => x.Type == StepType.When)
            .Where(x=> x.QueryResult != null && x.HandlerType == typeof(T))
            .Select(x=>x.QueryResult)
            .ToArray();

        foreach (var qr in queryResults) 
            assertion(qr);
    }
}