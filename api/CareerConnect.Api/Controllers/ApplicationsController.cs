using CareerConnect.Api.Contracts;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareerConnect.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/applications")]
public class ApplicationsController(
    IApplicationService applications,
    IMatchScoringService matches,
    IJobPostingIngestService jobPostings) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ApplicationResponse>>> List() =>
        Ok(await applications.ListAsync(UserId));

    /// <summary>Fetches a job posting URL and extracts company/role/description to prefill the add-application form. Creates nothing itself.</summary>
    [HttpPost("extract-from-url")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<JobPostingExtractionResponse>> ExtractFromUrl(
        ExtractJobPostingRequest request, CancellationToken cancellationToken)
    {
        var outcome = await jobPostings.IngestAsync(request.Url, cancellationToken);

        return outcome switch
        {
            JobPostingIngestOutcome.Success success => Ok(new JobPostingExtractionResponse
            {
                CompanyName = success.CompanyName,
                RoleTitle = success.RoleTitle,
                JobDescriptionText = success.JobDescriptionText,
            }),

            JobPostingIngestOutcome.Failed { Reason: JobPostingIngestFailureReason.NotAJobPosting } failed
                => StatusCode(StatusCodes.Status422UnprocessableEntity, new ProblemDetails
                {
                    Title = failed.Message,
                    Status = StatusCodes.Status422UnprocessableEntity,
                }),

            JobPostingIngestOutcome.Failed { Reason: JobPostingIngestFailureReason.ExtractorUnavailable } failed
                => StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
                {
                    Title = failed.Message,
                    Status = StatusCodes.Status503ServiceUnavailable,
                }),

            JobPostingIngestOutcome.Failed failed
                => StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Title = failed.Message,
                    Status = StatusCodes.Status502BadGateway,
                }),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    [HttpGet("summary")]
    public async Task<ActionResult<SummaryResponse>> Summary() =>
        Ok(await applications.GetSummaryAsync(UserId));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationResponse>> Get(Guid id)
    {
        var application = await applications.GetAsync(UserId, id);
        return application is null ? NotFound() : Ok(application);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApplicationResponse>> Create(CreateApplicationRequest request)
    {
        var created = await applications.CreateAsync(UserId, request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationResponse>> Update(Guid id, UpdateApplicationRequest request)
    {
        var updated = await applications.UpdateAsync(UserId, id, request);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationResponse>> UpdateStatus(Guid id, UpdateStatusRequest request)
    {
        var updated = await applications.UpdateStatusAsync(UserId, id, request.Status);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id) =>
        await applications.DeleteAsync(UserId, id) ? NoContent() : NotFound();

    /// <summary>Latest stored match result per application, keyed by application id.</summary>
    [HttpGet("matches")]
    public async Task<ActionResult<Dictionary<Guid, MatchResultResponse>>> Matches() =>
        Ok(await matches.GetLatestForAllAsync(UserId));

    [HttpGet("{id:guid}/match")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MatchResultResponse>> GetMatch(Guid id)
    {
        var match = await matches.GetLatestAsync(UserId, id);
        return match is null ? NotFound() : Ok(match);
    }

    /// <summary>Runs a fresh resume/job-description comparison and stores the result.</summary>
    [HttpPost("{id:guid}/match")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MatchResultResponse>> Score(Guid id, CancellationToken cancellationToken)
    {
        var outcome = await matches.ScoreAsync(UserId, id, cancellationToken);

        return outcome switch
        {
            ScoreOutcome.Success success => Ok(success.Result),

            ScoreOutcome.Failed { Reason: MatchFailureReason.ApplicationNotFound } => NotFound(),

            // Preconditions the user can fix (no JD, no active resume) are 409:
            // the request was well-formed, the data isn't ready yet.
            ScoreOutcome.Failed failed and { Reason: MatchFailureReason.NoJobDescription or MatchFailureReason.NoActiveResume }
                => Conflict(Problem(failed, StatusCodes.Status409Conflict)),

            ScoreOutcome.Failed failed and { Reason: MatchFailureReason.AnalyzerUnavailable }
                => StatusCode(StatusCodes.Status503ServiceUnavailable, Problem(failed, StatusCodes.Status503ServiceUnavailable)),

            ScoreOutcome.Failed failed
                => StatusCode(StatusCodes.Status502BadGateway, Problem(failed, StatusCodes.Status502BadGateway)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static ProblemDetails Problem(ScoreOutcome.Failed failed, int status)
    {
        var problem = new ProblemDetails
        {
            Title = failed.Message,
            Status = status,
        };

        // Machine-readable reason goes in an extension, not Detail — Detail is
        // rendered to the user and the enum name is not a sentence.
        problem.Extensions["reason"] = failed.Reason.ToString();
        return problem;
    }
}
