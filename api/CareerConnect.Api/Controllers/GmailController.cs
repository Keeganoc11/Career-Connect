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
            });
    }

    /// <summary>Returns whatever the last scheduled background scan found, then clears it — single-consumption, like a notification you've now seen.</summary>
    [HttpGet("pending-suggestions")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<GmailScanResponse>> PendingSuggestions()
    {
        var result = await oauth.GetAndClearPendingSuggestionsAsync(UserId);
        return result is null ? NoContent() : Ok(result);
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
                StatusUpdates = success.StatusUpdates,
                NewApplications = success.NewApplications,
            }),
            GmailScanOutcome.Failed failed => Conflict(new ProblemDetails
            {
                Title = failed.Message,
                Status = StatusCodes.Status409Conflict,
            }),
            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
