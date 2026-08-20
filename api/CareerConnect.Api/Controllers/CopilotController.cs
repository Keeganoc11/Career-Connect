using CareerConnect.Api.Contracts;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareerConnect.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/copilot")]
public class CopilotController(ICopilotService copilot) : ApiControllerBase
{
    /// <summary>Runs a fresh analysis of the caller's whole pipeline. On-demand only — nothing is cached or persisted, so this only runs (and only costs tokens) when asked.</summary>
    [HttpPost("analyze")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CopilotInsightsResponse>> Analyze(CancellationToken cancellationToken)
    {
        var outcome = await copilot.AnalyzeAsync(UserId, cancellationToken);

        return outcome switch
        {
            CopilotOutcome.Success success => Ok(new CopilotInsightsResponse
            {
                OverallSummary = success.Insights.OverallSummary,
                Actions = success.Insights.Actions
                    .Select(a => new CopilotActionResponse
                    {
                        Title = a.Title,
                        Detail = a.Detail,
                        Priority = a.Priority,
                        ApplicationId = a.ApplicationId,
                    })
                    .ToList(),
            }),

            CopilotOutcome.Failed { Reason: CopilotFailureReason.AnalyzerUnavailable } failed
                => StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
                {
                    Title = failed.Message,
                    Status = StatusCodes.Status503ServiceUnavailable,
                }),

            CopilotOutcome.Failed failed
                => StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Title = failed.Message,
                    Status = StatusCodes.Status502BadGateway,
                }),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
