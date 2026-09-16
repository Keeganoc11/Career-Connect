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
    Task<ResumeUpdateOutcome> UpdateAsync(Guid userId, Guid id, SaveResumeRequest request);
    Task<ResumeResponse?> UpdateExtraFactsAsync(Guid userId, Guid id, string? extraFacts);
    /// <summary>The resume drawn in its own layout, or null when it has none (or doesn't exist).</summary>
    Task<(ResumeLayout Layout, string Label)?> GetLayoutAsync(Guid userId, Guid id);
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

public abstract record ResumeUpdateOutcome
{
    public sealed record Updated(ResumeResponse Resume) : ResumeUpdateOutcome;
    public sealed record NotFound : ResumeUpdateOutcome;
    /// <summary>The text comes from an uploaded PDF's layout; editing it here would split the two.</summary>
    public sealed record LayoutLocked : ResumeUpdateOutcome;
}

public abstract record ResumeUploadOutcome
{
    public sealed record Success(ResumeResponse Resume) : ResumeUploadOutcome;
    public sealed record Failed(string Message) : ResumeUploadOutcome;
}

public class ResumeService(
    AppDbContext db,
    IResumeFileTextExtractor extractor,
    IResumeLayoutReader layoutReader) : IResumeService
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
                HasLayout = r.Layout != null,
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
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        string? text;
        try
        {
            text = extractor.Extract(new MemoryStream(bytes), fileName);
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

        // A PDF is also read as positioned lines, which is what lets tailoring
        // hand back a file in this exact format. When that works its text is
        // the cleaner of the two, so it becomes the stored content too.
        ResumeLayout? layout = null;
        string? layoutWarning = null;
        if (Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            switch (layoutReader.Read(bytes))
            {
                case ResumeLayoutReadOutcome.Success success:
                    layout = success.Layout;
                    text = layout.ToPlainText();
                    break;
                case ResumeLayoutReadOutcome.Failed failed:
                    layoutWarning = failed.Message;
                    break;
            }
        }
        else
        {
            layoutWarning = "Tailored resumes are drawn in your uploaded PDF's exact format, so upload a PDF to use tailoring.";
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

        var created = await CreateAsync(userId, new SaveResumeRequest
        {
            Label = resolvedLabel,
            Content = text.Trim(),
        });

        if (layout is not null)
        {
            var resume = await db.Resumes.FirstAsync(r => r.Id == created.Id);
            resume.Layout = layout;
            await db.SaveChangesAsync();
            created = ToResponse(resume);
        }

        return new ResumeUploadOutcome.Success(created with { LayoutWarning = layoutWarning });
    }

    public async Task<ResumeUpdateOutcome> UpdateAsync(Guid userId, Guid id, SaveResumeRequest request)
    {
        var resume = await db.Resumes.FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id);
        if (resume is null)
        {
            return new ResumeUpdateOutcome.NotFound();
        }

        var content = request.Content.Trim();
        if (resume.Layout is not null && content != resume.Content)
        {
            return new ResumeUpdateOutcome.LayoutLocked();
        }

        resume.Label = request.Label.Trim();
        resume.Content = content;
        resume.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return new ResumeUpdateOutcome.Updated(ToResponse(resume));
    }

    public async Task<ResumeResponse?> UpdateExtraFactsAsync(Guid userId, Guid id, string? extraFacts)
    {
        var resume = await db.Resumes.FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id);
        if (resume is null)
        {
            return null;
        }

        resume.ExtraFacts = string.IsNullOrWhiteSpace(extraFacts) ? null : extraFacts.Trim();
        resume.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToResponse(resume);
    }

    public async Task<(ResumeLayout Layout, string Label)?> GetLayoutAsync(Guid userId, Guid id)
    {
        var resume = await db.Resumes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id);
        return resume?.Layout is null ? null : (resume.Layout, resume.Label);
    }

    public async Task<ResumeResponse?> SetActiveAsync(Guid userId, Guid id)
    {
        var target = await db.Resumes.FirstOrDefaultAsync(r => r.UserId == userId && r.Id == id);
        if (target is null)
        {
            return null;
        }

        // Only the currently-active row(s) — usually just one — need to change;
        // no reason to pull every resume's body into memory for this.
        var currentlyActive = await db.Resumes
            .Where(r => r.UserId == userId && r.IsActive && r.Id != id)
            .ToListAsync();
        foreach (var resume in currentlyActive)
        {
            resume.IsActive = false;
        }

        target.IsActive = true;
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
        HasLayout = r.Layout is not null,
        ExtraFacts = r.ExtraFacts,
        CreatedAtUtc = r.CreatedAtUtc,
        UpdatedAtUtc = r.UpdatedAtUtc,
    };
}
