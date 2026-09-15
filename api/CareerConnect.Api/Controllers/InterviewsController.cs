using CareerConnect.Api.Contracts;
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
public class InterviewsController(IInterviewService interviews) : ApiControllerBase
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
}
