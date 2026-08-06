using CareerConnect.Api.Contracts;
using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public interface IResumeService
{
    Task<List<ResumeSummaryResponse>> ListAsync(Guid userId);
    Task<ResumeResponse?> GetAsync(Guid userId, Guid id);
    Task<ResumeResponse?> GetActiveAsync(Guid userId);
    Task<ResumeResponse> CreateAsync(Guid userId, SaveResumeRequest request);
    Task<ResumeUploadOutcome> CreateFromFileAsync(Guid userId, Stream file, string fileName, string? label);
    Task<ResumeResponse?> UpdateAsync(Guid userId, Guid id, SaveResumeRequest request);
    Task<ResumeResponse?> SetActiveAsync(Guid userId, Guid id);
    Task<DeleteResumeOutcome> DeleteAsync(Guid userId, Guid id);
}

public enum DeleteResumeOutcome
{
    Deleted,
    NotFound,
    /// <summary>Blocked: match results reference it, and deleting would erase their context.</summary>
    HasMatchResults
}

public abstract record ResumeUploadOutcome
{
    public sealed record Success(ResumeResponse Resume) : ResumeUploadOutcome;
    public sealed record Failed(string Message) : ResumeUploadOutcome;
}

public class ResumeService(AppDbContext db, IResumeFileTextExtractor extractor) : IResumeService
{
    private const int MinContentLength = 50;

    public async Task<List<ResumeSummaryResponse>> ListAsync(Guid userId) =>
        await db.Resumes
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.IsActive)
            .ThenByDescending(r => r.UpdatedAtUtc)
            .Select(r => new ResumeSummaryResponse
            {
                Id = r.Id,
                Label = r.Label,
                IsActive = r.IsActive,
                CharacterCount = r.Content.Length,
                UpdatedAtUtc = r.UpdatedAtUtc,
            })
            .ToListAsync();

    public async Task<ResumeResponse?> GetAsync(Guid userId, Guid id)
    {
        var resume = await db.Resumes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id);
        return resume is null ? null : ToResponse(resume);
    }

    public async Task<ResumeResponse?> GetActiveAsync(Guid userId)
    {
        var resume = await db.Resumes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.IsActive);
        return resume is null ? null : ToResponse(resume);
    }

    public async Task<ResumeResponse> CreateAsync(Guid userId, SaveResumeRequest request)
    {
        var utcNow = DateTime.UtcNow;

        // The first resume becomes active automatically — otherwise scoring
        // would fail on a freshly-populated account for no obvious reason.
        var isFirst = !await db.Resumes.AnyAsync(r => r.UserId == userId);

        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Label = request.Label.Trim(),
            Content = request.Content.Trim(),
            IsActive = isFirst,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
        };

        db.Resumes.Add(resume);
        await db.SaveChangesAsync();
        return ToResponse(resume);
    }

    public async Task<ResumeUploadOutcome> CreateFromFileAsync(
        Guid userId, Stream file, string fileName, string? label)
    {
        string? text;
        try
        {
            text = extractor.Extract(file, fileName);
        }
        catch (Exception)
        {
            return new ResumeUploadOutcome.Failed(
                "Couldn't read this file — it may be corrupted or password-protected.");
        }

        if (text is null)
        {
            return new ResumeUploadOutcome.Failed("Only .pdf and .docx files are supported.");
        }

        if (text.Trim().Length < MinContentLength)
        {
            return new ResumeUploadOutcome.Failed(
                "Couldn't find enough readable text in this file. If it's a scanned or " +
                "image-based PDF, try pasting the resume text directly instead.");
        }

        var resolvedLabel = string.IsNullOrWhiteSpace(label)
            ? Path.GetFileNameWithoutExtension(fileName)
            : label.Trim();
        // SaveResumeRequest.Label is capped at 200 chars via data annotation,
        // which isn't enforced here since this isn't going through model binding.
        if (resolvedLabel.Length > 200)
        {
            resolvedLabel = resolvedLabel[..200];
        }

        var resume = await CreateAsync(userId, new SaveResumeRequest
        {
            Label = resolvedLabel,
            Content = text.Trim(),
        });

        return new ResumeUploadOutcome.Success(resume);
    }

    public async Task<ResumeResponse?> UpdateAsync(Guid userId, Guid id, SaveResumeRequest request)
    {
        var resume = await db.Resumes.FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id);
        if (resume is null)
        {
            return null;
        }

        resume.Label = request.Label.Trim();
        resume.Content = request.Content.Trim();
        resume.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return ToResponse(resume);
    }

    public async Task<ResumeResponse?> SetActiveAsync(Guid userId, Guid id)
    {
        var resumes = await db.Resumes.Where(r => r.UserId == userId).ToListAsync();
        var target = resumes.FirstOrDefault(r => r.Id == id);
        if (target is null)
        {
            return null;
        }

        foreach (var resume in resumes)
        {
            resume.IsActive = resume.Id == id;
        }

        await db.SaveChangesAsync();
        return ToResponse(target);
    }

    public async Task<DeleteResumeOutcome> DeleteAsync(Guid userId, Guid id)
    {
        var resume = await db.Resumes.FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id);
        if (resume is null)
        {
            return DeleteResumeOutcome.NotFound;
        }

        if (await db.MatchResults.AnyAsync(m => m.ResumeId == id))
        {
            return DeleteResumeOutcome.HasMatchResults;
        }

        db.Resumes.Remove(resume);
        await db.SaveChangesAsync();

        // Removing the active resume would silently disable scoring; promote the
        // most recently updated survivor instead.
        if (resume.IsActive)
        {
            var replacement = await db.Resumes
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.UpdatedAtUtc)
                .FirstOrDefaultAsync();

            if (replacement is not null)
            {
                replacement.IsActive = true;
                await db.SaveChangesAsync();
            }
        }

        return DeleteResumeOutcome.Deleted;
    }

    private static ResumeResponse ToResponse(Resume r) => new()
    {
        Id = r.Id,
        Label = r.Label,
        Content = r.Content,
        IsActive = r.IsActive,
        CreatedAtUtc = r.CreatedAtUtc,
        UpdatedAtUtc = r.UpdatedAtUtc,
    };
}
