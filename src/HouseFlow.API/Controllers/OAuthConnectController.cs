using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using HouseFlow.API.Configuration;
using HouseFlow.API.Extensions;
using HouseFlow.API.OAuth;
using HouseFlow.Application.Interfaces;
using HouseFlow.Application.OAuth;
using HouseFlow.Core.Entities;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace HouseFlow.API.Controllers;

/// <summary>
/// OAuth 2.1 protocol endpoints of the embedded authorization server (issue #304). OpenIddict has
/// already validated each request (client, redirect URI, PKCE, scopes) when it reaches these
/// actions (passthrough mode); they decide who the user is and what is granted. The revocation and
/// discovery endpoints are handled by OpenIddict alone. Outside <c>/api/v1</c>: these follow the
/// OAuth RFCs (errors as <c>{ error, error_description }</c>), not the API's ProblemDetails contract.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public class OAuthConnectController : ControllerBase
{
    /// <summary>
    /// Added by the consent screen to the authorization URL when the user clicks « Refuser »
    /// (<c>houseflow_consent=denied</c>). Any occurrence of it is a refusal, whatever its value.
    /// </summary>
    public const string ConsentDeniedParameter = "houseflow_consent";

    private readonly IApplicationDbContext _context;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly OAuthConsentService _consents;
    private readonly OAuthSessionCookie _sessionCookie;
    private readonly OAuthOptions _options;
    private readonly ILogger<OAuthConnectController> _logger;

    public OAuthConnectController(
        IApplicationDbContext context,
        IOpenIddictApplicationManager applicationManager,
        OAuthConsentService consents,
        OAuthSessionCookie sessionCookie,
        IOptions<OAuthOptions> options,
        ILogger<OAuthConnectController> logger)
    {
        _context = context;
        _applicationManager = applicationManager;
        _consents = consents;
        _sessionCookie = sessionCookie;
        _options = options.Value;
        _logger = logger;
    }

    // ------------------------------------------------------------------ authorization endpoint

    [HttpGet("~/" + OpenIddictSetup.AuthorizationEndpoint)]
    [HttpPost("~/" + OpenIddictSetup.AuthorizationEndpoint)]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // 1. RFC 8707 — before anything is shown to the user: the token can only be meant for the
        //    MCP resource server. Answered here, not redirected to the client: the error does not
        //    come from the user, and anyone can register a client (RFC 9700 §4.11.2).
        var resources = OAuthResources.Resolve(request.GetResources(), OAuthResources.Allowed(Request, _options));
        if (resources is null)
            return LocalError(Errors.InvalidTarget, "The requested resource is not served by this authorization server.");

        // 2. Who is the user? The oauthSession cookie, posed by the front end once logged in.
        if (_sessionCookie.Read(Request) is not { } session)
        {
            if (request.HasPromptValue(PromptValues.None))
                return ForbidWith(Errors.LoginRequired, "The user is not logged in.");

            return RedirectToFrontend("oauth/authorize", CurrentAuthorizationUrl());
        }

        // 3. The account must still exist and not be restricted (RGPD Art. 18).
        var user = await FindActiveUserAsync(session.UserId, cancellationToken);
        if (user is null)
            return ForbidWith(Errors.AccessDenied, "This account cannot authorize applications.");

        AttributeAuditTo(user.Id);

        // 4. « Refuser » on the consent screen. The parameter in any case, with any value, any number
        //    of times, in the query or the form: a request crafted with a houseflow_consent of its own
        //    must never turn the user's refusal into a code.
        if (NamesConsentDeniedParameter())
            return ForbidWith(Errors.AccessDenied, "The user denied the authorization request.");

        var application = await _applicationManager.FindByClientIdAsync(request.ClientId!, cancellationToken)
            ?? throw new InvalidOperationException("The client application cannot be found.");

        // A request without any HouseFlow scope asks for the scopes the client registered for
        // (RFC 6749 §3.3: a pre-defined default value).
        var requested = request.GetScopes().Where(OAuthScopes.IsGrantable).ToList();
        var scopeDefaulted = requested.Count == 0;
        if (scopeDefaulted)
            requested = [.. await _consents.GetClientScopesAsync(application, cancellationToken)];

        // 5. Existing consent: what was granted and is asked again.
        var (authorizationId, granted) = await _consents.FindConsentAsync(user.Id, application, cancellationToken);
        var scopes = requested.Intersect(granted, StringComparer.Ordinal).ToList();

        // An earlier consent is not enough when the client asks for a scope it was not granted (the
        // user sees that it wants more, rather than a silently reduced code), when it asks for the
        // screen (prompt=consent), and always for a code delivered to a loopback listener: any
        // process of the machine can claim such a client's identity (RFC 8252 §8.6).
        var needsPrompt = !requested.All(scope => granted.Contains(scope, StringComparer.Ordinal))
            || request.HasPromptValue(PromptValues.Consent)
            || await DeliversToLoopbackAsync(application, request, cancellationToken);

        // Except on the way back from the consent screen: « Autoriser » names the client in the
        // session cookie — signed, never a parameter of this URL, which the client controls.
        var justConsented = session.ConsentedClientId is not null && session.ConsentedClientId == request.ClientId;

        if (authorizationId is null || scopes.Count == 0 || (needsPrompt && !justConsented))
        {
            if (request.HasPromptValue(PromptValues.None))
                return ForbidWith(Errors.ConsentRequired, "Interactive user consent is required.");

            // Coming back from the consent screen must not ask again: prompt=consent is dropped
            // from the return URL, and a defaulted scope is spelled out for the screen to show.
            return RedirectToFrontend("oauth/consent", CurrentAuthorizationUrl(
                dropConsentPrompt: true,
                scope: scopeDefaulted ? request.GetScopes().Where(scope => !OAuthScopes.IsGrantable(scope)).Concat(requested) : null));
        }

        // The consent just given is used once: the cookie forgets it, and the next authorization of
        // this client asks again wherever it must (a native client: every time).
        if (justConsented)
            _sessionCookie.Append(Response, user.Id);

        // 6. Issue the code: what was asked and granted, for the MCP resource, under the consent.
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString());
        identity.SetScopes(await WithOfflineAccessAsync(scopes, application, cancellationToken));
        identity.SetResources(resources);
        identity.SetAuthorizationId(authorizationId);
        identity.SetDestinations(GetDestinations);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // ------------------------------------------------------------------ token endpoint

    [HttpPost("~/" + OpenIddictSetup.TokenEndpoint)]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    [EnableRateLimiting(RateLimitPolicies.Token)]
    public async Task<IActionResult> Exchange(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Only these two grants are enabled: OpenIddict rejects any other before this action.
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            throw new InvalidOperationException("The specified grant type is not supported.");

        // The principal stored in the authorization code or refresh token. OpenIddict has already
        // refused a wrong code_verifier, a replayed code, a rotated or revoked refresh token, and a
        // revoked authorization.
        var principal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal
            ?? throw new InvalidOperationException("The authorization grant cannot be retrieved.");

        var user = Guid.TryParse(principal.GetClaim(Claims.Subject), out var userId)
            ? await FindActiveUserAsync(userId, cancellationToken)
            : null;
        if (user is null)
            return ForbidWith(Errors.InvalidGrant, "The account is no longer allowed to use this application.");

        AttributeAuditTo(user.Id);

        // RFC 8707 §2.2: a resource asked here must be one the grant covers (and still served).
        var resources = OAuthResources.Resolve(
            request.GetResources(), OAuthResources.Allowed(Request, _options), granted: principal.GetResources());
        if (resources is null)
            return ForbidWith(Errors.InvalidTarget, "The requested resource is not covered by this grant.");

        var identity = new ClaimsIdentity(principal.Claims,
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);
        identity.SetResources(resources);
        identity.SetDestinations(GetDestinations);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // ------------------------------------------------------------------ userinfo endpoint

    [HttpGet("~/" + OpenIddictSetup.UserInfoEndpoint)]
    [HttpPost("~/" + OpenIddictSetup.UserInfoEndpoint)]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    public IActionResult UserInfo() => Ok(new Dictionary<string, string>
    {
        [Claims.Subject] = User.GetClaim(Claims.Subject) ?? string.Empty,
        [Claims.Scope] = string.Join(' ', User.GetScopes().Where(OAuthScopes.IsGrantable))
    });

    // ------------------------------------------------------------------ dynamic client registration

    /// <summary>
    /// RFC 7591 Dynamic Client Registration, anonymous as MCP clients expect: public clients only,
    /// redirect URIs restricted by <see cref="RedirectUriPolicy"/>, consent always explicit.
    /// </summary>
    [HttpPost("~/" + OpenIddictSetup.RegistrationEndpoint)]
    [IgnoreAntiforgeryToken]
    [Consumes("application/json")]
    [Produces("application/json")]
    [RequestSizeLimit(16 * 1024)]
    [EnableRateLimiting(RateLimitPolicies.ClientRegistration)]
    public async Task<IActionResult> Register([FromBody] ClientRegistrationRequest? body, CancellationToken cancellationToken)
    {
        // RFC 7591 §3.2.1: the client information must not be cached.
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        if (!ModelState.IsValid) body = null; // malformed JSON or mistyped field
        if (!ClientRegistrationValidator.TryValidate(body, HouseFlowOrigins(), out var registration, out var error))
            return BadRequest(error);

        // 128 random bits, hexadecimal, no prefix (« hf_ » marks the API keys).
        var clientId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var descriptor = new OpenIddictApplicationDescriptor
        {
            // Loopback-only clients are native apps (RFC 8252): OpenIddict then accepts any port on
            // a loopback redirect URI registered without one (§7.3).
            ApplicationType = registration.RedirectUris.All(uri => RedirectUriPolicy.IsLoopback(new Uri(uri)))
                ? ApplicationTypes.Native
                : ApplicationTypes.Web,
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = registration.ClientName,
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.ResponseTypes.Code
            }
        };
        if (registration.AllowsRefreshTokens)
            descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
        foreach (var scope in registration.Scopes)
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        foreach (var uri in registration.RedirectUris)
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));

        descriptor.Properties[OAuthConsentService.ClientIdIssuedAtProperty] = JsonSerializer.SerializeToElement(issuedAt);
        if (registration.ClientUri is not null)
            descriptor.Properties[OAuthConsentService.ClientUriProperty] = JsonSerializer.SerializeToElement(registration.ClientUri);

        try
        {
            await _applicationManager.CreateAsync(descriptor, cancellationToken);
        }
        catch (OpenIddictExceptions.ValidationException exception)
        {
            return BadRequest(new ClientRegistrationError(ClientRegistrationError.InvalidClientMetadata,
                exception.Results.FirstOrDefault()?.ErrorMessage ?? "Invalid client metadata."));
        }

        // The client name is self-declared and the redirect URIs are not needed to trace it.
        _logger.LogInformation("OAuth client registered: {ClientId}", clientId);

        return StatusCode(StatusCodes.Status201Created, new ClientRegistrationResponse(
            ClientId: clientId,
            ClientIdIssuedAt: issuedAt,
            ClientName: registration.ClientName,
            RedirectUris: registration.RedirectUris,
            GrantTypes: registration.GrantTypes,
            ResponseTypes: registration.ResponseTypes,
            TokenEndpointAuthMethod: ClientRegistrationValidator.NoClientAuthentication,
            Scope: string.Join(' ', registration.Scopes),
            ClientUri: registration.ClientUri));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// HouseFlow's own origins, where no client may have codes sent: the API as this request reached
    /// it and as configured (issuer, MCP resources), and the front end (<see cref="OAuthOptions.WebBaseUrl"/>).
    /// </summary>
    private IReadOnlyCollection<Uri> HouseFlowOrigins() =>
        new[] { $"{Request.Scheme}://{Request.Host}", _options.WebBaseUrl, _options.Issuer }
            .Concat(OAuthResources.Allowed(Request, _options))
            .Select(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>()
            .ToList();

    /// <summary>An existing user whose processing is not restricted (RGPD Art. 18), or null.</summary>
    private async Task<User?> FindActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.ProcessingRestrictedAt == null, cancellationToken);

    /// <summary>
    /// These endpoints carry no API JWT: the user is known from the cookie or the grant. The OAuth
    /// rows written by OpenIddict in this request (code, tokens) are attributed to them, so that the
    /// account deletion anonymises them with the rest of the user's audit trail (Art. 17).
    /// </summary>
    private void AttributeAuditTo(Guid userId) =>
        _context.SetAuditContext(userId, null, HttpContext.GetClientIp(), Request.Headers.UserAgent.ToString());

    /// <summary>
    /// The code of this request goes to a loopback listener: the client is native (RFC 8252, its
    /// redirect URIs are all loopback ones, any port), or this redirect URI is a loopback one.
    /// </summary>
    private async Task<bool> DeliversToLoopbackAsync(object application, OpenIddictRequest request, CancellationToken cancellationToken) =>
        await _applicationManager.HasApplicationTypeAsync(application, ApplicationTypes.Native, cancellationToken)
        || (Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out var redirectUri) && RedirectUriPolicy.IsLoopback(redirectUri));

    private bool NamesConsentDeniedParameter() =>
        Request.Query.Keys
            .Concat(Request.HasFormContentType ? Request.Form.Keys : [])
            .Any(name => string.Equals(name, ConsentDeniedParameter, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <c>offline_access</c> (a refresh token) for every client registered with the refresh_token
    /// grant: MCP clients rarely ask for it, but need it. Never part of what the user consents to.
    /// </summary>
    private async Task<IEnumerable<string>> WithOfflineAccessAsync(
        IReadOnlyCollection<string> scopes, object application, CancellationToken cancellationToken) =>
        await _applicationManager.HasPermissionAsync(application, Permissions.GrantTypes.RefreshToken, cancellationToken)
            ? scopes.Append(Scopes.OfflineAccess)
            : scopes;

    /// <summary>
    /// The subject goes into the access token; nothing else about the user does (minimisation: no
    /// email, no name). No identity token is ever issued.
    /// </summary>
    private static IEnumerable<string> GetDestinations(Claim claim) =>
        claim.Type == Claims.Subject ? [Destinations.AccessToken] : [];

    /// <summary>
    /// An error answered to the browser instead of being redirected to the client: 400 with
    /// <c>{ error, error_description }</c>, never cached.
    /// </summary>
    private JsonResult LocalError(string error, string description)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return new JsonResult(new Dictionary<string, string>
        {
            [Parameters.Error] = error,
            [Parameters.ErrorDescription] = description
        })
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
    }

    private ForbidResult ForbidWith(string error, string description) => Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }),
        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    /// <summary>
    /// 302 to a page of the front end, which comes back to <paramref name="returnUrl"/> (validated
    /// there: same origin as the API, path /connect/authorize). No locale: the front end adds it.
    /// </summary>
    private RedirectResult RedirectToFrontend(string page, string returnUrl) =>
        Redirect($"{_options.WebBaseUrl}/{page}?returnUrl={Uri.EscapeDataString(returnUrl)}");

    /// <summary>
    /// Absolute URL of the current authorization request, as a GET. Kept verbatim unless it must
    /// change: a POSTed request, <c>prompt=consent</c> dropped, a defaulted scope spelled out.
    /// </summary>
    private string CurrentAuthorizationUrl(bool dropConsentPrompt = false, IEnumerable<string>? scope = null)
    {
        var url = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}";
        var consentPromptPresent = Request.Query[Parameters.Prompt].Any(value => value?.Contains(PromptValues.Consent) == true);
        if (!Request.HasFormContentType && scope is null && !(dropConsentPrompt && consentPromptPresent))
            return url + Request.QueryString;

        var parameters = new List<KeyValuePair<string, StringValues>>();
        foreach (var (name, values) in Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList())
        {
            if (name == Parameters.Scope && scope is not null) continue;
            if (name == Parameters.Prompt && dropConsentPrompt)
            {
                var prompt = string.Join(' ', values.SelectMany(value => (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .Where(value => value != PromptValues.Consent));
                if (prompt.Length > 0) parameters.Add(new(name, prompt));
                continue;
            }

            parameters.Add(new(name, values));
        }

        if (scope is not null)
            parameters.Add(new(Parameters.Scope, string.Join(' ', scope.Distinct(StringComparer.Ordinal))));

        return url + QueryString.Create(parameters);
    }
}
