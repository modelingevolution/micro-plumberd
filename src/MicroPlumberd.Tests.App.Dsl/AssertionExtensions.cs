namespace MicroPlumberd.Tests.App.Dsl;

public static class AssertionExtensions
{
    public static T State<T>(this IAggregateStateAccessor<T> aggregate)
    {
        return aggregate.State;
    }
}