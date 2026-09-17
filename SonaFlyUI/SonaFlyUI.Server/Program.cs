using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SonaFlyUI.Server.Api.Hubs;
using SonaFlyUI.Server.Api.Middleware;
using SonaFlyUI.Server.Application.Identification;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.BackgroundServices;
using SonaFlyUI.Server.Infrastructure.Configuration;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.HealthChecks;
using SonaFlyUI.Server.Infrastructure.Identity;
using SonaFlyUI.Server.Infrastructure.Identification;
using SonaFlyUI.Server.Infrastructure.Services;
using static SonaFlyUI.Server.Api.Controllers.AuthController;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──
builder.Services.AddDbContext<SonaFlyDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Identity ──
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;

    // Throttle online password guessing. Login passes lockoutOnFailure: true.
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<SonaFlyDbContext>()
.AddDefaultTokenProviders();

// ── JWT ──
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.Configure<JwtSettings>(jwtSection);
var jwtSettings = jwtSection.Get<JwtSettings>()!;

// Validate JWT secret.
// Any value that ships with the repository or a template is public knowledge and lets
// anyone mint an admin token, so length alone is not a sufficient test.
string[] wellKnownSecrets =
[
    "SonaFly-Dev-Secret-Key-Must-Be-At-Least-32-Chars!",
    "192837quwyeyrtfg192837quwyeyrtfg",                            // shipped in appsettings.json
    "CHANGE-ME-generate-a-random-secret-at-least-32-characters",   // docker/.env.example
];
if (wellKnownSecrets.Contains(jwtSettings.Secret, StringComparer.Ordinal))
{
    if (builder.Environment.IsProduction())
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("\n" + new string('!', 70));
        Console.WriteLine("  FATAL: You are using a publicly known JWT secret in Production!");
        Console.WriteLine("  This value ships with SonaFly, so anyone can forge tokens for this server.");
        Console.WriteLine("  Set the Jwt__Secret environment variable to a unique random string.");
        Console.WriteLine("  Generate one with: openssl rand -base64 48");
        Console.WriteLine(new string('!', 70) + "\n");
        Console.ResetColor();
        throw new InvalidOperationException(
            "Cannot start in Production with a publicly known JWT secret. Set Jwt__Secret to a unique random value.");
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n⚠ WARNING: Using default JWT dev secret. Do NOT use this in production.");
        Console.ResetColor();
    }
}
else if (jwtSettings.Secret.Length < 32)
{
    throw new InvalidOperationException("JWT secret must be at least 32 characters long.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
    options.Events = new JwtBearerEvents
    {
        // Allow SignalR to receive JWT via query string
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },

        // A signed, unexpired token is not sufficient. Re-check the account on every
        // request so disabling, deleting, demoting or resetting a user takes effect at
        // once instead of when the token happens to expire.
        OnTokenValidated = async context =>
        {
            var security = context.HttpContext.RequestServices
                .GetRequiredService<IUserSecurityService>();

            var user = await security.ResolveValidUserAsync(
                context.Principal!, context.HttpContext.RequestAborted);

            if (user is null)
            {
                context.Fail("The account is no longer valid for this token.");
            }
        }
    };
});

builder.Services.AddAuthorization();

// ── Deployment / transport ──
var deployment = builder.Configuration.GetSection(DeploymentOptions.SectionName).Get<DeploymentOptions>()
                 ?? new DeploymentOptions();
builder.Services.Configure<DeploymentOptions>(builder.Configuration.GetSection(DeploymentOptions.SectionName));

// ── Music identification (upgrade plan, section 15.1) ──
// Disabled by default: with no configuration the scan and playback paths
// behave exactly as before. Secrets stay in env vars / user secrets.
builder.Services.AddIdentificationOptions(builder.Configuration);

if (deployment.HasTrustedProxy)
{
    // Throws on a malformed address or network, so a typo fails startup rather than
    // silently leaving forwarded headers untrusted.
    var forwarded = ForwardedHeadersConfiguration.Build(deployment);
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = forwarded.ForwardedHeaders;
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();
        foreach (var proxy in forwarded.KnownProxies) options.KnownProxies.Add(proxy);
        foreach (var network in forwarded.KnownNetworks) options.KnownNetworks.Add(network);
    });
}

if (deployment.UseHttps && deployment.EnableHsts)
{
    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromDays(deployment.HstsMaxAgeDays);
        options.IncludeSubDomains = false;
        // Not preloaded: preloading is effectively irreversible and this is self-hosted.
        options.Preload = false;
    });
}

// ── Rate limiting ──
// Lockout alone only protects a single account; this also caps credential-stuffing
// and refresh-token probing from one address.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthRateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ── DI ──
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IUserSecurityService, UserSecurityService>();

// Library & Scanning
builder.Services.AddScoped<ILibraryRootService, LibraryRootService>();
builder.Services.AddScoped<IFileScanner, FileScanner>();
builder.Services.AddScoped<IMetadataReader, MetadataReader>();
builder.Services.AddScoped<IFileHashService, FileHashService>();
builder.Services.AddScoped<IServerSettingsService, ServerSettingsService>();
builder.Services.AddHttpClient<OnlineArtworkService>();
builder.Services.AddScoped<IArtworkService, ArtworkService>();
builder.Services.AddScoped<ILibraryIndexService, LibraryIndexService>();
builder.Services.AddSingleton<IScanQueue, ScanQueue>();
// Serializes scans against destructive maintenance (backlog N16).
builder.Services.AddSingleton<LibraryMaintenanceGate>();
builder.Services.AddHostedService<LibraryScanBackgroundService>();

// Music identification worker (upgrade plan 6, 7, and 12). Idle while disabled.
builder.Services.AddSingleton<AcoustIdThrottle>();
builder.Services.AddSingleton<MusicBrainzThrottle>();
builder.Services.AddSingleton<ITagEvidenceReader, TagEvidenceReader>();
builder.Services.AddSingleton<IFingerprintTool, FpcalcFingerprintTool>();
builder.Services.AddHttpClient<IAcoustIdClient, AcoustIdClient>((sp, http) =>
    http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<IdentificationOptions>>().Value.RequestTimeoutSeconds));
builder.Services.AddHttpClient<IMusicBrainzClient, MusicBrainzClient>((sp, http) =>
    http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<IdentificationOptions>>().Value.RequestTimeoutSeconds));
builder.Services.AddScoped<IdentificationItemProcessor>();
builder.Services.AddSingleton<IdentificationJobRunner>();
builder.Services.AddHostedService<IdentificationBackgroundService>();

// Streaming & Playlists
builder.Services.AddScoped<IStreamingService, StreamingService>();

// Stream tickets are signed with this key ring. It must outlive the container and be
// shared by every replica, or outstanding stream URLs break on restart.
builder.Services.AddSonaFlyDataProtection(DataProtectionSetup.ResolveKeyRingPath(builder.Configuration));

builder.Services.AddSingleton<StreamTicketService>();
builder.Services.AddScoped<IPlaylistService, PlaylistService>();
builder.Services.AddScoped<IMixedTapeService, MixedTapeService>();

// Auditorium
builder.Services.AddSingleton<AuditoriumStateService>();
builder.Services.AddSingleton<TrackEndSchedulerService>();
builder.Services.AddSignalR(options =>
{
    // Re-checks the account on every hub invocation; see AccountStatusHubFilter.
    options.AddFilter<AccountStatusHubFilter>();
});
builder.Services.AddSingleton<AccountStatusHubFilter>();

// ── Health checks ──
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

// ── Controllers + OpenAPI ──
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ── CORS (dev) ──
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        policy.AllowAnyMethod().AllowAnyHeader().AllowCredentials()
              .SetIsOriginAllowed(_ => true);
    });
});

var app = builder.Build();

// ── Seed Data ──
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<SonaFlyDbContext>();
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migration applied successfully.");

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        await IdentitySeeder.SeedRolesAsync(roleManager);
        logger.LogInformation("Roles seeded.");

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await IdentitySeeder.SeedAdminAsync(
            userManager,
            app.Configuration["SonaFly:AdminDefaultPassword"],
            app.Environment.IsProduction(),
            logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during database seeding.");
        throw; // Fail fast so the issue is visible
    }
}

// ── Middleware Pipeline ──

// Must be first: everything downstream that looks at the scheme or the client address —
// HTTPS redirection, HSTS, the Secure flag on the refresh cookie, rate-limit
// partitioning — needs the real values rather than the proxy's.
if (deployment.HasTrustedProxy)
{
    app.UseForwardedHeaders();
}
else if (app.Environment.IsProduction())
{
    app.Logger.LogWarning(
        "No trusted reverse proxy is configured ({Section}:KnownProxies / KnownNetworks). " +
        "If this instance is behind a proxy, the client address and scheme it sees are the " +
        "proxy's, so HTTPS detection and per-address rate limiting will be wrong.",
        DeploymentOptions.SectionName);
}

if (deployment.UseHttps)
{
    if (deployment.EnableHsts && !app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    // The health endpoint is exempt. The container probe reaches the app directly over
    // plain HTTP on the internal port, where there is no TLS listener to redirect it to —
    // and curl treats a 3xx as success, so redirecting would make "healthy" mean "answered
    // a redirect" rather than "the database is reachable". Everything else redirects.
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments(HealthEndpointPath),
        branch => branch.UseHttpsRedirection());
}
else if (app.Environment.IsProduction())
{
    app.Logger.LogWarning(
        "Serving Production over plain HTTP. Passwords, bearer tokens, refresh cookies and " +
        "stream tickets are readable by anyone on the network path. Put a TLS reverse proxy " +
        "in front and set {Section}:UseHttps=true. See docker/docker-compose.tls.yml.",
        DeploymentOptions.SectionName);
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseDefaultFiles();
app.MapStaticAssets();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("DevCors");
}

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Must run after authorization so the claim is available.
app.UseMiddleware<PasswordChangeRequiredMiddleware>();

// Replaces the old controller action, which reported Healthy whenever the process was
// alive. Anonymous by design: the container health probe has no credentials.
app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
        });
    }
});

app.MapControllers();
app.MapHub<AuditoriumHub>("/hubs/auditorium");

// The single-page-app fallback must not swallow API routes. Without these, a GET to a
// POST-only endpoint — which is what an HTTP client does after following a 301 from a
// plain-HTTP URL, because 301 turns POST into GET — matched no controller, fell through
// to index.html, and came back as 200 with a page of HTML. A native client then tried to
// parse that as JSON and reported "ExpectedStartOfValueNotFound, <", which says nothing
// about what actually went wrong. An API path that matches nothing is a 404.
app.MapFallback("/api/{**path}", () => Results.NotFound(new { detail = "No such API endpoint." }));
app.MapFallback("/hubs/{**path}", () => Results.NotFound(new { detail = "No such hub endpoint." }));

app.MapFallbackToFile("/index.html");

app.Run();

/// <summary>
/// Where the container probe looks. Named because two places have to agree on it: the
/// endpoint itself, and the HTTPS redirection that must not stand in front of it.
/// </summary>
public partial class Program
{
    internal const string HealthEndpointPath = "/api/health";
}
