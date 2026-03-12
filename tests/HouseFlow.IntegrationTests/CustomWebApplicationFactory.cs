using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HouseFlow.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public CustomWebApplicationFactory()
    {
        // Set JWT environment variable before the host is built
        Environment.SetEnvironmentVariable("JWT__KEY", "TestSecretKeyForJWTTokenGeneration123456TestSecretKeyForJWTTokenGeneration123456");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Set environment to Testing so Program.cs uses InMemory database
        builder.UseEnvironment("Testing");

        // Add test configuration for JWT
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
    }

    protected override void Dispose(bool disposing)
    {
        // Clean up environment variable
        Environment.SetEnvironmentVariable("JWT__KEY", null);
        base.Dispose(disposing);
    }
}
