using HouseFlow.API.Configuration;
using HouseFlow.Application.OAuth;
using Microsoft.AspNetCore;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace HouseFlow.API.OAuth;

/// <summary>
/// OpenIddict handler of the authorization request, ordered <b>before</b> OpenIddict looks the
/// <c>client_id</c> up (<c>Authentication.ValidateAuthentication</c> runs the authentication pipeline and its
/// <c>ValidateClientId</c>): a <c>client_id</c> that is an URL
/// (issue #308) has its application created or refreshed from its metadata document first. Any other
/// <c>client_id</c> (dynamic registration) is left alone. A refusal is <c>invalid_client</c> shown
/// on the error page — never a redirect to a <c>redirect_uri</c> nobody validated.
/// </summary>
public sealed class ClientMetadataDocumentHandler : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>
{
    private readonly ClientMetadataProvisioner _provisioner;
    private readonly IOptions<OAuthOptions> _options;

    public ClientMetadataDocumentHandler(ClientMetadataProvisioner provisioner, IOptions<OAuthOptions> options)
    {
        _provisioner = provisioner;
        _options = options;
    }

    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateAuthorizationRequestContext>()
            .UseScopedHandler<ClientMetadataDocumentHandler>()
            .SetOrder(OpenIddictServerHandlers.Authentication.ValidateAuthentication.Descriptor.Order - 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(ValidateAuthorizationRequestContext context)
    {
        if (!ClientIdMetadataUrl.LooksLikeUrl(context.ClientId))
            return;

        var request = context.Transaction.GetHttpRequest()
            ?? throw new InvalidOperationException("The ASP.NET Core request cannot be retrieved.");

        var outcome = await _provisioner.EnsureAsync(
            context.ClientId!,
            OAuthResources.HouseFlowOrigins(request, _options.Value),
            ClientPartitionKey(request),
            request.HttpContext.RequestAborted);

        if (!outcome.Succeeded)
            context.Reject(Errors.InvalidClient, outcome.Description);
    }

    private static string ClientPartitionKey(HttpRequest request) =>
        HttpEdge.ClientPartitionKey(request.HttpContext.Connection.RemoteIpAddress);
}
