namespace MicroPlumberd.Services.Identity.Aggregates;

public class ConcurrencyException : Exception
{
    public ConcurrencyException(string message) : base(message)
    {
    }
}