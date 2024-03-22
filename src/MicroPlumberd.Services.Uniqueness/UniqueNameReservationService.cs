using Microsoft.EntityFrameworkCore;

namespace MicroPlumberd.Services.Uniqueness;

class UniqueNameReservationService<TProvider> : IUniqueNameReservation<TProvider>
    where TProvider : IUniqueCategoryProvider
{
    public async Task Reserve(string name, Guid source, TimeSpan? duration)
    {
        if (name == null) throw new ArgumentNullException("name");
            
        duration ??= TimeSpan.FromMinutes(10);
        await using var db = new UniquenessDb<TProvider>();
        var toDelete =
            db.Reservations.Where(x => x.Name == name && x.ValidUntil < DateTime.Now && !x.IsConfirmed);
        db.Reservations.RemoveRange(toDelete);
        await db.SaveChangesAsync();

        // we might already have a confirmed reservation.
        var prvReservation =
            await db.Reservations
                .FirstOrDefaultAsync(x => x.SourceId == source && x.IsConfirmed);

        if (prvReservation != null && prvReservation.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            return;
        else
        {
            // this is brand new reservation or next reservation
            var reservation = new UniqueNameReservation()
            {
                Name = name,
                SourceId = source,
                ValidUntil = DateTime.Now.Add(duration.Value)
            };
            await db.Reservations.AddAsync(reservation);
        }

        await db.SaveChangesAsync();

    }

    public async Task Confirm(Guid source)
    {
        await using var db = new UniquenessDb<TProvider>();
        var reservation = await db.Reservations.SingleOrDefaultAsync(
            x => x.SourceId == source && !x.IsConfirmed);
        if (await db.Reservations.AnyAsync(x => x.SourceId == source && x.IsConfirmed))
            return;

        if (reservation.ValidUntil > DateTime.Now)
        {
            var prvReservation = await db.Reservations
                .FirstOrDefaultAsync(x => x.SourceId == source && x.IsConfirmed);
            if (prvReservation != null) db.Reservations.Remove(prvReservation);

            reservation.IsConfirmed = true;
            await db.SaveChangesAsync();
        }
        else
        {
            throw new Exception("Cannot confirm"); // arguably could mitigate f-ckup.
        }
    }



    public async Task<bool> RollbackReservation(Guid reservationId)
    {
        await using var db = new UniquenessDb<TProvider>();
        var toDelete = await db.Reservations
            .SingleOrDefaultAsync(x => x.SourceId == reservationId && !x.IsConfirmed );
        if (toDelete == null) return false;
        db.Reservations.Remove(toDelete);
        await db.SaveChangesAsync();
        return true;
    }
    public async Task<bool> DeleteConfirmedNameReservation(Guid reservationId)
    {
        await using var db = new UniquenessDb<TProvider>();
        var toDelete = await db.Reservations
            .SingleOrDefaultAsync(x => x.SourceId == reservationId && x.IsConfirmed );
        if (toDelete == null) return false;

        db.Reservations.Remove(toDelete);
        await db.SaveChangesAsync();
        return true;

    }
}