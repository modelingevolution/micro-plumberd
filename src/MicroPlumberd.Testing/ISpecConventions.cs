public interface ISpecConventions
{
    Func<Type, string> AggregateTypeSubjectConvention { get; set; }
    Func<Type, string> CommandHandlerTypeSubjectConvention { get; set; }
}