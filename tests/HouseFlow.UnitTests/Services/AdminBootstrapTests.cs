using FluentAssertions;
using HouseFlow.Application.Common;
using Microsoft.Extensions.Configuration;

namespace HouseFlow.UnitTests.Services;

public class AdminBootstrapTests
{
    private static IConfiguration Build(params (string key, string value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.key, e.value)))
            .Build();

    [Fact]
    public void GetBootstrapEmails_ReadsConfiguredList_TrimmedAndWithoutBlanks()
    {
        var config = Build(("Admin:BootstrapEmails:0", " julienrousselle@outlook.be "), ("Admin:BootstrapEmails:1", ""));

        AdminBootstrap.GetBootstrapEmails(config).Should().Equal("julienrousselle@outlook.be");
    }

    [Fact]
    public void GetBootstrapEmails_WithoutSection_IsEmpty()
    {
        AdminBootstrap.GetBootstrapEmails(Build()).Should().BeEmpty();
    }

    [Fact]
    public void IsBootstrapAdmin_IsCaseInsensitive()
    {
        var config = Build(("Admin:BootstrapEmails:0", "julienrousselle@outlook.be"));

        AdminBootstrap.IsBootstrapAdmin(config, "JulienRousselle@Outlook.be").Should().BeTrue();
        AdminBootstrap.IsBootstrapAdmin(config, "someone@example.com").Should().BeFalse();
    }

    [Fact]
    public void DemoAccount_IsBootstrapAdmin_OnlyInDemoMode()
    {
        AdminBootstrap.IsBootstrapAdmin(Build(("DEMO_MODE", "true")), AdminBootstrap.DemoEmail).Should().BeTrue();
        AdminBootstrap.IsBootstrapAdmin(Build(("DEMO_MODE", "false")), AdminBootstrap.DemoEmail).Should().BeFalse();
        AdminBootstrap.IsBootstrapAdmin(Build(), AdminBootstrap.DemoEmail).Should().BeFalse();
    }

    [Fact]
    public void GetBootstrapEmails_InDemoMode_DoesNotDuplicateDemoAccount()
    {
        var config = Build(("DEMO_MODE", "true"), ("Admin:BootstrapEmails:0", "DEMO@demo.com"));

        AdminBootstrap.GetBootstrapEmails(config).Should().ContainSingle();
    }
}
