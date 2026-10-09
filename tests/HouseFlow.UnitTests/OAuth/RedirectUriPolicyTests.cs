using FluentAssertions;
using HouseFlow.Application.OAuth;

namespace HouseFlow.UnitTests.OAuth;

/// <summary>Truth table of the redirect URIs a dynamically registered OAuth client may declare.</summary>
public class RedirectUriPolicyTests
{
    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback")]
    [InlineData("https://claude.ai")]
    [InlineData("https://example.com:8443/cb?tenant=a")]
    [InlineData("HTTPS://Claude.AI/callback")]
    [InlineData("http://127.0.0.1/cb")]
    [InlineData("http://127.0.0.1:9/cb")]
    [InlineData("http://localhost:3000/cb")]
    [InlineData("http://LOCALHOST:3000/cb")]
    [InlineData("http://[::1]:8080/cb")]
    [InlineData("https://xn--mnchen-3ya.de/cb")]
    public void Allowed(string uri) => RedirectUriPolicy.IsAllowed(uri).Should().BeTrue();

    [Theory]
    [InlineData(null, "missing")]
    [InlineData("", "empty")]
    [InlineData("/callback", "relative")]
    [InlineData("http://example.com/cb", "plain http to a remote host")]
    [InlineData("http://10.0.0.5/cb", "plain http to a private, non-loopback address")]
    [InlineData("http://127.0.0.2/cb", "only the canonical loopback hosts")]
    [InlineData("http://localhost.evil.com/cb", "not a loopback host")]
    [InlineData("https://localhost/cb", "loopback is plain http (RFC 8252)")]
    [InlineData("https://127.0.0.1:8443/cb", "loopback is plain http (RFC 8252)")]
    [InlineData("myapp://callback", "custom scheme")]
    [InlineData("com.example.app:/oauth", "private-use scheme")]
    [InlineData("javascript:alert(1)", "script")]
    [InlineData("data:text/html,hello", "data")]
    [InlineData("file:///etc/passwd", "file")]
    [InlineData("ftp://example.com/cb", "other scheme")]
    [InlineData("https://claude.ai/cb#fragment", "fragment")]
    [InlineData("https://claude.ai/cb#", "empty fragment")]
    [InlineData("https://user:pass@claude.ai/cb", "user info")]
    [InlineData("https://claude.ai@evil.com/cb", "user info hiding the real host")]
    [InlineData("https:claude.ai/cb", "authority not spelled out")]
    [InlineData("https:///cb", "no host")]
    [InlineData("https://claude.ai/c b", "white space")]
    [InlineData(" https://claude.ai/cb", "leading white space")]
    [InlineData("https://claude.ai/cb\n", "control character")]
    [InlineData("https://\u0441laude.ai/cb", "Cyrillic letter imitating a Latin one")]
    [InlineData("https://m\u00fcnchen.de/cb", "internationalized host not in its xn-- form")]
    [InlineData("https://\uFFFD.example/cb", "character IDNA rejects")]
    [InlineData("https://claude.ai/caf\u00e9", "non-ASCII path")]
    public void Refused(string? uri, string reason) => RedirectUriPolicy.IsAllowed(uri).Should().BeFalse(reason);

    [Fact]
    public void Refused_WhenAbsurdlyLong() =>
        RedirectUriPolicy.IsAllowed("https://claude.ai/" + new string('a', RedirectUriPolicy.MaxLength)).Should().BeFalse();

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback", "claude.ai")]
    [InlineData("HTTPS://Claude.AI/callback", "claude.ai")]
    [InlineData("https://example.com:443/cb", "example.com")]
    [InlineData("https://example.com:8443/cb?tenant=a", "example.com:8443")]
    [InlineData("http://127.0.0.1/cb", "127.0.0.1")]
    [InlineData("http://127.0.0.1:9/cb", "127.0.0.1:9")]
    [InlineData("http://localhost:3000/cb", "localhost:3000")]
    [InlineData("http://[::1]:8080/cb", "[::1]:8080")]
    [InlineData("https://xn--mnchen-3ya.de/cb", "xn--mnchen-3ya.de")]
    // Registered before internationalized hosts were refused: shown in punycode, never as look-alike letters.
    [InlineData("https://m\u00fcnchen.de/cb", "xn--mnchen-3ya.de")]
    [InlineData("https://\u0441laude.ai/cb", "xn--laude-0ye.ai")]
    // Characters IDNA forbids, on which Uri.IdnHost throws: escaped, never a crash nor an invisible character.
    [InlineData("https://\uFFFD.example/cb", "%EF%BF%BD.example")]
    [InlineData("https://a\u200Db.example:8443/cb", "a%E2%80%8Db.example:8443")]
    public void DisplayHost_IsTheAsciiHost_WithItsPortUnlessDefault(string uri, string expected) =>
        RedirectUriPolicy.DisplayHost(new Uri(uri)).Should().Be(expected);
}
