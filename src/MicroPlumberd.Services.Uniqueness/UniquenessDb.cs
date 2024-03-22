using Microsoft.EntityFrameworkCore;

namespace MicroPlumberd.Services.Uniqueness;

class UniquenessDb<TProvider> : DbContext
    where TProvider:IUniqueCategoryProvider
{
    public DbSet<UniqueNameReservation> Reservations { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UniqueNameReservation>().ToTable(TProvider.Category);
        modelBuilder.Entity<UniqueNameReservation>().HasIndex(x => new { x.Name }).IsUnique(true);
        modelBuilder.Entity<UniqueNameReservation>().HasIndex(x => new { x.SourceId }).IsUnique(false);
    }
}