using CareerConnect.Api.Contracts;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareerConnect.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/activity")]
public class ActivityController(IApplicationAutomation automation) : ApiControllerBase
{
    /// <summary>Changes the app made on its own recently, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ActivityResponse>>> List([FromQuery] int days = 14, CancellationToken cancellationToken = default) =>
        Ok(await automation.ListAsync(UserId, days, cancellationToken));

    /// <summary>Puts an automatic change back: the old status, the old date, and no interview it added.</summary>
    [HttpPost("{id:guid}/undo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ActivityResponse>> Undo(Guid id, CancellationToken cancellationToken) =>
        await automation.UndoAsync(UserId, id, cancellationToken) switch
        {
            UndoOutcome.Undone undone => Ok(undone.Activity),
            UndoOutcome.Conflict conflict => Conflict(new ProblemDetails { Title = conflict.Message, Status = StatusCodes.Status409Conflict }),
            _ => NotFound(),
        };
}
