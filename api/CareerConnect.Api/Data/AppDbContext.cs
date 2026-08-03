using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<StatusChange> StatusChanges => Set<StatusChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Email).HasMaxLength(320);
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<Application>(app =>
        {
            app.Property(a => a.CompanyName).HasMaxLength(200);
            app.Property(a => a.RoleTitle).HasMaxLength(200);
            app.Property(a => a.JobPostingUrl).HasMaxLength(2048);
            // Stored as strings: readable in the DB, and immune to enum reordering.
            app.Property(a => a.Status).HasConversion<string>().HasMaxLength(50);

            app.HasOne(a => a.User)
               .WithMany(u => u.Applications)
               .HasForeignKey(a => a.UserId)
               .OnDelete(DeleteBehavior.Cascade);

            app.HasIndex(a => new { a.UserId, a.Status });
            app.HasIndex(a => new { a.UserId, a.DateApplied });
        });

        modelBuilder.Entity<StatusChange>(change =>
        {
            change.Property(c => c.FromStatus).HasConversion<string>().HasMaxLength(50);
            change.Property(c => c.ToStatus).HasConversion<string>().HasMaxLength(50);
            change.Property(c => c.Source).HasConversion<string>().HasMaxLength(50);

            change.HasOne(c => c.Application)
                  .WithMany(a => a.StatusHistory)
                  .HasForeignKey(c => c.ApplicationId)
                  .OnDelete(DeleteBehavior.Cascade);

            change.HasIndex(c => c.ApplicationId);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Application>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = utcNow;
                entry.Entity.UpdatedAtUtc = utcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = utcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
