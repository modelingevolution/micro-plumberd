using Microsoft.EntityFrameworkCore;

namespace MicroPlumberd.Services.Uniqueness;

/// <summary>One context (and one table) per uniqueness category. The unique index on Name IS the lock.</summary>
class UniquenessDb<TCategory>(DbContextOptions<UniquenessDb<TCategory>> options) : DbContext(options)
{
    public DbSet<UniqueNameReservation> Reservations => Set<UniqueNameReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<UniqueNameReservation>();
        e.ToTable(CategoryName<TCategory>.Value);
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(400).IsRequired();
        e.HasIndex(x => x.Name).IsUnique();      // <- the hard lock
        e.HasIndex(x => x.SourceId);
    }
}
