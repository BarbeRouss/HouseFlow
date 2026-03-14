using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using HouseFlow.Infrastructure.Data;

namespace HouseFlow.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public CustomWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("JWT__KEY", "TestSecretKeyForJWTTokenGeneration123456TestSecretKeyForJWTTokenGeneration123456");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "TestSecretKeyForJWTTokenGeneration123456TestSecretKeyForJWTTokenGeneration123456",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                ["Jwt:RefreshTokenExpirationDays"] = "7"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<HouseFlowDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Add PostgreSQL Testcontainer
            services.AddDbContext<HouseFlowDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString(), npgsqlOptions =>
                    npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Environment.SetEnvironmentVariable("JWT__KEY", null);
        await _postgres.DisposeAsync();
    }
}
