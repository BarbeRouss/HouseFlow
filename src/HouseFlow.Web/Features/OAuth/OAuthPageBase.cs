using HouseFlow.Web.Auth;
using HouseFlow.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace HouseFlow.Web.Features.OAuth;

/// <summary>Why an OAuth page cannot go on (<see cref="OAuthErrorCard"/>).</summary>
public enum OAuthErrorKind
{
    /// <summary>?returnUrl= is not the API's /connect/authorize, or carries no client_id.</summary>
    Invalid,

    /// <summary>The client_id is not (or no longer) registered.</summary>
    UnknownClient,

    /// <summary>The request asks for no scope the user can grant to this client.</summary>
    NoScope,

    /// <summary>The page is displayed inside a frame (clickjacking).</summary>
    Framed,
}

/// <summary>
/// The OAuth pages (#304, /oauth/authorize and /oauth/consent), where the API's GET /connect/authorize
/// sends the browser with ?returnUrl= = the authorization request to resume. Both end with a
/// full-page navigation back to it, so both start with <see cref="CheckRequestAsync"/>: the returnUrl
/// is only followed once pinned to the API's own endpoint (<see cref="OAuthReturnUrl.Validate"/>),
/// and neither page works inside a frame. <see cref="Resume"/> can only go to that validated request.
/// </summary>
public abstract class OAuthPageBase : AppComponentBase
{
    [Inject] protected AppConfig Config { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;

    [Parameter] public new string Locale { get; set; } = "fr";

    /// <summary>The /connect/authorize request as the API sent it. Unchecked: see <see cref="Request"/>.</summary>
    [SupplyParameterFromQuery] public string? ReturnUrl { get; set; }

    /// <summary>The validated request (set by <see cref="CheckRequestAsync"/>); null until then or when refused.</summary>
    protected string? Request { get; private set; }

    /// <summary>
    /// Validates ?returnUrl= then the framing. Null when the page may go on (<see cref="Request"/> set),
    /// else the reason to show instead — and nothing to navigate to. <see cref="Request"/> is set before
    /// the first await, so the render that happens during the frame check already knows it.
    /// </summary>
    protected async Task<OAuthErrorKind?> CheckRequestAsync()
    {
        Request = OAuthReturnUrl.Validate(ReturnUrl, Config.ApiBaseUrl);
        if (Request is null) return OAuthErrorKind.Invalid;
        if (!await IsFramedAsync()) return null;
        Request = null;
        return OAuthErrorKind.Framed;
    }

    /// <summary>
    /// Back to the API's /connect/authorize (full page load), which answers the application:
    /// with a code, or with <c>access_denied</c> when <paramref name="denied"/>.
    /// </summary>
    protected void Resume(bool denied = false)
    {
        if (Request is null) return;
        Nav.NavigateTo(denied ? OAuthReturnUrl.Denied(Request) : Request, forceLoad: true);
    }

    /// <summary>
    /// True inside a frame, where an « Autoriser » button could be clickjacked (RFC 9700 §4.16).
    /// Fails closed: an interop failure counts as framed.
    /// </summary>
    private async Task<bool> IsFramedAsync()
    {
        try
        {
            return await JS.InvokeAsync<bool>("hf.isFramed");
        }
        catch (JSException)
        {
            return true;
        }
    }
}
