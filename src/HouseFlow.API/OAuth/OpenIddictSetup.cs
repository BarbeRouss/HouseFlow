using HouseFlow.API.Authentication;
using HouseFlow.Application.OAuth;
using HouseFlow.Infrastructure.Data;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace HouseFlow.API.OAuth;

/// <summary>
/// The embedded OAuth 2.1 authorization server (issue #304, OpenIddict): authorization code flow
/// with mandatory PKCE (S256) and rotating refresh tokens, public clients registered dynamically
/// (RFC 7591), tokens bound to the MCP resource server (RFC 8707). Protocol endpoints live outside
/// <c>/api/v1</c>, under <c>/connect</c> and <c>/.well-known</c>.
/// </summary>
public static class OpenIddictSetup
{
    public const string AuthorizationEndpoint = "connect/authorize";
    public const string TokenEndpoint = "connect/token";
    public const string UserInfoEndpoint = "connect/userinfo";
    public const string RevocationEndpoint = "connect/revocation";
    public const string RegistrationEndpoint = "connect/register";

    /// <summary>RFC 8414 / RFC 7591 metadata name of the registration endpoint.</summary>
    public const string RegistrationEndpointMetadata = "registration_endpoint";

    public static IServiceCollection AddHouseFlowOAuth(
        this IServiceCollection services, IConfiguration configuration, string jwtKey, string jwtIssuer)
    {
        var section = configuration.GetSection(OAuthOptions.SectionName);
        var settings = section.Get<OAuthOptions>() ?? new OAuthOptions();

        services.AddOptions<OAuthOptions>()
            .Bind(section)
            .PostConfigure(options => options.WebBaseUrl =
                OAuthOptions.ResolveWebBaseUrl(options.WebBaseUrl, configuration["CORS:ORIGINS"]))
            .ValidateDataAnnotations()
            .Validate(options => options.HasValidUris(),
                "OAuth:WebBaseUrl, OAuth:Issuer and OAuth:Resources must be absolute http(s) URIs.")
            .ValidateOnStart();

        services.AddSingleton(new OAuthSessionCookie(jwtKey, jwtIssuer,
            TimeSpan.FromMinutes(settings.SessionCookieLifetimeMinutes),
            RefreshTokenCookie.ResolveSameSite(configuration)));
        services.AddScoped<OAuthConsentService>();

        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<HouseFlowDbContext>())
            .AddServer(options => ConfigureServer(options, settings, jwtKey))
            .AddValidation(options =>
            {
                // Tokens are validated with the server's own keys and options, in process.
                options.UseLocalServer();
                options.UseAspNetCore();
                // Look the token and its authorization up on every use: a revoked consent or a
                // revoked token stops working immediately, not at the end of its 15 minutes.
                options.EnableAuthorizationEntryValidation();
                options.EnableTokenEntryValidation();
            });

        return services;
    }

    private static void ConfigureServer(OpenIddictServerBuilder options, OAuthOptions settings, string jwtKey)
    {
        options.SetAuthorizationEndpointUris(AuthorizationEndpoint)
               .SetTokenEndpointUris(TokenEndpoint)
               .SetUserInfoEndpointUris(UserInfoEndpoint)
               .SetRevocationEndpointUris(RevocationEndpoint)
               // RFC 8414 and OpenID Connect discovery: the same document under both names.
               .SetConfigurationEndpointUris(".well-known/oauth-authorization-server", ".well-known/openid-configuration");

        // OAuth 2.1: the authorization code flow only — no implicit, password, client credentials
        // or device flow — and its refresh tokens (offline_access, added by AllowRefreshTokenFlow).
        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow();

        // PKCE on every authorization request, with S256 only: OpenIddict also accepts "plain" by
        // default — and treats a missing code_challenge_method as "plain" (RFC 7636 §4.3).
        options.RequireProofKeyForCodeExchange();
        options.Configure(server => server.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));

        options.RegisterScopes(OAuthScopes.HousesRead, OAuthScopes.HousesWrite);

        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(settings.AccessTokenLifetimeMinutes))
               .SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(settings.AuthorizationCodeLifetimeMinutes))
               .SetRefreshTokenLifetime(TimeSpan.FromDays(settings.RefreshTokenLifetimeDays));

        // Strict rotation: a refresh token already exchanged is refused at once, and its replay
        // revokes every token of the authorization (OpenIddict's reuse detection).
        options.SetRefreshTokenReuseLeeway(TimeSpan.Zero);

        // RFC 8707: the allowed resource defaults to the MCP endpoint of the request's host, which
        // OpenIddict's static resource registry cannot express. OAuthConnectController validates
        // the resource parameter instead (invalid_target), and every client may target it.
        options.DisableResourceValidation();
        options.IgnoreResourcePermissions();

        ConfigureKeys(options, jwtKey);

        if (!string.IsNullOrEmpty(settings.Issuer))
            options.SetIssuer(new Uri(settings.Issuer, UriKind.Absolute));

        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableUserInfoEndpointPassthrough()
               // TLS is terminated by the Azure Container Apps ingress: the API itself only ever
               // serves plain HTTP behind it (and in local development). The trusted forwarded
               // headers (HttpEdge) still give the requests their https scheme in Azure.
               .DisableTransportSecurityRequirement();

        options.AddEventHandler<OpenIddictServerEvents.HandleConfigurationRequestContext>(handler => handler
            .UseInlineHandler(context =>
            {
                context.Metadata[RegistrationEndpointMetadata] =
                    new Uri(context.BaseUri!, RegistrationEndpoint).AbsoluteUri;

                // Every client is public (DCR + PKCE): advertise "none" only, so that a client does
                // not try to register for an authentication method the registration would refuse.
                context.TokenEndpointAuthenticationMethods.Clear();
                context.TokenEndpointAuthenticationMethods.Add(ClientAuthenticationMethods.None);
                context.RevocationEndpointAuthenticationMethods.Clear();
                context.RevocationEndpointAuthenticationMethods.Add(ClientAuthenticationMethods.None);
                return default;
            })
            .SetOrder(OpenIddictServerHandlers.Discovery.AttachClientAuthenticationMethods.Descriptor.Order + 1_000)
            .SetType(OpenIddictServerHandlerType.Custom));
    }

    /// <summary>
    /// Signing and encryption keys derived from <c>Jwt:Key</c> (HKDF, <see cref="OAuthKeyDerivation"/>):
    /// identical on every replica and across restarts, with nothing new to provision.
    /// <para>
    /// OpenIddict refuses to start without an asymmetric signing key (ID0086), but it prefers a
    /// symmetric key for everything it signs except identity tokens, which only an asymmetric key
    /// can sign. HouseFlow never issues identity tokens (the <c>openid</c> scope is never granted),
    /// so the RSA key below signs nothing: an ephemeral one satisfies the check without making any
    /// token depend on the lifetime of the process.
    /// </para>
    /// </summary>
    private static void ConfigureKeys(OpenIddictServerBuilder options, string jwtKey)
    {
        options.AddSigningCredentials(new SigningCredentials(
            new SymmetricSecurityKey(OAuthKeyDerivation.SigningKey(jwtKey)), SecurityAlgorithms.HmacSha512));

        // Access tokens are encrypted as well (OpenIddict's default): opaque to the client.
        options.AddEncryptionKey(new SymmetricSecurityKey(OAuthKeyDerivation.EncryptionKey(jwtKey)));

        options.AddEphemeralSigningKey();
    }
}
