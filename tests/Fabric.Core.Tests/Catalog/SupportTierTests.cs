using Fabric.Core.Catalog;
using Fabric.Core.Packs;

namespace Fabric.Core.Tests.Catalog;

/// <summary>
/// ADR-0003: nothing is supported that the matrix does not build. An option without a pack builds nothing, and a
/// <c>supported</c> option is in a profile or in <c>build/matrix.json</c>, the two layers that build it.
/// </summary>
public class SupportTierTests
{
    [Fact]
    public void Every_supported_option_of_the_repository_catalog_has_a_pack()
    {
        var catalog = CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));
        var packs = PackLoader.Load(RepositoryRoot.File("packs"), catalog).Packs;

        var supportedWithoutPack = catalog.Dimensions.Values
            .SelectMany(dimension => dimension.Options.Values
                .Where(option => option.Tier == SupportTier.Supported && !packs.ContainsKey($"{dimension.Id}.{option.Id}"))
                .Select(option => $"{dimension.Id}={option.Id}"))
            .ToList();

        supportedWithoutPack.ShouldBeEmpty(
            $"these options are tier: supported but have no pack under packs/; mark them experimental until their pack lands: {string.Join(", ", supportedWithoutPack)}");
    }

    [Fact]
    public void Every_supported_option_of_the_repository_catalog_is_in_a_profile_or_the_matrix()
    {
        var catalog = CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));
        var matrix = File.ReadAllText(RepositoryRoot.File("build", "matrix.json"));

        var unbuilt = SupportTierCheck.Unbuilt(catalog, matrix);

        unbuilt.ShouldBeEmpty(
            $"these options are tier: supported but in no profile and no combination of build/matrix.json, so nothing builds them; " +
            $"regenerate the matrix with fabric matrix, or mark them experimental: {string.Join(", ", unbuilt)}");
    }

    /// <summary>
    /// ADR-0003 (amended 2026-11-06): an option is promoted once every matrix combination containing it is green in the
    /// same nightly run. Nightly run 42800894655 (main 72cd016, 2026-11-10) passed 51 of 52 combinations; these are the
    /// experimental options with a pack that are in no combination but the failing one, pairwise-48, and whose pack is
    /// unchanged on main since the run.
    /// </summary>
    [Theory]
    [InlineData("backend.database", "sqlserver")]
    [InlineData("backend.bus.transport", "azure-service-bus")]
    [InlineData("backend.mediator", "wolverine")]
    [InlineData("backend.auth", "entra-id")]
    [InlineData("backend.auth", "generic-oidc")]
    [InlineData("backend.llm", "anthropic")]
    [InlineData("backend.llm", "azure-openai")]
    [InlineData("backend.llm", "ollama")]
    [InlineData("backend.llm.agents", "none")]
    [InlineData("backend.llm.embeddings", "openai")]
    [InlineData("backend.llm.embeddings", "ollama")]
    [InlineData("module.event-store", "kurrentdb")]
    [InlineData("backend.test.framework", "nunit")]
    [InlineData("backend.test.framework", "tunit")]
    [InlineData("backend.test.assertions", "awesome-assertions")]
    [InlineData("backend.test.integration", "testcontainers")]
    [InlineData("test.discipline", "test-after")]
    [InlineData("frontend.architecture", "micro-frontends")]
    [InlineData("frontend.mfe-technique", "module-federation")]
    [InlineData("frontend.mfe-mixed-frameworks", "no")]
    [InlineData("frontend.workspace", "nx")]
    [InlineData("frontend.workspace", "turborepo")]
    [InlineData("frontend.state", "zustand-tanstack-query")]
    [InlineData("frontend.state", "pinia")]
    [InlineData("frontend.api-client", "openapi-fetch")]
    [InlineData("frontend.forms", "vee-validate")]
    [InlineData("frontend.forms", "formkit")]
    [InlineData("frontend.validation", "valibot")]
    [InlineData("frontend.styling", "css-modules")]
    [InlineData("frontend.ui-library", "radix-headless")]
    [InlineData("frontend.ui-library", "mui")]
    [InlineData("frontend.ui-library", "shadcn-vue")]
    [InlineData("frontend.design-system", "tokens")]
    [InlineData("frontend.design-system", "tokens-and-components")]
    [InlineData("frontend.data-grid", "ag-grid")]
    [InlineData("frontend.charts", "recharts")]
    [InlineData("frontend.charts", "chartjs")]
    [InlineData("frontend.auth-client", "msal")]
    [InlineData("frontend.pwa", "yes")]
    [InlineData("frontend.test.extras", "storybook-tests")]
    [InlineData("backend.data-access.reads", "dapper")]
    public void An_option_every_combination_of_nightly_run_42800894655_passed_with_is_supported(string dimension, string option)
    {
        var catalog = CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));

        catalog.Dimensions[dimension].Options[option].Tier.ShouldBe(SupportTier.Supported);
    }

    /// <summary>
    /// ADR-0003 (amended 2026-11-06), owner decisions on #287: nightly run 42998190693 (main c3aa53b, 2026-11-11, the
    /// first full run on .NET 11) passed 52 of 57 combinations; these are the experimental options with a pack that are in
    /// no failing combination (pairwise-4, -11, -17, -18 and -20) and whose pack is unchanged on main since the run.
    /// </summary>
    [Theory]
    [InlineData("backend.llm.agents", "agent-framework")]
    [InlineData("frontend.state", "pinia-tanstack-query")]
    [InlineData("frontend.ui-library", "reka-ui")]
    [InlineData("frontend.ui-library", "vuetify")]
    [InlineData("frontend.dashboards", "user-editable")]
    [InlineData("frontend.dashboards", "generative")]
    [InlineData("frontend.agent-ui", "none")]
    [InlineData("frontend.agent-ui", "ag-ui")]
    public void An_option_every_combination_of_nightly_run_42998190693_passed_with_is_supported(string dimension, string option)
    {
        var catalog = CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));

        catalog.Dimensions[dimension].Options[option].Tier.ShouldBe(SupportTier.Supported);
    }

    /// <summary>
    /// Owner decision on #240: these also passed every combination of nightly run 42800894655, but their packs changed on
    /// main after it, so the run did not build what main generates; they stay experimental until a run that does.
    /// generative and ag-ui left this list when nightly run 42998190693 promoted them.
    /// </summary>
    [Theory]
    [InlineData("backend.auth", "none")]
    [InlineData("backend.tenancy", "schema-per-tenant")]
    [InlineData("frontend.design-system", "web-components")]
    [InlineData("frontend.realtime", "signalr")]
    [InlineData("frontend.mfe-technique", "native-federation")]
    [InlineData("frontend.realtime", "sse")]
    [InlineData("frontend.routing", "tanstack-router")]
    [InlineData("frontend.styling", "library-theme")]
    public void An_option_whose_pack_changed_after_nightly_run_42800894655_stays_experimental(string dimension, string option)
    {
        var catalog = CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));

        catalog.Dimensions[dimension].Options[option].Tier.ShouldBe(SupportTier.Experimental);
    }

    [Fact]
    public void A_supported_option_in_neither_a_profile_nor_the_matrix_is_named_and_nothing_else_is()
    {
        // x.a=q only in a profile, x.a=p and x.b=p by default in that profile; x.b=q and x.m=two only in the matrix,
        // x.b=q as one value of a multi-valued choice. x.b=s is supported and in neither; x.b=r is experimental.
        var catalog = CatalogReader.Read(
            new StringReader("""
                version: 1
                profiles:
                  only-profile:
                    x.a: q
                dimensions:
                  x.a:
                    scope: project
                    default: p
                    options:
                      p: { tier: supported }
                      q: { tier: supported }
                  x.b:
                    scope: project
                    multi: true
                    default: [p]
                    options:
                      p: { tier: supported }
                      q: { tier: supported }
                      r: { tier: experimental }
                      s: { tier: supported }
                  x.m:
                    scope: module
                    default: one
                    options:
                      one: { tier: supported }
                      two: { tier: supported }
                """),
            "inline");
        const string Matrix = """
            {
              "combinations": [
                { "name": "pairwise-1", "choices": { "x.a": "p", "x.b": ["p", "q"], "x.m": "two" } }
              ],
              "infeasiblePairs": []
            }
            """;

        SupportTierCheck.Unbuilt(catalog, Matrix).ShouldBe(["x.b=s"]);
    }
}
