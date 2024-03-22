namespace MicroPlumberd.Services.Uniqueness;

interface IUniqueNameReservation<TProvider>
    where TProvider : IUniqueCategoryProvider
{
    Task Reserve(string name, Guid source, TimeSpan? duration = null);
    Task Confirm(Guid source);

    Task<bool> RollbackReservation(Guid reservationId);
    Task<bool> DeleteConfirmedNameReservation(Guid reservationId);
}