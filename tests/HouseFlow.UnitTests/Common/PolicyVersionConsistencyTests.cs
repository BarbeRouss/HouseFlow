using System.Text.RegularExpressions;
using FluentAssertions;
using HouseFlow.Application.Common;

namespace HouseFlow.UnitTests.Common;

/// <summary>
/// La version de politique vit à quatre endroits : la constante backend qui décide si un
/// utilisateur doit ré-accepter, la constante frontend envoyée lors de l'acceptation, et les
/// historiques de version des pages légales. Si la première et la deuxième divergent, le
/// backend rejette chaque acceptation (400) et la bannière devient <b>impossible à valider</b> :
/// l'utilisateur la voit indéfiniment. Aucun test ne verrouillait cet invariant.
/// </summary>
public class PolicyVersionConsistencyTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HouseFlow.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("les tests doivent tourner depuis un checkout du dépôt");
        return dir!.FullName;
    }

    private static string ReadRepoFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void FrontendPolicyVersion_MatchesBackendPolicyVersion()
    {
        var source = ReadRepoFile("src", "HouseFlow.Web", "LegalConstants.cs");

        var match = Regex.Match(source, @"PolicyVersion\s*=\s*""(?<version>[^""]+)""");

        match.Success.Should().BeTrue("LegalConstants.PolicyVersion doit rester une constante littérale");
        match.Groups["version"].Value.Should().Be(
            GdprPolicy.CurrentPolicyVersion,
            "une divergence fait rejeter toute acceptation des CGU et rend la bannière de ré-acceptation indéfiniment bloquante");
    }

    [Theory]
    [InlineData("PrivacyContentFr.razor")]
    [InlineData("PrivacyContentEn.razor")]
    [InlineData("TermsContentFr.razor")]
    [InlineData("TermsContentEn.razor")]
    public void LegalPage_DocumentsTheCurrentPolicyVersion(string fileName)
    {
        var content = ReadRepoFile("src", "HouseFlow.Web", "Features", "Legal", fileName);

        content.Should().Contain(
            $"Version {GdprPolicy.CurrentPolicyVersion}",
            "l'historique de version de la page doit citer la version en vigueur, sinon l'utilisateur ré-accepte un texte qui ne dit pas ce qui a changé");
    }
}
