using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CareerConnect.Api.Data;
using CareerConnect.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    // Enums travel as their names ("PhoneScreen"), matching how they're stored.
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(PostgresConnectionString.Resolve(builder.Configuration)));

builder.Services.AddScoped<IPlanService, PlanService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();
builder.Services.AddScoped<IResumeService, ResumeService>();
builder.Services.AddSingleton<IResumeFileTextExtractor, ResumeFileTextExtractor>();
builder.Services.AddScoped<IMatchScoringService, MatchScoringService>();
builder.Services.AddSingleton<IResumeMatchAnalyzer, ClaudeResumeMatchAnalyzer>();
builder.Services.AddSingleton<ITokenService, TokenService>();

builder.Services.AddSingleton<IJobPostingFetcher, JobPostingFetcher>();
builder.Services.AddSingleton<IJobPostingExtractor, ClaudeJobPostingExtractor>();
builder.Services.AddScoped<IJobPostingIngestService, JobPostingIngestService>();


builder.Services.AddSingleton<ICoverLetterGenerator, ClaudeCoverLetterGenerator>();
builder.Services.AddScoped<ICoverLetterService, CoverLetterService>();

builder.Services.AddSingleton<IInterviewPrepGenerator, ClaudeInterviewPrepGenerator>();
builder.Services.AddScoped<IInterviewPrepService, InterviewPrepService>();

builder.Services.AddSingleton<ICopilotAnalyzer, ClaudeCopilotAnalyzer>();
builder.Services.AddScoped<ICopilotService, CopilotService>();

// A prep pass chains several model calls, far past any sane HTTP timeout, so
// the request only records the run and the background worker executes it.
builder.Services.AddSingleton<IPrepRunQueue, PrepRunQueue>();
builder.Services.AddSingleton<IResumeLayoutReader, ResumeLayoutReader>();
builder.Services.AddSingleton<IResumeRenderer, ResumeRenderer>();
builder.Services.AddSingleton<ClaudeStructuredCaller>();
builder.Services.AddSingleton<IResumeLayoutTailorer, ClaudeResumeLayoutTailorer>();
builder.Services.AddSingleton<IResumeClaimsAuditor, ClaudeResumeClaimsAuditor>();
builder.Services.AddSingleton<IResumeReviewer, ClaudeResumeReviewer>();
builder.Services.AddScoped<IResumeEditGuard, ResumeEditGuard>();
builder.Services.AddSingleton<IJobPostingIdentifier, ClaudeJobPostingIdentifier>();
builder.Services.AddScoped<IJobCaptureService, JobCaptureService>();
builder.Services.AddScoped<IPrepRunService, PrepRunService>();
builder.Services.AddScoped<IApplicationPrepRunner, ApplicationPrepRunner>();
builder.Services.AddHostedService<PrepRunBackgroundService>();

builder.Services.AddScoped<IInterviewService, InterviewService>();
builder.Services.AddSingleton<IInterviewQuestionSuggester, ClaudeInterviewQuestionSuggester>();
builder.Services.AddSingleton<IInterviewDebriefWriter, ClaudeInterviewDebriefWriter>();
builder.Services.AddScoped<IInterviewTrackerService, InterviewTrackerService>();
builder.Services.AddScoped<IInterviewCalendarSync, GoogleInterviewCalendarSync>();
builder.Services.AddScoped<IAgendaService, AgendaService>();
builder.Services.AddScoped<IApplicationAutomation, ApplicationAutomation>();
builder.Services.AddSingleton<IFollowUpWriter, ClaudeFollowUpWriter>();
builder.Services.AddScoped<IFollowUpService, FollowUpService>();
builder.Services.AddHostedService<AutomationBackgroundService>();

// Encrypts the stored Gmail refresh token (see GmailOAuthService). Without a
// persisted key ring, a container redeploy generates a new one and silently
// strands every previously-stored token — set DataProtection:KeysPath to a
// path on a mounted, persistent volume in any deployed environment.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("CareerConnect");
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.AddScoped<IGmailOAuthService, GmailOAuthService>();
builder.Services.AddScoped<IGmailMailReader, GmailMailReader>();
builder.Services.AddScoped<IGmailUpdateScanner, GmailUpdateScanner>();
builder.Services.AddScoped<IGmailPendingUpdates, GmailPendingUpdates>();
builder.Services.AddSingleton<IEmailStatusClassifier, ClaudeEmailStatusClassifier>();
builder.Services.AddSingleton<IInterviewDetailsExtractor, ClaudeInterviewDetailsExtractor>();

builder.Services.AddScoped<IScheduledGmailScanRunner, ScheduledGmailScanRunner>();
builder.Services.AddHostedService<GmailBackgroundScanService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]
                    ?? throw new InvalidOperationException("Jwt:Key is not configured.")))
        };
    });
builder.Services.AddAuthorization();

// Registration/login are the one surface an anonymous caller can hit
// repeatedly — cap it per IP so credential stuffing or signup spam can't
// hammer the password hasher or fill the Users table.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

// In production the client is served by this same app (see UseStaticFiles /
// MapFallbackToFile below), so there's no cross-origin call to allow. CORS is
// only needed in local dev, where the Vite dev server runs on its own origin
// — set via Cors:AllowedOrigins in appsettings.Development.json.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("client", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Career Connect API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// No-op locally (nothing is published to wwwroot in dev — the client runs
// separately under Vite). In production the Dockerfile publishes the built
// client into wwwroot, and this is what serves it.
// index.html must be revalidated on every load, or a browser keeps running the
// previous deploy's client for hours after a release. Everything under /assets
// has a content hash in its name, so a changed file is a new URL and those can
// be cached forever.
var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl =
            context.Context.Request.Path.StartsWithSegments("/assets")
                ? "public, max-age=31536000, immutable"
                : "no-cache";
    },
};

app.UseDefaultFiles();
app.UseStaticFiles(staticFiles);

if (allowedOrigins.Length > 0)
{
    app.UseCors("client");
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html", staticFiles);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DbSeeder.SeedAsync(db, app.Configuration, logger);
}

app.Run();
