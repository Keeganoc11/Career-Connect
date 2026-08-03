using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public class ApplicationService(AppDbContext db) : IApplicationService
{
    public async Task<List<ApplicationResponse>> ListAsync(Guid userId)
    {
        var applications = await db.Applications
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.DateApplied)
            .ThenByDescending(a => a.CreatedAtUtc)
            .ToListAsync();

        return applications.Select(a => ToResponse(a, includeHistory: false)).ToList();
    }

    public async Task<ApplicationResponse?> GetAsync(Guid userId, Guid id)
    {
        var application = await FindWithHistoryAsync(userId, id, track: false);
        return application is null ? null : ToResponse(application, includeHistory: true);
    }

    public async Task<ApplicationResponse> CreateAsync(Guid userId, CreateApplicationRequest request)
    {
        var application = new Application
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CompanyName = request.CompanyName.Trim(),
            RoleTitle = request.RoleTitle.Trim(),
            JobPostingUrl = NormalizeOptional(request.JobPostingUrl),
            Status = request.Status,
            DateApplied = request.DateApplied,
            Notes = NormalizeOptional(request.Notes),
            JobDescriptionText = NormalizeOptional(request.JobDescriptionText),
            StatusHistory =
            [
                new StatusChange
                {
                    Id = Guid.NewGuid(),
                    FromStatus = null,
                    ToStatus = request.Status,
                    ChangedAtUtc = DateTime.UtcNow,
                    Source = StatusChangeSource.Manual
                }
            ]
        };

        db.Applications.Add(application);
        await db.SaveChangesAsync();

        return ToResponse(application, includeHistory: true);
    }

    public async Task<ApplicationResponse?> UpdateAsync(Guid userId, Guid id, UpdateApplicationRequest request)
    {
        var application = await FindWithHistoryAsync(userId, id, track: true);
        if (application is null)
        {
            return null;
        }

        application.CompanyName = request.CompanyName.Trim();
        application.RoleTitle = request.RoleTitle.Trim();
        application.JobPostingUrl = NormalizeOptional(request.JobPostingUrl);
        application.DateApplied = request.DateApplied;
        application.Notes = NormalizeOptional(request.Notes);
        application.JobDescriptionText = NormalizeOptional(request.JobDescriptionText);

        await db.SaveChangesAsync();
        return ToResponse(application, includeHistory: true);
    }

    public async Task<ApplicationResponse?> UpdateStatusAsync(Guid userId, Guid id, ApplicationStatus newStatus)
    {
        var application = await FindWithHistoryAsync(userId, id, track: true);
        if (application is null)
        {
            return null;
        }

        if (application.Status != newStatus)
        {
            var change = new StatusChange
            {
                Id = Guid.NewGuid(),
                ApplicationId = application.Id,
                FromStatus = application.Status,
                ToStatus = newStatus,
                ChangedAtUtc = DateTime.UtcNow,
                Source = StatusChangeSource.Manual
            };
            // Explicit Add: with a client-set GUID key, relationship fixup alone
            // would mark this entity Modified (an UPDATE) instead of Added.
            // Fixup then attaches it to application.StatusHistory for us.
            db.StatusChanges.Add(change);
            application.Status = newStatus;
            await db.SaveChangesAsync();
        }

        return ToResponse(application, includeHistory: true);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id)
    {
        var application = await db.Applications
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Id == id);
        if (application is null)
        {
            return false;
        }

        db.Applications.Remove(application);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<SummaryResponse> GetSummaryAsync(Guid userId)
    {
        var counts = await db.Applications
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        return new SummaryResponse
        {
            Total = counts.Sum(c => c.Count),
            Counts = Enum.GetValues<ApplicationStatus>()
                .Select(status => new StatusCountResponse
                {
                    Status = status,
                    Count = counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0
                })
                .ToList()
        };
    }

    private Task<Application?> FindWithHistoryAsync(Guid userId, Guid id, bool track)
    {
        IQueryable<Application> query = db.Applications.Include(a => a.StatusHistory);
        if (!track)
        {
            query = query.AsNoTracking();
        }

        return query.FirstOrDefaultAsync(a => a.UserId == userId && a.Id == id);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ApplicationResponse ToResponse(Application a, bool includeHistory) => new()
    {
        Id = a.Id,
        CompanyName = a.CompanyName,
        RoleTitle = a.RoleTitle,
        JobPostingUrl = a.JobPostingUrl,
        Status = a.Status,
        DateApplied = a.DateApplied,
        Notes = a.Notes,
        JobDescriptionText = a.JobDescriptionText,
        CreatedAtUtc = a.CreatedAtUtc,
        UpdatedAtUtc = a.UpdatedAtUtc,
        StatusHistory = includeHistory
            ? a.StatusHistory
                .OrderBy(c => c.ChangedAtUtc)
                .Select(c => new StatusChangeResponse
                {
                    FromStatus = c.FromStatus,
                    ToStatus = c.ToStatus,
                    ChangedAtUtc = c.ChangedAtUtc,
                    Source = c.Source
                })
                .ToList()
            : null
    };
}
