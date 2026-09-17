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
    IGmailPendingUpdates pendingUpdates,
    IApplicationService applications,
    IInterviewService interviews,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration) : ApiControllerBase
{
    // The URI Google redirects back to after consent — must exactly match one
    // registered on the OAuth client in Google Cloud Console. Required in
    // every environment (dev value lives in appsettings.Development.json).
    private readonly string? _redirectUri = configuration["Gmail:RedirectUri"];

    // Where to send the browser after the callback completes. Left unset in
    // production on purpose: the client is served from this same app there
    // (see Program.cs), so a relative redirect already lands in the right
    // place. Only local dev needs this, since the Vite dev server runs on a
    // different origin (port 5173) than the API (port 5199).
    private readonly string _clientOrigin = configuration["App:ClientOrigin"] ?? "";

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
    [ProOnly]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<GmailAuthorizationUrlResponse> Connect()
    {
        if (!oauth.IsConfigured || string.IsNullOrWhiteSpace(_redirectUri))
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
        var url = oauth.BuildAuthorizationUrl(_redirectUri, state);
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

        if (string.IsNullOrWhiteSpace(_redirectUri))
        {
            return RedirectToClient(success: false, "Gmail integration is not configured.");
        }

        try
        {
            await oauth.ConnectAsync(userId, code, _redirectUri);
        }
        catch (Exception ex)
        {
            return RedirectToClient(success: false, ex.Message);
        }

        return RedirectToClient(success: true);
    }

    private RedirectResult RedirectToClient(bool success, string? message = null) =>
        Redirect(success
            ? $"{_clientOrigin}/?gmail=connected"
            : $"{_clientOrigin}/?gmail=error&message={Uri.EscapeDataString(message ?? "Something went wrong.")}");

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
                HasPendingSuggestions = connection.HasPendingSuggestions,
                CalendarEnabled = connection.CalendarEnabled,
            });
    }

    /// <summary>
    /// Everything scans have found that's still waiting for review. Reading
    /// doesn't clear it — an update stays until it's accepted or dismissed.
    /// </summary>
    [HttpGet("pending-suggestions")]
    [ProOnly]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<GmailScanResponse>> PendingSuggestions(CancellationToken cancellationToken)
    {
        var result = await pendingUpdates.ReadAsync(UserId, cancellationToken);
        return result is null ? NoContent() : Ok(result);
    }

    /// <summary>Hides a suggested status change without applying it.</summary>
    [HttpPost("pending-suggestions/status-updates/dismiss")]
    [ProOnly]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DismissStatusUpdate(
        DismissStatusUpdateRequest request, CancellationToken cancellationToken)
    {
        await pendingUpdates.RemoveStatusUpdateAsync(
            UserId, request.ApplicationId, request.SuggestedStatus, cancellationToken);
        return NoContent();
    }

    /// <summary>Hides a suggested new application without adding it.</summary>
    [HttpPost("pending-suggestions/new-applications/dismiss")]
    [ProOnly]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DismissNewApplication(
        DismissNewApplicationRequest request, CancellationToken cancellationToken)
    {
        await pendingUpdates.RemoveNewApplicationAsync(UserId, request.CompanyName, cancellationToken);
        return NoContent();
    }

    /// <summary>Applies a status change the user accepted from a scan, recording that email is where it came from.</summary>
    [HttpPost("suggestions/accept")]
    [ProOnly]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationResponse>> AcceptSuggestion(AcceptSuggestionRequest request)
    {
        var updated = await applications.UpdateStatusAsync(
            UserId, request.ApplicationId, request.Status, Domain.ChangeSource.EmailSuggestion);

        if (updated is null)
        {
            return NotFound();
        }

        await pendingUpdates.RemoveStatusUpdateAsync(UserId, request.ApplicationId, request.Status);

        // One review, both outcomes: the user confirmed what the email means,
        // so the time it named goes on the calendar in the same action.
        if (request.InterviewAtUtc is { } interviewAt)
        {
            await interviews.RecordFromEmailAsync(
                UserId,
                request.ApplicationId,
                interviewAt,
                request.InterviewKind ?? Domain.InterviewKind.Other,
                notes: null);

            // Re-read so the response carries the interview just scheduled.
            updated = await applications.GetAsync(UserId, request.ApplicationId) ?? updated;
        }

        return Ok(updated);
    }

    [HttpDelete("connection")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disconnect()
    {
        await oauth.DisconnectAsync(UserId);
        return NoContent();
    }

    /// <summary>
    /// Runs a scan and returns everything waiting for review — this scan's
    /// findings plus any earlier ones still unhandled. Applies nothing itself
    /// beyond the one automatic Preparing → Applied confirmation.
    /// </summary>
    [HttpPost("scan")]
    [ProOnly]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GmailScanResponse>> Scan(CancellationToken cancellationToken)
    {
        var outcome = await scanner.ScanAsync(UserId, cancellationToken);

        if (outcome is GmailScanOutcome.Failed failed)
        {
            return Conflict(new ProblemDetails
            {
                Title = failed.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }

        if (outcome is not GmailScanOutcome.Success success)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        // Findings go where a scheduled scan's do, so closing the window before
        // handling them doesn't lose them: the watermark has already moved past
        // these emails, and no later scan will find them again.
        await pendingUpdates.AddAsync(UserId, new GmailScanResponse
        {
            StatusUpdates = success.StatusUpdates,
            NewApplications = success.NewApplications,
            AutoApplied = success.AutoApplied,
        }, cancellationToken);

        var waiting = await pendingUpdates.ReadAsync(UserId, cancellationToken);
        return Ok(waiting ?? new GmailScanResponse { StatusUpdates = [], NewApplications = [], AutoApplied = [] });
    }
}
