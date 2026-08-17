using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<StatusChange> StatusChanges => Set<StatusChange>();
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<MatchResult> MatchResults => Set<MatchResult>();
    public DbSet<GmailConnection> GmailConnections => Set<GmailConnection>();

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

        modelBuilder.Entity<Resume>(resume =>
        {
            resume.Property(r => r.Label).HasMaxLength(200);

            resume.HasOne(r => r.User)
                  .WithMany(u => u.Resumes)
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            resume.HasIndex(r => new { r.UserId, r.IsActive });
        });

        modelBuilder.Entity<MatchResult>(match =>
        {
            match.Property(m => m.ModelId).HasMaxLength(100);

            // Keyword lists are read and written whole and never queried by
            // element, so JSON columns beat a join table here.
            match.Property(m => m.MatchedKeywords).HasStringListConversion();
            match.Property(m => m.MissingKeywords).HasStringListConversion();
            match.Property(m => m.Suggestions).HasSuggestedEditListConversion();

            match.HasOne(m => m.Application)
                 .WithMany(a => a.MatchResults)
                 .HasForeignKey(m => m.ApplicationId)
                 .OnDelete(DeleteBehavior.Cascade);

            // Deleting a resume must not erase the scores it produced, so this
            // FK restricts instead of cascading; the service blocks the delete.
            match.HasOne(m => m.Resume)
                 .WithMany(r => r.MatchResults)
                 .HasForeignKey(m => m.ResumeId)
                 .OnDelete(DeleteBehavior.Restrict);

            match.HasIndex(m => new { m.ApplicationId, m.CreatedAtUtc });
        });

        modelBuilder.Entity<GmailConnection>(gmail =>
        {
            gmail.Property(g => g.ConnectedEmail).HasMaxLength(320);

            gmail.HasOne(g => g.User)
                 .WithOne(u => u.GmailConnection)
                 .HasForeignKey<GmailConnection>(g => g.UserId)
                 .OnDelete(DeleteBehavior.Cascade);

            gmail.HasIndex(g => g.UserId).IsUnique();
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
