using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Hangfire;
using Hangfire.PostgreSql;
using HouseFlow.API.Filters;
using HouseFlow.API.Middleware;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using HouseFlow.Infrastructure.Data;
using HouseFlow.Infrastructure.Jobs;
using HouseFlow.Infrastructure.Services;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Serilog;
using Serilog.Events;
using BCryptNet = BCrypt.Net.BCrypt;

// Configure Serilog with async console sink for better performance
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Query", LogEventLevel.Error)
    .Enrich.FromLogContext()
    .WriteTo.Async(a => a.Console())
    .CreateLogger();

try
{

var builder = WebApplication.CreateBuilder(args);

// Use Serilog
builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<DomainExceptionFilter>();
    })
    .AddJsonOptions(options =>
    {
        // Support string-to-enum conversion for JSON requests
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// Database
if (builder.Environment.IsProduction() || builder.Environment.EnvironmentName == "Staging")
{
    // Azure: Entra ID (Managed Identity) passwordless auth to PostgreSQL
    var connectionString = builder.Configuration.GetConnectionString("houseflow")
        ?? throw new InvalidOperationException("ConnectionStrings:houseflow not configured for Production/Staging");

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);

    // If no password in connection string, use Entra ID token authentication
    var connStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
    if (string.IsNullOrEmpty(connStringBuilder.Password))
    {
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
        });

        dataSourceBuilder.UsePeriodicPasswordProvider(async (_, ct) =>
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(["https://ossrdbms-aad.database.windows.net/.default"]), ct);
            return token.Token;
        }, TimeSpan.FromHours(24), TimeSpan.FromSeconds(10));
    }

    var dataSource = dataSourceBuilder.Build();

    builder.Services.AddDbContext<HouseFlowDbContext>(options =>
        options.UseNpgsql(dataSource, npgsqlOptions =>
            npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
}
else
{
    // Local (Development, Testing, CI): Aspire-managed connection
    builder.AddNpgsqlDbContext<HouseFlowDbContext>("houseflow", configureDbContextOptions: options =>
    {
        options.UseNpgsql(npgsqlOptions =>
            npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    });
}

// --migrate mode: apply migrations and exit (used by init containers)
// Must run before JWT/Hangfire/etc. config since init containers don't have those env vars
if (args.Contains("--migrate"))
{
    var migrateApp = builder.Build();
    using var scope = migrateApp.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HouseFlowDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Running database migrations...");
        dbContext.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating the database.");
        throw;
    }

    return; // Exit after migration — do not start the web server
}

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IHouseMemberService, HouseMemberService>();
builder.Services.AddScoped<IHouseService, HouseService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IMaintenanceService, MaintenanceService>();
builder.Services.AddScoped<IMaintenanceCalculatorService, MaintenanceCalculatorService>();
builder.Services.AddScoped<IUserSettingsService, UserSettingsService>();
builder.Services.AddScoped<CleanupExpiredInvitationsJob>();

// Hangfire (background jobs) — uses a separate "hangfire" schema
var hangfireEnabled = false;
{
    var hangfireConnStr = builder.Configuration.GetConnectionString("houseflow");
    if (!string.IsNullOrEmpty(hangfireConnStr))
    {
        hangfireEnabled = true;

        // For Entra ID auth: get a token and inject it as password in the connection string
        var hangfireConnBuilder = new NpgsqlConnectionStringBuilder(hangfireConnStr);
        if (string.IsNullOrEmpty(hangfireConnBuilder.Password))
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
            });
            var tokenResponse = credential.GetToken(
                new TokenRequestContext(["https://ossrdbms-aad.database.windows.net/.default"]));
            hangfireConnBuilder.Password = tokenResponse.Token;
            hangfireConnStr = hangfireConnBuilder.ConnectionString;
        }

        builder.Services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(hangfireConnStr),
                new PostgreSqlStorageOptions { SchemaName = "hangfire" }));

        builder.Services.AddHangfireServer();
    }
}

// JWT Authentication
// JWT Key priority: 1. Environment variable 2. Configuration file 3. User secrets
var jwtKey = Environment.GetEnvironmentVariable("JWT__KEY")
    ?? builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT Key not configured. Set JWT__KEY environment variable or Jwt:Key in configuration.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("JWT Issuer not configured");
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("JWT Audience not configured");

// Validate JWT key strength (minimum 256 bits = 32 bytes)
if (jwtKey.Length < 32)
{
    throw new InvalidOperationException("JWT Key must be at least 32 characters (256 bits) for security.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero // Remove default 5-minute tolerance
        };
    });

builder.Services.AddAuthorization();

// Health checks
// Aspire's AddNpgsqlDbContext already registers a "HouseFlowDbContext" health check in Development,
// so only add it explicitly for non-Development environments to avoid duplicates.
var healthChecks = builder.Services.AddHealthChecks();
if (!builder.Environment.IsDevelopment())
{
    healthChecks.AddDbContextCheck<HouseFlowDbContext>();
}

// NSwag OpenAPI
builder.Services.AddOpenApiDocument(config =>
{
    config.Title = "HouseFlow API";
    config.Version = "v1";
    config.Description = "API for the HouseFlow Home Maintenance Application";
    config.AddSecurity("Bearer", new NSwag.OpenApiSecurityScheme
    {
        Type = NSwag.OpenApiSecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme."
    });
});

// CORS — configurable via CORS__ORIGINS environment variable (comma-separated)
var corsOrigins = Environment.GetEnvironmentVariable("CORS__ORIGINS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? ["http://localhost:3000", "https://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins.Length == 1 && corsOrigins[0] == "*")
            policy.SetIsOriginAllowed(_ => true);
        else
            policy.WithOrigins(corsOrigins);

        policy.AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Rate Limiting (only enabled for Azure environments)
if (builder.Environment.IsProduction() || builder.Environment.EnvironmentName == "Staging")
{
    builder.Services.AddRateLimiter(options =>
    {
        // Auth endpoints: 5 requests per minute per IP (prevent brute force)
        options.AddPolicy("auth", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: GetClientIp(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

        // API endpoints: 100 requests per minute per IP
        options.AddPolicy("api", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: GetClientIp(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 10
                }));

        // Global fallback: 200 requests per minute per IP
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: GetClientIp(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 200,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                }));

        options.OnRejected = async (context, token) =>
        {
            context.HttpContext.Response.StatusCode = 429;

            double? retryAfterSeconds = null;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                retryAfterSeconds = retryAfter.TotalSeconds;
            }

            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                error = "Rate limit exceeded. Please try again later.",
                retryAfter = retryAfterSeconds
            }, cancellationToken: token);
        };
    });
}

var app = builder.Build();

// Auto-migrate in Development (local dev + integration tests via Aspire)
// Production/Staging use --migrate flag or init containers for controlled deployments
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HouseFlowDbContext>();
    dbContext.Database.Migrate();
}

// Seed default admin user (Development only - NOT for production)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HouseFlowDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    const string adminEmail = "admin@admin.com";
    if (!dbContext.Users.Any(u => u.Email == adminEmail))
    {
        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            Email = adminEmail,
            PasswordHash = BCryptNet.HashPassword("admin"),
            FirstName = "Admin",
            LastName = "User",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Users.Add(adminUser);
        dbContext.SaveChanges();
        logger.LogInformation("Default admin user created: {Email}", adminEmail);
    }
}

// Seed demo user when DEMO_MODE is enabled (PR previews + local dev with DEMO_MODE=true)
if (string.Equals(app.Configuration["DEMO_MODE"], "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HouseFlowDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    const string demoEmail = "demo@demo.com";
    if (!dbContext.Users.Any(u => u.Email == demoEmail))
    {
        var demoUser = new User
        {
            Id = Guid.NewGuid(),
            Email = demoEmail,
            PasswordHash = BCryptNet.HashPassword("demo"),
            FirstName = "Demo",
            LastName = "User",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Users.Add(demoUser);

        var house = new House
        {
            Id = Guid.NewGuid(),
            Name = "Ma maison",
            UserId = demoUser.Id,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Houses.Add(house);

        var member = new HouseMember
        {
            Id = Guid.NewGuid(),
            UserId = demoUser.Id,
            HouseId = house.Id,
            Role = HouseRole.Owner,
            CanLogMaintenance = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.HouseMembers.Add(member);

        dbContext.SaveChanges();
        logger.LogInformation("Demo user created: {Email} (password: demo)", demoEmail);
    }
}

// Hangfire dashboard + recurring jobs
if (hangfireEnabled)
{
    if (app.Environment.IsDevelopment())
    {
        app.UseHangfireDashboard("/hangfire");
    }

    var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
    jobManager.AddOrUpdate<CleanupExpiredInvitationsJob>(
        "cleanup-expired-invitations",
        job => job.ExecuteAsync(),
        Cron.Daily); // Runs once per day
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi();
}

// Forward headers from reverse proxy (must be before any middleware that uses client IP)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseHttpsRedirection();

// Security headers middleware
app.UseMiddleware<SecurityHeadersMiddleware>();

// Rate limiter middleware (only for Azure environments)
if (app.Environment.IsProduction() || app.Environment.EnvironmentName == "Staging")
{
    app.UseRateLimiter();
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health");
app.MapHealthChecks("/alive", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // No checks, just confirms the app is running
});

app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Extracts the client IP address from the HTTP context.
/// Uses RemoteIpAddress which is populated by the ForwardedHeaders middleware
/// when behind a reverse proxy (X-Forwarded-For), or the direct connection IP otherwise.
/// </summary>
static string GetClientIp(HttpContext context)
{
    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
