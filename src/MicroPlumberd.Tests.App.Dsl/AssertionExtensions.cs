namespace MicroPlumberd.Tests.App.Dsl;

public static class AssertionExtensions
{
    public static T State<T>(this IStatefull<T> aggregate)
    {
        return aggregate.State;
    }
}