namespace MicroPlumberd.Service.Identity.Aggregates;

public class ConcurrencyException : Exception
{
    public ConcurrencyException(string message) : base(message)
    {
    }
}