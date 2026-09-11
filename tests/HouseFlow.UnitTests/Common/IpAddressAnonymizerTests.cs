using FluentAssertions;
using HouseFlow.Application.Common;

namespace HouseFlow.UnitTests.Common;

public class IpAddressAnonymizerTests
{
    [Theory]
    [InlineData("192.168.1.42", "192.168.1.0")]
    [InlineData("10.0.0.1", "10.0.0.0")]
    [InlineData("8.8.8.8", "8.8.8.0")]
    [InlineData("255.255.255.255", "255.255.255.0")]
    public void Anonymize_IPv4_ZeroesLastOctet(string input, string expected)
    {
        IpAddressAnonymizer.Anonymize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("2001:db8:85a3:8d3:1319:8a2e:370:7348", "2001:db8:85a3::")]
    [InlineData("2001:0db8:0000:0000:0000:ff00:0042:8329", "2001:db8::")]
    [InlineData("fe80::1", "fe80::")]
    public void Anonymize_IPv6_KeepsOnlyFirst48Bits(string input, string expected)
    {
        IpAddressAnonymizer.Anonymize(input).Should().Be(expected);
    }

    [Fact]
    public void Anonymize_IPv4MappedIPv6_IsTreatedAsIPv4()
    {
        IpAddressAnonymizer.Anonymize("::ffff:192.168.1.42").Should().Be("192.168.1.0");
    }

    [Fact]
    public void Anonymize_Loopback_IsStillTruncated()
    {
        IpAddressAnonymizer.Anonymize("127.0.0.1").Should().Be("127.0.0.0");
        IpAddressAnonymizer.Anonymize("::1").Should().Be("::");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Anonymize_NullOrEmpty_ReturnsNull(string? input)
    {
        IpAddressAnonymizer.Anonymize(input).Should().BeNull();
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("999.1.1.1")]
    [InlineData("unknown")]
    public void Anonymize_Unparsable_ReturnsNull_RatherThanKeepingIdentifyingValue(string input)
    {
        IpAddressAnonymizer.Anonymize(input).Should().BeNull();
    }

    [Fact]
    public void Anonymize_IsIdempotent()
    {
        var once = IpAddressAnonymizer.Anonymize("192.168.1.42");
        IpAddressAnonymizer.Anonymize(once).Should().Be(once);
        IpAddressAnonymizer.IsAnonymized(once).Should().BeTrue();
        IpAddressAnonymizer.IsAnonymized("192.168.1.42").Should().BeFalse();
    }

    [Fact]
    public void Anonymize_RedactedMarker_IsPreserved()
    {
        IpAddressAnonymizer.Anonymize(IpAddressAnonymizer.Redacted).Should().Be(IpAddressAnonymizer.Redacted);
        IpAddressAnonymizer.IsAnonymized(IpAddressAnonymizer.Redacted).Should().BeTrue();
    }
}

public class GdprPolicyTests
{
    [Fact]
    public void IsConsentRequired_WhenNeverGiven_ReturnsTrue()
    {
        GdprPolicy.IsConsentRequired(null, null).Should().BeTrue();
    }

    [Fact]
    public void IsConsentRequired_WhenOlderPolicyVersion_ReturnsTrue()
    {
        GdprPolicy.IsConsentRequired(DateTime.UtcNow, "2020-01-01").Should().BeTrue();
    }

    [Fact]
    public void IsConsentRequired_WhenCurrentVersionAccepted_ReturnsFalse()
    {
        GdprPolicy.IsConsentRequired(DateTime.UtcNow, GdprPolicy.CurrentPolicyVersion).Should().BeFalse();
    }
}
