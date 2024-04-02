using MicroPlumberd;

class SpecConventions : ISpecConventions
{
    public Func<Type, string> AggregateTypeSubjectConvention { get; set; } = x => x.GetFriendlyName().Remove("Aggregate");
    public Func<Type, string> CommandHandlerTypeSubjectConvention { get; set; } = x => x.GetFriendlyName().Remove("CommandHandler");
}