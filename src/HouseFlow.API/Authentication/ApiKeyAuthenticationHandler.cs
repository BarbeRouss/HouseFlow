using System.Security.Claims;
using System.Text.Encodings.Web;
using HouseFlow.Application.Interfaces;
using HouseFlow.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HouseFlow.API.Authentication;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string ApiKeyHeaderName = "X-API-Key";
    private const string ApiKeyBearerPrefix = "Bearer hf_";

    private readonly IApiKeyService _apiKeyService;
    private readonly HouseFlowDbContext _context;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyService apiKeyService,
        HouseFlowDbContext context)
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
        _context = context;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Try to extract the API key from the request
        string? rawKey = null;

        if (Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader))
        {
            rawKey = apiKeyHeader.ToString();
        }
        else if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authValue = authHeader.ToString();
            if (authValue.StartsWith("Bearer hf_", StringComparison.Ordinal))
            {
                rawKey = authValue["Bearer ".Length..];
            }
        }

        if (string.IsNullOrEmpty(rawKey))
        {
            return AuthenticateResult.NoResult();
        }

        var result = await _apiKeyService.ValidateKeyAsync(rawKey);
        if (result == null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var (userId, scope) = result.Value;

        // Look up the user's email for claims
        var userEmail = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();

        if (userEmail == null)
        {
            return AuthenticateResult.Fail("User not found.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, userEmail),
            new Claim("api_key_scope", scope.ToString())
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
