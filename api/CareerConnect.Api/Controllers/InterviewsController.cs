using CareerConnect.Api.Contracts;
using CareerConnect.Api.Domain;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareerConnect.Api.Controllers;

/// <summary>
/// Scheduled interviews. Kept off ApplicationsController, which is already
/// carrying fifteen endpoints across five different concerns.
/// </summary>
[ApiController]
[Authorize]
public class InterviewsController(
    IInterviewService interviews,
    IInterviewTrackerService tracker) : ApiControllerBase
{
    [HttpGet("api/applications/{applicationId:guid}/interviews")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<InterviewEventResponse>>> List(Guid applicationId) =>
        Ok(await interviews.ListAsync(UserId, applicationId));

    [HttpPost("api/applications/{applicationId:guid}/interviews")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InterviewEventResponse>> Create(
        Guid applicationId, CreateInterviewRequest request)
    {
        var created = await interviews.CreateAsync(UserId, applicationId, request);
        return created is null
            ? NotFound()
            : CreatedAtAction(nameof(List), new { applicationId }, created);
    }

    [HttpPut("api/interviews/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InterviewEventResponse>> Update(Guid id, UpdateInterviewRequest request)
    {
        var updated = await interviews.UpdateAsync(UserId, id, request);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>The interview as an .ics download — works with any calendar app, no Google connection needed.</summary>
    [HttpGet("api/interviews/{id:guid}.ics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id)
    {
        var ics = await interviews.BuildCalendarFileAsync(UserId, id);
        return ics is null
            ? NotFound()
            : File(System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar", $"interview-{id}.ics");
    }

    [HttpDelete("api/interviews/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id) =>
        await interviews.DeleteAsync(UserId, id) ? NoContent() : NotFound();

    // ---- Interview tracker (Pro) ----
    //
    // Scheduling an interview is part of the free tracker; preparing for one,
    // logging what was asked and getting it scored against the posting is not.

    /// <summary>Everything one interview's page shows: notes, both question lists, the debrief.</summary>
    [HttpGet("api/interviews/{id:guid}/prep")]
    [ProOnly]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InterviewTrackerResponse>> GetPrep(Guid id, CancellationToken cancellationToken)
    {
        var prep = await tracker.GetAsync(UserId, id, cancellationToken);
        return prep is null ? NotFound() : Ok(prep);
    }

    [HttpPut("api/interviews/{id:guid}/research")]
    [ProOnly]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InterviewTrackerResponse>> SaveResearch(
        Guid id, SaveResearchRequest request, CancellationToken cancellationToken)
    {
        var saved = await tracker.SaveResearchAsync(UserId, id, request, cancellationToken);
        return saved is null ? NotFound() : Ok(saved);
    }

    [HttpPut("api/interviews/{id:guid}/reflection")]
    [ProOnly]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InterviewTrackerResponse>> SaveReflection(
        Guid id, SaveReflectionRequest request, CancellationToken cancellationToken)
    {
        var saved = await tracker.SaveReflectionAsync(UserId, id, request, cancellationToken);
        return saved is null ? NotFound() : Ok(saved);
    }

    [HttpPost("api/interviews/{id:guid}/questions")]
    [ProOnly]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TrackedQuestionResponse>> AddQuestion(
        Guid id, CreateInterviewQuestionRequest request, CancellationToken cancellationToken)
    {
        var created = await tracker.AddQuestionAsync(UserId, id, request, cancellationToken);
        return created is null
            ? NotFound()
            : CreatedAtAction(nameof(GetPrep), new { id }, created);
    }

    [HttpPut("api/interview-questions/{questionId:guid}")]
    [ProOnly]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TrackedQuestionResponse>> UpdateQuestion(
        Guid questionId, UpdateInterviewQuestionRequest request, CancellationToken cancellationToken)
    {
        var updated = await tracker.UpdateQuestionAsync(UserId, questionId, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/interview-questions/{questionId:guid}")]
    [ProOnly]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteQuestion(Guid questionId, CancellationToken cancellationToken) =>
        await tracker.DeleteQuestionAsync(UserId, questionId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Adds likely questions for this round, and sharp ones to ask them.</summary>
    [HttpPost("api/interviews/{id:guid}/questions/suggest")]
    [ProOnly]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<List<TrackedQuestionResponse>>> SuggestQuestions(
        Guid id, CancellationToken cancellationToken) =>
        Resolve(await tracker.SuggestQuestionsAsync(UserId, id, cancellationToken));

    /// <summary>The honest read on how the round went, scored against the job description.</summary>
    [HttpPost("api/interviews/{id:guid}/debrief")]
    [ProOnly]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<InterviewDebrief>> Debrief(Guid id, CancellationToken cancellationToken) =>
        Resolve(await tracker.GenerateDebriefAsync(UserId, id, cancellationToken));

    /// <summary>Every question you've been asked, across every interview.</summary>
    [HttpGet("api/interview-questions/bank")]
    [ProOnly]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<QuestionBankResponse>> QuestionBank(CancellationToken cancellationToken) =>
        Ok(await tracker.GetQuestionBankAsync(UserId, cancellationToken));

    /// <summary>
    /// One mapping from outcome to status code for both model-backed endpoints:
    /// a missing precondition is 409 (the request was fine, the data isn't
    /// ready), no API key is 503, and a failed call is 502.
    /// </summary>
    private ActionResult<T> Resolve<T>(InterviewPrepOutcome<T> outcome) => outcome switch
    {
        InterviewPrepOutcome<T>.Success success => Ok(success.Value),
        InterviewPrepOutcome<T>.NotFound => NotFound(),
        InterviewPrepOutcome<T>.NotReady notReady
            => Conflict(new ProblemDetails { Title = notReady.Message, Status = StatusCodes.Status409Conflict }),
        InterviewPrepOutcome<T>.Unavailable unavailable
            => StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails { Title = unavailable.Message, Status = StatusCodes.Status503ServiceUnavailable }),
        InterviewPrepOutcome<T>.Failed failed
            => StatusCode(StatusCodes.Status502BadGateway,
                new ProblemDetails { Title = failed.Message, Status = StatusCodes.Status502BadGateway }),
        _ => StatusCode(StatusCodes.Status500InternalServerError),
    };
}
