using System.Net;
using FluentAssertions;
using HouseFlow.Application.OAuth;

namespace HouseFlow.UnitTests.OAuth;

/// <summary>The SSRF guard of the metadata-document download (#308): only public addresses pass.</summary>
public class OutboundAddressPolicyTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("160.79.104.10")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("168.63.129.15")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2a00:1450:4007:80f::200e")]
    [InlineData("::ffff:8.8.8.8")]
    public void Public(string address) => OutboundAddressPolicy.IsPublic(IPAddress.Parse(address)).Should().BeTrue();

    [Theory]
    [InlineData("127.0.0.1", "loopback")]
    [InlineData("127.255.255.254", "loopback range")]
    [InlineData("0.0.0.0", "this host")]
    [InlineData("0.1.2.3", "0/8")]
    [InlineData("10.0.0.1", "RFC 1918")]
    [InlineData("172.16.0.1", "RFC 1918")]
    [InlineData("172.31.255.255", "RFC 1918")]
    [InlineData("192.168.1.1", "RFC 1918")]
    [InlineData("169.254.169.254", "link-local: the Azure instance metadata service")]
    [InlineData("168.63.129.16", "Azure wireserver, in a public range")]
    [InlineData("100.64.0.1", "carrier-grade NAT")]
    [InlineData("100.127.255.255", "carrier-grade NAT")]
    [InlineData("192.0.0.1", "IETF protocol assignments")]
    [InlineData("192.0.2.1", "documentation")]
    [InlineData("198.18.0.1", "benchmarking")]
    [InlineData("198.51.100.7", "documentation")]
    [InlineData("203.0.113.9", "documentation")]
    [InlineData("224.0.0.1", "multicast")]
    [InlineData("239.255.255.250", "multicast")]
    [InlineData("240.0.0.1", "reserved")]
    [InlineData("255.255.255.255", "broadcast")]
    [InlineData("::", "unspecified")]
    [InlineData("::1", "IPv6 loopback")]
    [InlineData("fe80::1", "IPv6 link-local")]
    [InlineData("fc00::1", "unique local")]
    [InlineData("fd12:3456:789a::1", "unique local")]
    [InlineData("ff02::1", "IPv6 multicast")]
    [InlineData("::ffff:10.0.0.1", "IPv4-mapped private address")]
    [InlineData("::ffff:127.0.0.1", "IPv4-mapped loopback")]
    [InlineData("::ffff:169.254.169.254", "IPv4-mapped metadata service")]
    [InlineData("::ffff:168.63.129.16", "IPv4-mapped wireserver")]
    [InlineData("::10.0.0.1", "IPv4-compatible private address")]
    [InlineData("64:ff9b::a00:1", "NAT64 wrapping 10.0.0.1")]
    [InlineData("2002:a00:1::1", "6to4 wrapping 10.0.0.1")]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2", "Teredo")]
    [InlineData("2001:db8::1", "documentation")]
    public void NotPublic(string address, string reason) =>
        OutboundAddressPolicy.IsPublic(IPAddress.Parse(address)).Should().BeFalse(reason);
}
