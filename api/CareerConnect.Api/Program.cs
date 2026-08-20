using System.Text;
using System.Text.Json.Serialization;
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

builder.Services.AddSingleton<IResumeTailorer, ClaudeResumeTailorer>();
builder.Services.AddScoped<IResumeTailorService, ResumeTailorService>();

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
builder.Services.AddSingleton<IEmailStatusClassifier, ClaudeEmailStatusClassifier>();

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
app.UseDefaultFiles();
app.UseStaticFiles();

if (allowedOrigins.Length > 0)
{
    app.UseCors("client");
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DbSeeder.SeedAsync(db, app.Configuration, logger);
}

app.Run();
