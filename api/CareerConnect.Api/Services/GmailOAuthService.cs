using CareerConnect.Api.Data;
using CareerConnect.Api.Domain;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CareerConnect.Api.Services;

public class GmailOAuthService : IGmailOAuthService
{
    // Data Protection purposes are per-payload-kind by convention — this one
    // is scoped to exactly this secret so it can never be cross-used with,
    // say, the OAuth state protector in the controller.
    private const string RefreshTokenProtectionPurpose = "CareerConnect.GmailRefreshToken.v1";
    private static readonly string[] Scopes = [GmailService.Scope.GmailReadonly];

    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly ILogger<GmailOAuthService> _logger;

    public GmailOAuthService(
        AppDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        ILogger<GmailOAuthService> logger)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector(RefreshTokenProtectionPurpose);
        _logger = logger;
        _clientId = configuration["Gmail:ClientId"];
        _clientSecret = configuration["Gmail:ClientSecret"];

        if (!IsConfigured)
        {
            _logger.LogWarning(
                "No Gmail OAuth client configured — email status detection is disabled. " +
                "Set Gmail:ClientId / Gmail:ClientSecret user secrets to enable it.");
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_clientSecret);

    private GoogleAuthorizationCodeFlow CreateFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret },
        Scopes = Scopes,
    });

    public string BuildAuthorizationUrl(string redirectUri, string state)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Gmail OAuth is not configured.");
        }

        using var flow = CreateFlow();
        // The runtime type is GoogleAuthorizationCodeRequestUrl (that's what
        // GoogleAuthorizationCodeFlow actually builds), but the compile-time
        // return type of CreateAuthorizationCodeRequest is the plain base
        // class, so the Google-specific properties need this cast to reach.
        var request = (GoogleAuthorizationCodeRequestUrl)flow.CreateAuthorizationCodeRequest(redirectUri);
        request.State = state;
        // AccessType already defaults to "offline" (that's what gets us a
        // refresh token) — set it explicitly anyway so that stays true even
        // if the SDK's default ever changes. Prompt=consent forces Google to
        // re-issue a refresh token on a reconnect, not just on first consent.
        request.AccessType = "offline";
        request.Prompt = "consent";
        return request.Build().ToString();
    }

    public async Task<GmailConnectionInfo> ConnectAsync(
        Guid userId, string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Gmail OAuth is not configured.");
        }

        using var flow = CreateFlow();
        TokenResponse token;
        try
        {
            token = await flow.ExchangeCodeForTokenAsync(userId.ToString(), code, redirectUri, cancellationToken);
        }
        catch (TokenResponseException ex)
        {
            throw new InvalidOperationException($"Google rejected the connection request: {ex.Error.ErrorDescription}", ex);
        }

        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            throw new InvalidOperationException(
                "Google didn't return a refresh token — this can happen on a reconnect. Remove Career " +
                "Connect at myaccount.google.com/permissions and try connecting again.");
        }

        var credential = new UserCredential(flow, userId.ToString(), token);
        using var gmailService = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Career Connect",
        });
        var profile = await gmailService.Users.GetProfile("me").ExecuteAsync(cancellationToken);
        var email = profile.EmailAddress ?? "unknown";

        var utcNow = DateTime.UtcNow;
        var connection = await _db.GmailConnections.FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);
        if (connection is null)
        {
            connection = new GmailConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ConnectedEmail = email,
                EncryptedRefreshToken = _protector.Protect(token.RefreshToken),
                ConnectedAtUtc = utcNow,
            };
            _db.GmailConnections.Add(connection);
        }
        else
        {
            connection.ConnectedEmail = email;
            connection.EncryptedRefreshToken = _protector.Protect(token.RefreshToken);
            connection.ConnectedAtUtc = utcNow;
            // Reset the watermark — reconnecting is a good moment to re-scan
            // recent mail in case anything was missed while disconnected.
            connection.LastCheckedAtUtc = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new GmailConnectionInfo(connection.ConnectedEmail, connection.ConnectedAtUtc, connection.LastCheckedAtUtc);
    }

    public async Task<GmailConnectionInfo?> GetConnectionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var connection = await _db.GmailConnections.AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);
        return connection is null
            ? null
            : new GmailConnectionInfo(connection.ConnectedEmail, connection.ConnectedAtUtc, connection.LastCheckedAtUtc);
    }

    public async Task DisconnectAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var connection = await _db.GmailConnections.FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);
        if (connection is null)
        {
            return;
        }

        _db.GmailConnections.Remove(connection);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCheckedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var connection = await _db.GmailConnections.FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);
        if (connection is null)
        {
            return;
        }

        connection.LastCheckedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GmailService?> GetGmailServiceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var connection = await _db.GmailConnections.AsNoTracking()
            .FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);
        if (connection is null)
        {
            return null;
        }

        string refreshToken;
        try
        {
            refreshToken = _protector.Unprotect(connection.EncryptedRefreshToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not decrypt the stored Gmail refresh token for user {UserId}.", userId);
            return null;
        }

        var flow = CreateFlow();
        var credential = new UserCredential(flow, userId.ToString(), new TokenResponse { RefreshToken = refreshToken });

        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Career Connect",
        });
    }
}
