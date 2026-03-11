using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SonaFlyUI.Server.Api.Hubs;
using SonaFlyUI.Server.Api.Middleware;
using SonaFlyUI.Server.Application.Interfaces;
using SonaFlyUI.Server.Domain.Entities;
using SonaFlyUI.Server.Infrastructure.BackgroundServices;
using SonaFlyUI.Server.Infrastructure.Data;
using SonaFlyUI.Server.Infrastructure.Identity;
using SonaFlyUI.Server.Infrastructure.Services;

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
})
.AddEntityFrameworkStores<SonaFlyDbContext>()
.AddDefaultTokenProviders();

// ── JWT ──
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.Configure<JwtSettings>(jwtSection);
var jwtSettings = jwtSection.Get<JwtSettings>()!;

// Validate JWT secret
var devSecret = "SonaFly-Dev-Secret-Key-Must-Be-At-Least-32-Chars!";
if (jwtSettings.Secret == devSecret)
{
    if (builder.Environment.IsProduction())
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("\n" + new string('!', 70));
        Console.WriteLine("  FATAL: You are using the default JWT secret in Production!");
        Console.WriteLine("  Set the Jwt__Secret environment variable to a unique random string.");
        Console.WriteLine("  Generate one with: openssl rand -base64 48");
        Console.WriteLine(new string('!', 70) + "\n");
        Console.ResetColor();
        throw new InvalidOperationException("Cannot start in Production with the default JWT secret. Set Jwt__Secret.");
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
    // Allow SignalR to receive JWT via query string
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ── DI ──
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Library & Scanning
builder.Services.AddScoped<ILibraryRootService, LibraryRootService>();
builder.Services.AddScoped<IFileScanner, FileScanner>();
builder.Services.AddScoped<IMetadataReader, MetadataReader>();
builder.Services.AddHttpClient<OnlineArtworkService>();
builder.Services.AddScoped<IArtworkService, ArtworkService>();
builder.Services.AddScoped<ILibraryIndexService, LibraryIndexService>();
builder.Services.AddSingleton<IScanQueue, ScanQueue>();
builder.Services.AddHostedService<LibraryScanBackgroundService>();

// Streaming & Playlists
builder.Services.AddScoped<IStreamingService, StreamingService>();
builder.Services.AddScoped<IPlaylistService, PlaylistService>();
builder.Services.AddScoped<IMixedTapeService, MixedTapeService>();

// Auditorium
builder.Services.AddSingleton<AuditoriumStateService>();
builder.Services.AddSignalR();

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
        if (!await roleManager.RoleExistsAsync("Admin"))
            await roleManager.CreateAsync(new ApplicationRole("Admin"));
        if (!await roleManager.RoleExistsAsync("User"))
            await roleManager.CreateAsync(new ApplicationRole("User"));
        logger.LogInformation("Roles seeded.");

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.FindByNameAsync("admin") == null)
        {
            var admin = new ApplicationUser
            {
                UserName = "admin",
                Email = "admin@sonafly.local",
                EmailConfirmed = true,
                DisplayName = "Administrator",
                IsEnabled = true
            };
            var adminPassword = app.Configuration["SonaFly:AdminDefaultPassword"] ?? "Admin123!";
            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "Admin");
                logger.LogInformation("Admin user seeded successfully.");
            }
            else
            {
                logger.LogError("Failed to create admin user: {Errors}",
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
        else
        {
            logger.LogInformation("Admin user already exists, skipping seed.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during database seeding.");
        throw; // Fail fast so the issue is visible
    }
}

// ── Middleware Pipeline ──
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseDefaultFiles();
app.MapStaticAssets();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("DevCors");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AuditoriumHub>("/hubs/auditorium");

app.MapFallbackToFile("/index.html");

app.Run();
