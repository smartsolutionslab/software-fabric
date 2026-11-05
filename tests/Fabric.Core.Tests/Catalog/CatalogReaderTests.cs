using Fabric.Core.Catalog;

namespace Fabric.Core.Tests.Catalog;

public class CatalogReaderTests
{
    private static CatalogModel ReadRepositoryCatalog() =>
        CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));

    private static CatalogModel Read(string yaml) =>
        CatalogReader.Read(new StringReader(yaml), "test.yaml");

    [Fact]
    public void Reads_every_dimension_option_and_profile_of_the_repository_catalog()
    {
        var catalog = ReadRepositoryCatalog();

        catalog.Version.ShouldBe(1);
        catalog.Dimensions.Count.ShouldBe(51);
        catalog.Dimensions.Values.Sum(dimension => dimension.Options.Count).ShouldBe(140);
        catalog.Profiles.Keys.ShouldBe(
            ["saas-standard-react", "saas-standard-vue", "industrial-event-driven-mfe", "lean-crud", "saas-strict-react"],
            ignoreOrder: false);
    }

    [Fact]
    public void Reads_the_role_slots_a_dimension_requires_of_its_options()
    {
        var catalog = Read("""
            version: 1
            dimensions:
              frontend.framework:
                scope: project
                role-slots:
                  web.dependencies: { description: "Entries of the app's dependencies.", separator: ",\n" }
                  web.imports: { description: "Import lines of the entry point." }
                options: { react: { tier: supported } }
            """);

        var roles = catalog.Dimensions["frontend.framework"].RoleSlots;
        roles.Keys.ShouldBe(["web.dependencies", "web.imports"], ignoreOrder: false);
        roles["web.dependencies"].ShouldBe(new RoleSlot("web.dependencies", "Entries of the app's dependencies.", ",\n"));
        roles["web.imports"].Separator.ShouldBeNull();
        Read("version: 1\ndimensions:\n  a.b: { scope: project, options: { c: { tier: supported } } }\n").Dimensions["a.b"].RoleSlots.ShouldBeEmpty();
    }

    [Fact]
    public void Keeps_dimensions_in_file_order()
    {
        var catalog = ReadRepositoryCatalog();

        catalog.Dimensions.Keys.First().ShouldBe("backend.database");
        catalog.Dimensions.Keys.Last().ShouldBe("frontend.test.extras");
    }

    [Fact]
    public void Reads_scope_multi_active_when_and_default()
    {
        var catalog = ReadRepositoryCatalog();

        var busLibrary = catalog.Dimensions["backend.bus.library"];
        busLibrary.Scope.ShouldBe(DimensionScope.Project);
        busLibrary.Multi.ShouldBeFalse();
        busLibrary.ActiveWhen?.ToString().ShouldBe("backend.bus.transport != none");
        busLibrary.Default.ShouldBe(["wolverine"]);

        var protocols = catalog.Dimensions["backend.api-protocols"];
        protocols.Multi.ShouldBeTrue();
        protocols.Default.ShouldBe(["rest"]);

        catalog.Dimensions["module.style"].Scope.ShouldBe(DimensionScope.Module);
        catalog.Dimensions["backend.test.extras"].Default.ShouldBe([]);
    }

    [Fact]
    public void Reads_option_tier_requires_and_conflicts()
    {
        var catalog = ReadRepositoryCatalog();
        var uiLibraries = catalog.Dimensions["frontend.ui-library"].Options;

        uiLibraries["shadcn"].Tier.ShouldBe(SupportTier.Supported);
        uiLibraries["shadcn"].Requires.Select(condition => condition.ToString()).ShouldBe(["frontend.framework == react", "frontend.styling == tailwind"]);
        uiLibraries["mui"].Conflicts.Select(condition => condition.ToString()).ShouldBe(["frontend.styling == tailwind"]);
        uiLibraries["reka-ui"].Requires.Select(condition => condition.ToString()).ShouldBe(["frontend.framework == vue", "frontend.styling == tailwind"]);
        uiLibraries.ShouldNotContainKey("primevue");
        catalog.Dimensions["module.event-store"].Options["kurrentdb"].Tier.ShouldBe(SupportTier.Experimental);
    }

    [Theory]
    [InlineData("backend.auth", "none")]
    [InlineData("backend.authorization", "roles")]
    [InlineData("backend.authorization", "claims")]
    public void An_authentication_or_authorization_option_without_a_pack_is_experimental(string dimension, string option)
    {
        ReadRepositoryCatalog().Dimensions[dimension].Options[option].Tier.ShouldBe(SupportTier.Experimental);
    }

    [Theory]
    [InlineData("backend.llm", "anthropic")]
    [InlineData("backend.llm", "openai")]
    [InlineData("backend.llm", "azure-openai")]
    [InlineData("backend.llm", "ollama")]
    [InlineData("backend.mcp", "server")]
    [InlineData("backend.mcp", "client")]
    public void An_llm_or_mcp_option_without_a_pack_is_experimental(string dimension, string option)
    {
        ReadRepositoryCatalog().Dimensions[dimension].Options[option].Tier.ShouldBe(SupportTier.Experimental);
    }

    [Fact]
    public void Agents_are_a_choice_only_for_a_project_that_calls_a_model_and_default_to_none()
    {
        var agents = ReadRepositoryCatalog().Dimensions["backend.llm.agents"];

        agents.ActiveWhen!.ToString().ShouldBe("backend.llm != none");
        agents.Default.ShouldBe(["none"]);
        agents.Options.Keys.ShouldBe(["none", "agent-framework"]);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("agent-framework")]
    public void Every_agents_option_is_experimental_because_no_profile_calls_a_model(string option)
    {
        ReadRepositoryCatalog().Dimensions["backend.llm.agents"].Options[option].Tier.ShouldBe(SupportTier.Experimental);
    }

    [Fact]
    public void Agent_ui_is_a_choice_only_for_a_project_with_agents_and_defaults_to_none()
    {
        var agentUi = ReadRepositoryCatalog().Dimensions["frontend.agent-ui"];

        agentUi.ActiveWhen!.ToString().ShouldBe("backend.llm.agents == agent-framework");
        agentUi.Multi.ShouldBeFalse();
        agentUi.Default.ShouldBe(["none"]);
        agentUi.Options.Keys.ShouldBe(["none", "ag-ui"]);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("ag-ui")]
    public void Every_agent_ui_option_is_experimental_because_no_profile_has_agents(string option)
    {
        ReadRepositoryCatalog().Dimensions["frontend.agent-ui"].Options[option].Tier.ShouldBe(SupportTier.Experimental);
    }

    [Fact]
    public void Extras_are_a_choice_only_for_a_project_that_calls_a_model_and_none_is_chosen_by_default()
    {
        var extras = ReadRepositoryCatalog().Dimensions["backend.llm.extras"];

        extras.ActiveWhen!.ToString().ShouldBe("backend.llm != none");
        extras.Multi.ShouldBeTrue();
        extras.Default.ShouldBe([]);
        extras.Options.Keys.ShouldBe(["rag", "evals"]);
    }

    [Fact]
    public void Rag_requires_postgres()
    {
        ReadRepositoryCatalog().Dimensions["backend.llm.extras"].Options["rag"].Requires
            .Select(condition => condition.ToString())
            .ShouldBe(["backend.database == postgres"]);
    }

    [Fact]
    public void Embeddings_are_a_choice_only_with_rag_and_their_default_follows_the_llm_provider()
    {
        var embeddings = ReadRepositoryCatalog().Dimensions["backend.llm.embeddings"];

        embeddings.ActiveWhen!.ToString().ShouldBe("backend.llm.extras has rag");
        embeddings.Multi.ShouldBeFalse();
        embeddings.Default.ShouldBeNull();
        embeddings.DefaultFrom.ShouldBe("backend.llm");
        embeddings.Options.Keys.ShouldBe(["openai", "azure-openai", "ollama"]);
    }

    [Theory]
    [InlineData("backend.llm.extras", "rag")]
    [InlineData("backend.llm.embeddings", "openai")]
    [InlineData("backend.llm.embeddings", "azure-openai")]
    [InlineData("backend.llm.embeddings", "ollama")]
    public void Every_rag_option_is_experimental_because_it_has_no_pack_yet(string dimension, string option)
    {
        ReadRepositoryCatalog().Dimensions[dimension].Options[option].Tier.ShouldBe(SupportTier.Experimental);
    }

    [Fact]
    public void Evals_is_an_extra_with_a_threshold_from_one_to_five_that_defaults_to_four()
    {
        var evals = ReadRepositoryCatalog().Dimensions["backend.llm.extras"].Options["evals"];

        evals.Tier.ShouldBe(SupportTier.Experimental);
        evals.Requires.ShouldBeEmpty();
        var threshold = evals.Settings.ShouldHaveSingleItem().Value;
        threshold.Id.ShouldBe("threshold");
        threshold.Default.ShouldBe(4m);
        threshold.Minimum.ShouldBe(1m);
        threshold.Maximum.ShouldBe(5m);
    }

    [Fact]
    public void Reads_the_settings_an_option_declares()
    {
        var catalog = Read("""
            version: 1
            dimensions:
              x.y:
                scope: project
                options:
                  o:
                    tier: supported
                    settings:
                      threshold: { default: 3.5, minimum: 1, maximum: 5, note: "lowest mean score" }
                      runs: { default: 2, minimum: 1, maximum: 10 }
                  p: { tier: supported }
            """);

        var options = catalog.Dimensions["x.y"].Options;
        options["o"].Settings.Keys.ShouldBe(["threshold", "runs"], ignoreOrder: false);
        options["o"].Settings["threshold"].ShouldBe(new OptionSetting("threshold", 3.5m, 1m, 5m, "lowest mean score"));
        options["o"].Settings["runs"].Note.ShouldBeNull();
        options["p"].Settings.ShouldBeEmpty();
    }

    [Fact]
    public void Rejects_a_setting_that_is_not_a_number()
    {
        Should.Throw<CatalogException>(() => Read("""
            version: 1
            dimensions:
              x.y: { scope: project, options: { o: { tier: supported, settings: { threshold: { default: high, minimum: 1, maximum: 5 } } } } }
            """)).Message.ShouldBe("test.yaml:3: invalid value 'high' for 'default' on setting 'threshold' of option 'x.y.o'; expected a number.");
    }

    [Fact]
    public void Rejects_a_setting_without_a_range()
    {
        Should.Throw<CatalogException>(() => Read("""
            version: 1
            dimensions:
              x.y: { scope: project, options: { o: { tier: supported, settings: { threshold: { default: 4 } } } } }
            """)).Message.ShouldBe("test.yaml:3: missing field 'minimum' on setting 'threshold' of option 'x.y.o'.");
    }

    [Fact]
    public void A_dimension_without_default_from_follows_nothing()
    {
        ReadRepositoryCatalog().Dimensions["backend.llm"].DefaultFrom.ShouldBeNull();
    }

    [Fact]
    public void No_llm_is_supported_because_every_profile_generates_it()
    {
        ReadRepositoryCatalog().Dimensions["backend.llm"].Options["none"].Tier.ShouldBe(SupportTier.Supported);
    }

    [Fact]
    public void Reads_yes_and_no_as_option_names_not_booleans()
    {
        var catalog = ReadRepositoryCatalog();

        catalog.Dimensions["frontend.pwa"].Options.Keys.ShouldBe(["no", "yes"]);
        catalog.Dimensions["frontend.pwa"].Default.ShouldBe(["no"]);
    }

    [Fact]
    public void Reads_profile_settings_and_extends()
    {
        var catalog = ReadRepositoryCatalog();
        var vue = catalog.Profiles["saas-standard-vue"];

        vue.Extends.ShouldBe("saas-standard-react");
        vue.Settings["frontend.framework"].ShouldBe(["vue"]);
        catalog.Profiles["saas-standard-react"].Extends.ShouldBeNull();
    }

    [Fact]
    public void Rejects_an_unknown_field_on_a_dimension_with_its_line()
    {
        var exception = Should.Throw<CatalogException>(() => Read("""
            version: 1
            dimensions:
              a.b:
                scope: project
                colour: red
                options:
                  x: { tier: supported }
            """));

        exception.Message.ShouldContain("test.yaml:5");
        exception.Message.ShouldContain("'colour'");
    }

    [Fact]
    public void Rejects_an_unknown_field_on_an_option()
    {
        var exception = Should.Throw<CatalogException>(() => Read("""
            version: 1
            dimensions:
              a.b:
                scope: project
                options:
                  x: { tier: supported, needs: ["c.d == e"] }
            """));

        exception.Message.ShouldContain("'needs'");
    }

    [Fact]
    public void Rejects_a_dimension_without_options()
    {
        var exception = Should.Throw<CatalogException>(() => Read("""
            version: 1
            dimensions:
              a.b:
                scope: project
            """));

        exception.Message.ShouldContain("'options'");
    }

    [Theory]
    [InlineData("scope: global", "scope")]
    [InlineData("scope: project\n    multi: maybe", "multi")]
    public void Rejects_an_invalid_dimension_value(string fields, string field)
    {
        var exception = Should.Throw<CatalogException>(() => Read($"""
            version: 1
            dimensions:
              a.b:
                {fields}
                options:
                  x: {"{"} tier: supported {"}"}
            """));

        exception.Message.ShouldContain($"'{field}'");
    }

    [Fact]
    public void Rejects_an_unknown_tier()
    {
        var exception = Should.Throw<CatalogException>(() => Read("""
            version: 1
            dimensions:
              a.b:
                scope: project
                options:
                  x: { tier: beta }
            """));

        exception.Message.ShouldContain("'beta'");
    }

    [Fact]
    public void Rejects_a_missing_version()
    {
        Should.Throw<CatalogException>(() => Read("dimensions: {}"))
            .Message.ShouldContain("'version'");
    }
}
