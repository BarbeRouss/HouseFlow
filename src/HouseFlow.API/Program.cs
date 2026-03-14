using System.Text;
using System.Threading.RateLimiting;
using HouseFlow.API.Middleware;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Infrastructure.Data;
using HouseFlow.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Support string-to-enum conversion for JSON requests
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// Database
if (builder.Environment.EnvironmentName == "Testing")
{
    builder.Services.AddDbContext<HouseFlowDbContext>(options =>
        options.UseInMemoryDatabase("InMemoryTestDb"));
}
else if (builder.Environment.EnvironmentName == "CI")
{
    // CI: use standard EF Core with explicit connection string (no Aspire orchestrator)
    var connectionString = builder.Configuration.GetConnectionString("houseflow")
        ?? throw new InvalidOperationException("ConnectionStrings:houseflow not configured for CI");
    builder.Services.AddDbContext<HouseFlowDbContext>(options =>
        options.UseNpgsql(connectionString, npgsqlOptions =>
            npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
}
else
{
    // Aspire adds the connection string automatically with the name "houseflow"
    // QuerySplittingBehavior.SplitQuery splits multi-collection queries for better performance
    builder.AddNpgsqlDbContext<HouseFlowDbContext>("houseflow", configureDbContextOptions: options =>
    {
        options.UseNpgsql(npgsqlOptions =>
            npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
    });
}

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IHouseService, HouseService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IMaintenanceService, MaintenanceService>();
builder.Services.AddScoped<IMaintenanceCalculatorService, MaintenanceCalculatorService>();

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
builder.Services.AddHealthChecks()
    .AddDbContextCheck<HouseFlowDbContext>();

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

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "https://localhost:3000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Rate Limiting (disabled for Development and Testing environments to allow E2E tests)
if (!builder.Environment.IsDevelopment() && builder.Environment.EnvironmentName != "Testing" && builder.Environment.EnvironmentName != "CI")
{
    builder.Services.AddRateLimiter(options =>
    {
        // Auth endpoints: 5 requests per minute (prevent brute force)
        options.AddFixedWindowLimiter("auth", limiterOptions =>
        {
            limiterOptions.PermitLimit = 5;
            limiterOptions.Window = TimeSpan.FromMinutes(1);
            limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            limiterOptions.QueueLimit = 0; // No queueing
        });

        // API endpoints: 100 requests per minute
        options.AddFixedWindowLimiter("api", limiterOptions =>
        {
            limiterOptions.PermitLimit = 100;
            limiterOptions.Window = TimeSpan.FromMinutes(1);
            limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            limiterOptions.QueueLimit = 10;
        });

        // Global fallback: 200 requests per minute
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
                factory: partition => new FixedWindowRateLimiterOptions
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

// Apply pending migrations automatically in Development mode
// Skip for Testing environment (uses InMemory database which doesn't support migrations)
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<HouseFlowDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            dbContext.Database.Migrate();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while migrating the database.");
            throw;
        }

        // Seed default admin user (Development only - NOT for production)
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
}
else if (app.Environment.EnvironmentName == "Testing")
{
    // For Testing environment with InMemory database, just ensure database is created
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<HouseFlowDbContext>();
        dbContext.Database.EnsureCreated();
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi();
}

app.UseHttpsRedirection();

// Security headers middleware
app.UseMiddleware<SecurityHeadersMiddleware>();

// Rate limiter middleware (only if rate limiting is configured)
if (!app.Environment.IsDevelopment() && app.Environment.EnvironmentName != "Testing" && app.Environment.EnvironmentName != "CI")
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
