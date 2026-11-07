using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// The identity provider packs silence no rule (#308). The keycloak and generic-oidc packs once wrapped their local
/// Keycloak in <c>#pragma warning disable</c> for the preview Aspire Keycloak package's diagnostics, and the base
/// pack's agent rules carried that one exception. Their AppHosts now run Keycloak as a plain container, so neither
/// the suppression nor the exception is generated, and a template that adds a suppression back fails here.
/// </summary>
public sealed class IdentityProviderSuppressionTests
{
    public static TheoryData<string> KeycloakProviders => ["keycloak", "generic-oidc"];

    [Fact]
    public void No_auth_pack_template_silences_a_rule_with_a_pragma()
    {
        var auth = Path.Combine(Repository.Root, "packs", "backend", "auth");

        var suppressions = Directory.EnumerateFiles(auth, "*", SearchOption.AllDirectories)
            .Where(file => file.Split(Path.DirectorySeparatorChar).Contains("templates"))
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => (File: file, Number: index + 1, Line: line)))
            .Where(entry => entry.Line.Contains("#pragma warning disable", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetRelativePath(Repository.Root, entry.File)}:{entry.Number}: {entry.Line.Trim()}")
            .ToList();

        suppressions.ShouldBeEmpty(string.Join(Environment.NewLine, suppressions));
    }

    [Theory]
    [MemberData(nameof(KeycloakProviders))]
    public void The_apphost_runs_keycloak_as_a_plain_container_without_the_preview_package(string provider)
    {
        var files = Files(provider);
        var appHost = files["src/AppHost/AppHost.cs"];

        appHost.ShouldNotContain("#pragma");
        appHost.ShouldNotContain("AddKeycloak");
        appHost.ShouldContain("AddContainer(");
        appHost.ShouldContain("quay.io/keycloak/keycloak");
        appHost.ShouldContain("KC_BOOTSTRAP_ADMIN_PASSWORD");
        appHost.ShouldContain("/health/ready");
        appHost.ShouldContain("/opt/keycloak/data/import");
        files["src/AppHost/AppHost.csproj"].ShouldNotContain("Aspire.Hosting.Keycloak");
        files["Directory.Packages.props"].ShouldNotContain("Aspire.Hosting.Keycloak");
    }

    [Theory]
    [MemberData(nameof(KeycloakProviders))]
    public void Neither_the_agent_rules_nor_the_decision_records_carry_an_exception_for_the_preview_diagnostics(string provider)
    {
        var files = Files(provider);
        var agents = files["AGENTS.md"];
        var adr = files.Single(file => file.Key.EndsWith($"-backend-auth-{provider}.md", StringComparison.Ordinal)).Value;

        agents.ShouldContain("Never silence a rule with `#pragma`");
        agents.ShouldNotContain("ASPIREHOSTINGKEYCLOAK001");
        agents.ShouldNotContain("#308");
        adr.ShouldNotContain("ASPIREHOSTINGKEYCLOAK001");
        adr.ShouldNotContain("preview");
    }

    private static Dictionary<string, string> Files(string provider)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", "saas-standard-react", [$"backend.auth={provider}"]);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}
