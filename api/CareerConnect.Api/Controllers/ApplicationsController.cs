using System.Security.Claims;
using CareerConnect.Api.Contracts;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareerConnect.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/applications")]
public class ApplicationsController(IApplicationService applications) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ApplicationResponse>>> List() =>
        Ok(await applications.ListAsync(UserId));

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

    private Guid UserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? throw new InvalidOperationException("Authenticated user has no id claim."));
}
