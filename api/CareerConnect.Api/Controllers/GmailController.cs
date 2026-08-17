using CareerConnect.Api.Contracts;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace CareerConnect.Api.Controllers;

[ApiController]
[Route("api/gmail")]
public class GmailController(
    IGmailOAuthService oauth,
    IGmailUpdateScanner scanner,
    IDataProtectionProvider dataProtectionProvider) : ApiControllerBase
{
    // Local-only redirect targets — this app has no hosted deployment, same
    // assumption the client CORS policy in Program.cs already makes.
    private const string RedirectUri = "http://localhost:5199/api/gmail/callback";
    private const string ClientOrigin = "http://localhost:5173";

    // A separate purpose from the refresh-token protector: Data Protection
    // purposes should be per-payload-kind, never shared across secret types.
    private readonly IDataProtector _stateProtector =
        dataProtectionProvider.CreateProtector("CareerConnect.GmailOAuthState.v1");

    /// <summary>
    /// Returns the Google consent URL for the client to navigate to. A plain
    /// redirect can't carry the Authorization header, so this is a normal
    /// authenticated JSON call — the client does the actual navigation.
    /// </summary>
    [HttpGet("connect")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<GmailAuthorizationUrlResponse> Connect()
    {
        if (!oauth.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "Gmail integration needs Google OAuth credentials. See the README for setup.",
                Status = StatusCodes.Status503ServiceUnavailable,
            });
        }

        // Google's redirect back has no way to carry our JWT, so the user id
        // rides along encrypted in `state` — the callback below recovers it.
        var state = _stateProtector.Protect(UserId.ToString());
        var url = oauth.BuildAuthorizationUrl(RedirectUri, state);
        return Ok(new GmailAuthorizationUrlResponse { AuthorizationUrl = url });
    }

    /// <summary>Google redirects the browser here after consent — necessarily unauthenticated.</summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            return RedirectToClient(success: false, error);
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return RedirectToClient(success: false, "Missing authorization code.");
        }

        Guid userId;
        try
        {
            userId = Guid.Parse(_stateProtector.Unprotect(state));
        }
        catch
        {
            return RedirectToClient(success: false, "Invalid or expired connection request. Try connecting again.");
        }

        try
        {
            await oauth.ConnectAsync(userId, code, RedirectUri);
        }
        catch (Exception ex)
        {
            return RedirectToClient(success: false, ex.Message);
        }

        return RedirectToClient(success: true);
    }

    private RedirectResult RedirectToClient(bool success, string? message = null) =>
        Redirect(success
            ? $"{ClientOrigin}/?gmail=connected"
            : $"{ClientOrigin}/?gmail=error&message={Uri.EscapeDataString(message ?? "Something went wrong.")}");

    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<GmailConnectionResponse>> Status()
    {
        var connection = await oauth.GetConnectionAsync(UserId);
        return Ok(connection is null
            ? new GmailConnectionResponse { Connected = false }
            : new GmailConnectionResponse
            {
                Connected = true,
                ConnectedEmail = connection.ConnectedEmail,
                ConnectedAtUtc = connection.ConnectedAtUtc,
                LastCheckedAtUtc = connection.LastCheckedAtUtc,
            });
    }

    [HttpDelete("connection")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disconnect()
    {
        await oauth.DisconnectAsync(UserId);
        return NoContent();
    }

    /// <summary>Runs a scan and returns suggested status changes and new applications. Applies nothing itself.</summary>
    [HttpPost("scan")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GmailScanResponse>> Scan(CancellationToken cancellationToken)
    {
        var outcome = await scanner.ScanAsync(UserId, cancellationToken);

        return outcome switch
        {
            GmailScanOutcome.Success success => Ok(new GmailScanResponse
            {
                StatusUpdates = success.StatusUpdates.Select(ToResponse).ToList(),
                NewApplications = success.NewApplications.Select(ToResponse).ToList(),
            }),
            GmailScanOutcome.Failed failed => Conflict(new ProblemDetails
            {
                Title = failed.Message,
                Status = StatusCodes.Status409Conflict,
            }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static SuggestedStatusUpdateResponse ToResponse(SuggestedStatusUpdate s) => new()
    {
        ApplicationId = s.ApplicationId,
        CompanyName = s.CompanyName,
        RoleTitle = s.RoleTitle,
        CurrentStatus = s.CurrentStatus,
        SuggestedStatus = s.SuggestedStatus,
        Reasoning = s.Reasoning,
        EmailSubject = s.EmailSubject,
        EmailFrom = s.EmailFrom,
        EmailReceivedAtUtc = s.EmailReceivedAtUtc,
    };

    private static SuggestedNewApplicationResponse ToResponse(SuggestedNewApplication n) => new()
    {
        CompanyName = n.CompanyName,
        RoleTitle = n.RoleTitle,
        Reasoning = n.Reasoning,
        EmailSubject = n.EmailSubject,
        EmailFrom = n.EmailFrom,
        EmailReceivedAtUtc = n.EmailReceivedAtUtc,
    };
}
