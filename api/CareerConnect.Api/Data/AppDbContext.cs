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
    public DbSet<PrepRun> PrepRuns => Set<PrepRun>();
    public DbSet<InterviewEvent> InterviewEvents => Set<InterviewEvent>();
    public DbSet<InterviewQuestionEntry> InterviewQuestions => Set<InterviewQuestionEntry>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Email).HasMaxLength(320);
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.DisplayName).HasMaxLength(200);
            user.Property(u => u.Plan).HasConversion<string>().HasMaxLength(20);

            user.HasMany(u => u.PasswordResetTokens)
                .WithOne(t => t.User)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Application>(app =>
        {
            app.Property(a => a.CompanyName).HasMaxLength(200);
            app.Property(a => a.RoleTitle).HasMaxLength(200);
            app.Property(a => a.JobPostingUrl).HasMaxLength(2048);
            app.Property(a => a.TailoredResumeLayout).HasJsonConversion();
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
            resume.Property(r => r.Layout).HasJsonConversion();

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

        modelBuilder.Entity<PrepRun>(run =>
        {
            run.Property(r => r.Status).HasConversion<string>().HasMaxLength(50);
            run.Property(r => r.Steps).HasPrepStepListConversion();
            run.Property(r => r.Review).HasJsonConversion();
            run.Property(r => r.Instructions).HasMaxLength(1000);
            run.Property(r => r.Changes).HasJsonListConversion();

            run.HasOne(r => r.Application)
               .WithMany(a => a.PrepRuns)
               .HasForeignKey(r => r.ApplicationId)
               .OnDelete(DeleteBehavior.Cascade);

            run.HasIndex(r => new { r.ApplicationId, r.StartedAtUtc });
        });

        modelBuilder.Entity<InterviewEvent>(interview =>
        {
            interview.Property(i => i.Kind).HasConversion<string>().HasMaxLength(50);
            interview.Property(i => i.Source).HasConversion<string>().HasMaxLength(50);
            interview.Property(i => i.CalendarEventId).HasMaxLength(1024);
            interview.Property(i => i.Debrief).HasJsonConversion();

            interview.HasOne(i => i.Application)
                     .WithMany(a => a.Interviews)
                     .HasForeignKey(i => i.ApplicationId)
                     .OnDelete(DeleteBehavior.Cascade);

            // Every read is "what's coming up", so the schedule is the index.
            interview.HasIndex(i => new { i.ApplicationId, i.ScheduledAtUtc });
        });

        modelBuilder.Entity<PasswordResetToken>(token =>
        {
            token.Property(t => t.TokenHash).HasMaxLength(64);

            // Every lookup is by hash — that's all the server is given.
            token.HasIndex(t => t.TokenHash).IsUnique();
        });

        modelBuilder.Entity<InterviewQuestionEntry>(question =>
        {
            question.Property(q => q.Side).HasConversion<string>().HasMaxLength(50);
            question.Property(q => q.Kind).HasConversion<string>().HasMaxLength(50);
            question.Property(q => q.Quality).HasConversion<string>().HasMaxLength(50);
            question.Property(q => q.Text).HasMaxLength(1000);
            question.Property(q => q.Answer).HasMaxLength(8000);

            question.HasOne(q => q.Interview)
                    .WithMany(i => i.Questions)
                    .HasForeignKey(q => q.InterviewEventId)
                    .OnDelete(DeleteBehavior.Cascade);

            // Read as two ordered lists per interview, and swept across every
            // interview for the question bank.
            question.HasIndex(q => new { q.InterviewEventId, q.Side, q.Position });
        });

        modelBuilder.Entity<ActivityEvent>(activity =>
        {
            activity.Property(a => a.Trigger).HasConversion<string>().HasMaxLength(50);
            activity.Property(a => a.FromStatus).HasConversion<string>().HasMaxLength(50);
            activity.Property(a => a.ToStatus).HasConversion<string>().HasMaxLength(50);
            activity.Property(a => a.EmailSubject).HasMaxLength(1000);
            activity.Property(a => a.EmailFrom).HasMaxLength(500);

            activity.HasOne(a => a.Application)
                    .WithMany()
                    .HasForeignKey(a => a.ApplicationId)
                    .OnDelete(DeleteBehavior.Cascade);

            // The feed is always "this user's recent changes".
            activity.HasIndex(a => new { a.UserId, a.CreatedAtUtc });
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
