using CareerConnect.Api.Contracts;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareerConnect.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/resumes")]
public class ResumesController(IResumeService resumes) : ApiControllerBase
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
    public async Task<ActionResult<ResumeResponse>> Update(Guid id, SaveResumeRequest request)
    {
        var updated = await resumes.UpdateAsync(UserId, id, request);
        return updated is null ? NotFound() : Ok(updated);
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
