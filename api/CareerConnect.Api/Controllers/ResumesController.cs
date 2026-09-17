using CareerConnect.Api.Contracts;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareerConnect.Api.Controllers;

[ApiController]
[Authorize]
// The resume library exists to feed tailoring and scoring; on Free there is
// nothing to feed, so the whole controller is Pro.
[ProOnly]
[Route("api/resumes")]
public class ResumesController(IResumeService resumes, IResumeRenderer renderer) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ResumeSummaryResponse>>> List() =>
        Ok(await resumes.ListAsync(UserId));

    [HttpGet("active")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeResponse>> GetActive()
    {
        var resume = await resumes.GetActiveAsync(UserId);
        return resume is null ? NotFound() : Ok(resume);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeResponse>> Get(Guid id)
    {
        var resume = await resumes.GetAsync(UserId, id);
        return resume is null ? NotFound() : Ok(resume);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ResumeResponse>> Create(SaveResumeRequest request)
    {
        var created = await resumes.CreateAsync(UserId, request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Creates a resume from an uploaded .pdf or .docx by extracting its text.</summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(10_000_000)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ResumeResponse>> Upload(IFormFile? file, [FromForm] string? label)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "Choose a file to upload.", Status = 400 });
        }

        await using var stream = file.OpenReadStream();
        var outcome = await resumes.CreateFromFileAsync(UserId, stream, file.FileName, label);

        return outcome switch
        {
            ResumeUploadOutcome.Success success =>
                CreatedAtAction(nameof(Get), new { id = success.Resume.Id }, success.Resume),
            ResumeUploadOutcome.Failed failed =>
                BadRequest(new ProblemDetails { Title = failed.Message, Status = 400 }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ResumeResponse>> Update(Guid id, SaveResumeRequest request) =>
        await resumes.UpdateAsync(UserId, id, request) switch
        {
            ResumeUpdateOutcome.Updated updated => Ok(updated.Resume),
            ResumeUpdateOutcome.LayoutLocked => Conflict(new ProblemDetails
            {
                Title = "This resume's text comes from its PDF",
                Detail = "Editing it here would make the text and the PDF disagree. Upload an updated PDF instead.",
                Status = StatusCodes.Status409Conflict,
            }),
            _ => NotFound(),
        };

    /// <summary>True things that don't fit on the page, for tailoring to draw on.</summary>
    [HttpPut("{id:guid}/extra-facts")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeResponse>> UpdateExtraFacts(Guid id, UpdateExtraFactsRequest request)
    {
        var updated = await resumes.UpdateExtraFactsAsync(UserId, id, request.ExtraFacts);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>The resume redrawn from its stored layout — what every tailored version starts from.</summary>
    [HttpGet("{id:guid}/pdf")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pdf(Guid id)
    {
        var found = await resumes.GetLayoutAsync(UserId, id);
        if (found is not { } resume)
        {
            return NotFound();
        }

        return File(renderer.Render(resume.Layout), "application/pdf", $"{ResumeFileNames.Safe(resume.Label)}.pdf");
    }

    [HttpPatch("{id:guid}/active")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumeResponse>> SetActive(Guid id)
    {
        var updated = await resumes.SetActiveAsync(UserId, id);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id) =>
        await resumes.DeleteAsync(UserId, id) switch
        {
            DeleteResumeOutcome.Deleted => NoContent(),
            DeleteResumeOutcome.NotFound => NotFound(),
            _ => Conflict(new ProblemDetails
            {
                Title = "This resume has match results attached",
                Detail = "Deleting it would remove the context behind those scores. Replace its text instead.",
                Status = StatusCodes.Status409Conflict,
            }),
        };
}
