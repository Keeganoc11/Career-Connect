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
    IJobPostingIngestService jobPostings,
    ICoverLetterService coverLetters,
    IInterviewPrepService interviewPrep,
    IPrepRunService prepRuns,
    IJobCaptureService jobCapture,
    IFollowUpService followUps,
    IResumeRenderer renderer) : ApiControllerBase
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

    /// <summary>
    /// Paste a job description (or give a link) and get a tracked application
    /// with tailoring already running — the whole "add a job" flow in one call.
    /// </summary>
    [HttpPost("capture")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CaptureJobResponse>> Capture(CaptureJobRequest request, CancellationToken cancellationToken)
    {
        var outcome = await jobCapture.CaptureAsync(UserId, request, cancellationToken);

        ObjectResult Fail(int status, string title, Action<ProblemDetails>? extend = null)
        {
            var problem = new ProblemDetails { Title = title, Status = status };
            extend?.Invoke(problem);
            return StatusCode(status, problem);
        }

        return outcome switch
        {
            JobCaptureOutcome.Captured captured => CreatedAtAction(nameof(Get), new { id = captured.Application.Id }, new CaptureJobResponse
            {
                Application = captured.Application,
                PrepRun = captured.Run is null ? null : PrepRunResponse.From(captured.Run),
                PrepMessage = captured.PrepMessage,
            }),

            JobCaptureOutcome.Duplicate duplicate => Fail(
                StatusCodes.Status409Conflict,
                $"You're already tracking {duplicate.CompanyName} · {duplicate.RoleTitle}.",
                p => p.Extensions["existingApplicationId"] = duplicate.ExistingApplicationId),

            JobCaptureOutcome.Invalid invalid => Fail(StatusCodes.Status400BadRequest, invalid.Message),
            JobCaptureOutcome.NotAPosting notAPosting => Fail(StatusCodes.Status422UnprocessableEntity, notAPosting.Message),
            JobCaptureOutcome.Unavailable unavailable => Fail(StatusCodes.Status503ServiceUnavailable, unavailable.Message),
            JobCaptureOutcome.Failed failed => Fail(StatusCodes.Status502BadGateway, failed.Message),
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

    /// <summary>Saves user edits to the tailored resume and cover letter the prep pipeline generated.</summary>
    [HttpPut("{id:guid}/documents")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationResponse>> UpdateDocuments(
        Guid id, UpdateApplicationDocumentsRequest request)
    {
        var updated = await applications.UpdateDocumentsAsync(UserId, id, request);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Latest prep run per application, keyed by application id.</summary>
    [HttpGet("prep-runs")]
    public async Task<ActionResult<Dictionary<Guid, PrepRunResponse>>> PrepRuns(CancellationToken cancellationToken)
    {
        var runs = await prepRuns.GetLatestForAllAsync(UserId, cancellationToken);
        return Ok(runs.ToDictionary(pair => pair.Key, pair => PrepRunResponse.From(pair.Value)));
    }

    [HttpGet("{id:guid}/prep")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PrepRunResponse>> GetPrepRun(Guid id, CancellationToken cancellationToken)
    {
        var run = await prepRuns.GetLatestAsync(UserId, id, cancellationToken);
        return run is null ? NotFound() : Ok(PrepRunResponse.From(run));
    }

    /// <summary>
    /// Kicks off the automated prep pass — score, rewrite and re-score until it
    /// clears the target, check the changes, then write the reality check. Returns immediately with a
    /// Running run; poll GET /prep for progress.
    /// </summary>
    [HttpPost("{id:guid}/prep")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PrepRunResponse>> StartPrep(
        Guid id, [FromBody] StartPrepRequest? request, CancellationToken cancellationToken)
    {
        var outcome = await prepRuns.StartAsync(UserId, id, request?.Instructions, cancellationToken);

        return outcome switch
        {
            PrepStartOutcome.Started started
                => Accepted(PrepRunResponse.From(started.Run)),

            PrepStartOutcome.Failed { Reason: PrepStartFailureReason.ApplicationNotFound } => NotFound(),

            PrepStartOutcome.Failed failed and { Reason: PrepStartFailureReason.AiUnavailable }
                => StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
                {
                    Title = failed.Message,
                    Status = StatusCodes.Status503ServiceUnavailable,
                }),

            // Everything else is a precondition the user can fix (no JD, no
            // active resume) or a duplicate start — the request was fine, the
            // state isn't ready.
            PrepStartOutcome.Failed failed
                => Conflict(new ProblemDetails { Title = failed.Message, Status = StatusCodes.Status409Conflict }),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>A follow-up email to copy and send yourself. Nothing is sent from here.</summary>
    [HttpPost("{id:guid}/follow-up")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<FollowUpDraftResponse>> DraftFollowUp(Guid id, CancellationToken cancellationToken) =>
        await followUps.DraftAsync(UserId, id, cancellationToken) switch
        {
            FollowUpOutcome.Drafted drafted => Ok(new FollowUpDraftResponse { Subject = drafted.Draft.Subject, Body = drafted.Draft.Body }),
            FollowUpOutcome.Unavailable unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails { Title = unavailable.Message, Status = StatusCodes.Status503ServiceUnavailable }),
            FollowUpOutcome.Failed failed => StatusCode(StatusCodes.Status502BadGateway,
                new ProblemDetails { Title = failed.Message, Status = StatusCodes.Status502BadGateway }),
            _ => NotFound(),
        };

    /// <summary>Records that a follow-up went out, restarting how long the application counts as silent.</summary>
    [HttpPost("{id:guid}/followed-up")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationResponse>> MarkFollowedUp(Guid id, CancellationToken cancellationToken)
    {
        var updated = await followUps.MarkSentAsync(UserId, id, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>The tailored resume as a PDF, in the base resume's exact format.</summary>
    [HttpGet("{id:guid}/resume.pdf")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TailoredResumePdf(Guid id)
    {
        var found = await applications.GetTailoredResumeAsync(UserId, id);
        if (found is not { } tailored)
        {
            return NotFound();
        }

        return File(
            renderer.Render(tailored.Layout),
            "application/pdf",
            ResumeFileNames.ForApplication(tailored.Layout, tailored.CompanyName));
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

    /// <summary>Generates a cover letter for this application against its active resume. Saves nothing.</summary>
    [HttpPost("{id:guid}/cover-letter")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CoverLetterResponse>> GenerateCoverLetter(Guid id, CancellationToken cancellationToken)
    {
        var outcome = await coverLetters.GenerateAsync(UserId, id, cancellationToken);

        return outcome switch
        {
            CoverLetterOutcome.Success success => Ok(new CoverLetterResponse { Content = success.Content }),

            CoverLetterOutcome.Failed { Reason: CoverLetterFailureReason.ApplicationNotFound } => NotFound(),

            CoverLetterOutcome.Failed failed and
                { Reason: CoverLetterFailureReason.NoJobDescription or CoverLetterFailureReason.NoActiveResume }
                => Conflict(new ProblemDetails { Title = failed.Message, Status = StatusCodes.Status409Conflict }),

            CoverLetterOutcome.Failed failed and { Reason: CoverLetterFailureReason.GeneratorUnavailable }
                => StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
                {
                    Title = failed.Message,
                    Status = StatusCodes.Status503ServiceUnavailable,
                }),

            CoverLetterOutcome.Failed failed
                => StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Title = failed.Message,
                    Status = StatusCodes.Status502BadGateway,
                }),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>Generates interview questions and talking points for this application against its active resume. Saves nothing.</summary>
    [HttpPost("{id:guid}/interview-prep")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<InterviewPrepResponse>> GenerateInterviewPrep(
        Guid id, CancellationToken cancellationToken, [FromQuery] bool regenerate = false)
    {
        var outcome = await interviewPrep.GenerateAsync(UserId, id, regenerate, cancellationToken);

        return outcome switch
        {
            InterviewPrepOutcome.Success success => Ok(ToResponse(success.Prep)),

            InterviewPrepOutcome.Failed { Reason: InterviewPrepFailureReason.ApplicationNotFound } => NotFound(),

            InterviewPrepOutcome.Failed failed and
                { Reason: InterviewPrepFailureReason.NoJobDescription or InterviewPrepFailureReason.NoActiveResume }
                => Conflict(new ProblemDetails { Title = failed.Message, Status = StatusCodes.Status409Conflict }),

            InterviewPrepOutcome.Failed failed and { Reason: InterviewPrepFailureReason.GeneratorUnavailable }
                => StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
                {
                    Title = failed.Message,
                    Status = StatusCodes.Status503ServiceUnavailable,
                }),

            InterviewPrepOutcome.Failed failed
                => StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Title = failed.Message,
                    Status = StatusCodes.Status502BadGateway,
                }),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>The stored interview prep, if any. 204 when none has been generated — no model call either way.</summary>
    [HttpGet("{id:guid}/interview-prep")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<InterviewPrepResponse>> GetInterviewPrep(Guid id, CancellationToken cancellationToken)
    {
        var prep = await interviewPrep.GetStoredAsync(UserId, id, cancellationToken);
        return prep is null ? NoContent() : Ok(ToResponse(prep));
    }

    private static InterviewPrepResponse ToResponse(InterviewPrep prep) => new()
    {
        Questions = prep.Questions
            .Select(q => new InterviewQuestionResponse { Question = q.Question, WhyItMightComeUp = q.WhyItMightComeUp })
            .ToList(),
        TalkingPoints = prep.TalkingPoints
            .Select(t => new TalkingPointResponse { Point = t.Point, HowToUseIt = t.HowToUseIt })
            .ToList(),
    };
}
