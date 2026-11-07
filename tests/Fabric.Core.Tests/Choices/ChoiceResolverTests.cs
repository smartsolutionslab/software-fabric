using Fabric.Core.Catalog;
using Fabric.Core.Choices;

namespace Fabric.Core.Tests.Choices;

public class ChoiceResolverTests
{
    private static readonly CatalogModel RepositoryCatalog = CatalogReader.ReadFile(RepositoryRoot.File("catalog", "catalog.yaml"));

    private static ResolvedChoices ResolveSuccessfully(string? profile, params string[] overrides)
    {
        var resolution = ChoiceResolver.Resolve(RepositoryCatalog, profile, overrides);
        resolution.Errors.ShouldBeEmpty();
        return resolution.Choices.ShouldNotBeNull();
    }

    private static string SingleError(CatalogModel catalog, string? profile, params string[] overrides)
    {
        var resolution = ChoiceResolver.Resolve(catalog, profile, overrides);
        resolution.Choices.ShouldBeNull();
        return resolution.Errors.ShouldHaveSingleItem();
    }

    private static CatalogModel Catalog(string yaml) => CatalogReader.Read(new StringReader(yaml), "test.yaml");

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    [InlineData("industrial-event-driven-mfe")]
    [InlineData("lean-crud")]
    public void Every_profile_resolves(string profile)
    {
        ResolveSuccessfully(profile).Project.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_profile_value_wins_over_the_default()
    {
        ResolveSuccessfully("saas-standard-react").Project["backend.tenancy"].ShouldBe(["column"]);
    }

    [Fact]
    public void An_unset_active_dimension_takes_its_default()
    {
        var choices = ResolveSuccessfully("saas-standard-react");

        choices.Project["backend.database"].ShouldBe(["postgres"]);
        choices.Project["backend.bus.library"].ShouldBe(["wolverine"]);
        choices.ModuleDefaults["module.ddd-depth"].ShouldBe(["light"]);
    }

    [Fact]
    public void An_inactive_dimension_is_left_out()
    {
        var choices = ResolveSuccessfully("lean-crud");

        choices.Project.ShouldNotContainKey("backend.bus.library");
        choices.Project.ShouldNotContainKey("frontend.mfe-technique");
        choices.ModuleDefaults.ShouldNotContainKey("module.ddd-depth");
        choices.ModuleDefaults.ShouldNotContainKey("module.event-store");
    }

    [Fact]
    public void Project_and_module_dimensions_are_kept_apart()
    {
        var choices = ResolveSuccessfully("lean-crud");

        choices.Project.Keys.ShouldAllBe(dimension => !dimension.StartsWith("module.", StringComparison.Ordinal));
        choices.ModuleDefaults.Keys.ShouldBe(["module.style", "module.cqrs", "module.communication"]);
    }

    [Fact]
    public void A_child_profile_inherits_and_overrides_its_parent()
    {
        var choices = ResolveSuccessfully("saas-standard-vue");

        choices.Project["frontend.framework"].ShouldBe(["vue"]);
        choices.Project["backend.tenancy"].ShouldBe(["column"]);
        choices.Project["backend.bus.transport"].ShouldBe(["rabbitmq"]);
    }

    [Fact]
    public void The_vue_profile_forms_with_vee_validate_and_composes_shadcn_vue_styled_with_tailwind()
    {
        var choices = ResolveSuccessfully("saas-standard-vue");

        choices.Project["frontend.forms"].ShouldBe(["vee-validate"]);
        choices.Project["frontend.ui-library"].ShouldBe(["shadcn-vue"]);
        choices.Project["frontend.styling"].ShouldBe(["tailwind"]);
    }

    [Fact]
    public void The_ai_profile_adds_openai_agents_rag_evals_the_agent_panel_and_dashboards_to_the_standard_react_profile()
    {
        var choices = ResolveSuccessfully("saas-ai-react");
        var standard = ResolveSuccessfully("saas-standard-react");

        choices.Project["backend.llm"].ShouldBe(["openai"]);
        choices.Project["backend.llm.agents"].ShouldBe(["agent-framework"]);
        choices.Project["backend.llm.extras"].ShouldBe(["rag", "evals"]);
        choices.Project["backend.llm.embeddings"].ShouldBe(["openai"], "the embeddings follow backend.llm (default-from).");
        choices.Project["frontend.agent-ui"].ShouldBe(["ag-ui"]);
        choices.Project["frontend.dashboards"].ShouldBe(["user-editable", "generative"]);
        choices.Settings["backend.llm.extras.evals.threshold"].ShouldBe(4m);
        foreach (var (dimension, options) in standard.Project.Where(choice => choice.Key is not ("backend.llm" or "frontend.dashboards")))
        {
            choices.Project[dimension].ShouldBe(options, dimension);
        }
    }

    [Fact]
    public void Overrides_win_over_the_profile_and_activate_dependent_dimensions()
    {
        var choices = ResolveSuccessfully("lean-crud", "backend.bus.transport=kafka");

        choices.Project["backend.bus.transport"].ShouldBe(["kafka"]);
        choices.Project["backend.bus.library"].ShouldBe(["wolverine"]);
    }

    [Fact]
    public void A_multi_dimension_override_takes_a_comma_separated_set()
    {
        ResolveSuccessfully(null, "backend.api-protocols=rest,grpc").Project["backend.api-protocols"].ShouldBe(["rest", "grpc"]);
    }

    [Fact]
    public void Without_a_profile_every_active_dimension_takes_its_default()
    {
        var choices = ResolveSuccessfully(null);

        choices.Project["frontend.framework"].ShouldBe(["react"]);
        choices.Project["backend.test.extras"].ShouldBeEmpty();
        choices.Project.ShouldNotContainKey("backend.bus.library");
    }

    [Fact]
    public void Without_authentication_authorization_and_the_browser_sign_in_are_inactive()
    {
        var choices = ResolveSuccessfully("saas-standard-react", "backend.auth=none");

        choices.Project["backend.auth"].ShouldBe(["none"]);
        choices.Project.ShouldNotContainKey("backend.authorization");
        choices.Project.ShouldNotContainKey("frontend.auth-client");
    }

    [Fact]
    public void With_authentication_authorization_defaults_to_signed_in_only()
    {
        var choices = ResolveSuccessfully("saas-standard-react");

        choices.Project["backend.authorization"].ShouldBeEmpty();
        choices.Project["frontend.auth-client"].ShouldBe(["oidc-client-ts"]);
    }

    [Theory]
    [InlineData("backend.authorization=", new string[0])]
    [InlineData("backend.authorization=roles", new[] { "roles" })]
    [InlineData("backend.authorization=claims", new[] { "claims" })]
    [InlineData("backend.authorization=roles,claims", new[] { "roles", "claims" })]
    public void Authorization_takes_any_set_of_roles_and_claims(string setting, string[] expected)
    {
        ResolveSuccessfully("saas-standard-react", setting).Project["backend.authorization"].ShouldBe(expected);
    }

    [Fact]
    public void Rejects_authorization_without_authentication_and_says_why()
    {
        SingleError(RepositoryCatalog, "saas-standard-react", "backend.auth=none", "backend.authorization=roles").ShouldBe(
            "backend.authorization is set to roles, but it only applies when backend.auth != none, and backend.auth is none.");
    }

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    [InlineData("industrial-event-driven-mfe")]
    [InlineData("lean-crud")]
    public void Every_profile_uses_no_llm_and_no_mcp(string profile)
    {
        var choices = ResolveSuccessfully(profile);

        choices.Project["backend.llm"].ShouldBe(["none"]);
        choices.Project["backend.mcp"].ShouldBeEmpty();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("anthropic")]
    [InlineData("openai")]
    [InlineData("azure-openai")]
    [InlineData("ollama")]
    public void The_llm_takes_each_provider(string provider)
    {
        ResolveSuccessfully("saas-standard-react", $"backend.llm={provider}").Project["backend.llm"].ShouldBe([provider]);
    }

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    [InlineData("industrial-event-driven-mfe")]
    [InlineData("lean-crud")]
    [InlineData("saas-strict-react")]
    public void No_profile_has_agents_because_none_calls_a_model(string profile)
    {
        ResolveSuccessfully(profile).Project.ShouldNotContainKey("backend.llm.agents");
    }

    [Fact]
    public void A_project_that_calls_a_model_has_no_agents_unless_it_chooses_them()
    {
        ResolveSuccessfully("lean-crud", "backend.llm=anthropic").Project["backend.llm.agents"].ShouldBe(["none"]);
    }

    [Theory]
    [InlineData("anthropic")]
    [InlineData("openai")]
    [InlineData("azure-openai")]
    [InlineData("ollama")]
    public void Agent_framework_takes_each_provider(string provider)
    {
        ResolveSuccessfully("lean-crud", $"backend.llm={provider}", "backend.llm.agents=agent-framework").Project["backend.llm.agents"].ShouldBe(["agent-framework"]);
    }

    [Fact]
    public void Rejects_agents_without_a_model_and_says_why()
    {
        SingleError(RepositoryCatalog, "lean-crud", "backend.llm.agents=agent-framework").ShouldBe(
            "backend.llm.agents is set to agent-framework, but it only applies when backend.llm != none, and backend.llm is none.");
    }

    [Fact]
    public void Rejects_two_llm_providers_in_one_project()
    {
        SingleError(RepositoryCatalog, "saas-standard-react", "backend.llm=anthropic,openai").ShouldBe(
            "backend.llm takes one option, got 2: anthropic, openai.");
    }

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    [InlineData("industrial-event-driven-mfe")]
    [InlineData("lean-crud")]
    [InlineData("saas-strict-react")]
    public void No_profile_has_an_agent_ui_because_none_has_agents(string profile)
    {
        ResolveSuccessfully(profile).Project.ShouldNotContainKey("frontend.agent-ui");
    }

    [Fact]
    public void A_project_with_agents_has_no_agent_ui_unless_it_chooses_one()
    {
        ResolveSuccessfully("lean-crud", "backend.llm=anthropic", "backend.llm.agents=agent-framework")
            .Project["frontend.agent-ui"].ShouldBe(["none"]);
    }

    [Fact]
    public void A_project_with_agents_can_choose_the_ag_ui_panel()
    {
        ResolveSuccessfully("lean-crud", "backend.llm=anthropic", "backend.llm.agents=agent-framework", "frontend.agent-ui=ag-ui")
            .Project["frontend.agent-ui"].ShouldBe(["ag-ui"]);
    }

    [Fact]
    public void A_project_that_calls_a_model_without_agents_has_no_agent_ui()
    {
        ResolveSuccessfully("lean-crud", "backend.llm=anthropic").Project.ShouldNotContainKey("frontend.agent-ui");
    }

    [Fact]
    public void Rejects_an_agent_ui_without_agents_and_says_why()
    {
        SingleError(RepositoryCatalog, "lean-crud", "backend.llm=anthropic", "frontend.agent-ui=ag-ui").ShouldBe(
            "frontend.agent-ui is set to ag-ui, but it only applies when backend.llm.agents == agent-framework, and backend.llm.agents is none.");
    }

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    [InlineData("industrial-event-driven-mfe")]
    [InlineData("lean-crud")]
    [InlineData("saas-strict-react")]
    public void No_profile_has_llm_extras_or_embeddings_because_none_calls_a_model(string profile)
    {
        var choices = ResolveSuccessfully(profile);

        choices.Project.ShouldNotContainKey("backend.llm.extras");
        choices.Project.ShouldNotContainKey("backend.llm.embeddings");
    }

    [Fact]
    public void A_project_that_calls_a_model_has_no_extras_and_no_embeddings_unless_it_chooses_rag()
    {
        var choices = ResolveSuccessfully("lean-crud", "backend.llm=openai");

        choices.Project["backend.llm.extras"].ShouldBeEmpty();
        choices.Project.ShouldNotContainKey("backend.llm.embeddings");
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("azure-openai")]
    [InlineData("ollama")]
    public void Embeddings_default_to_the_llm_provider_when_it_has_embeddings(string provider)
    {
        var choices = ResolveSuccessfully("lean-crud", $"backend.llm={provider}", "backend.llm.extras=rag");

        choices.Project["backend.llm.extras"].ShouldBe(["rag"]);
        choices.Project["backend.llm.embeddings"].ShouldBe([provider]);
    }

    [Fact]
    public void Chosen_embeddings_win_over_the_llm_provider()
    {
        ResolveSuccessfully("lean-crud", "backend.llm=openai", "backend.llm.extras=rag", "backend.llm.embeddings=ollama")
            .Project["backend.llm.embeddings"].ShouldBe(["ollama"]);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("azure-openai")]
    [InlineData("ollama")]
    public void Anthropic_takes_each_embeddings_provider_chosen_explicitly(string embeddings)
    {
        ResolveSuccessfully("lean-crud", "backend.llm=anthropic", "backend.llm.extras=rag", $"backend.llm.embeddings={embeddings}")
            .Project["backend.llm.embeddings"].ShouldBe([embeddings]);
    }

    [Fact]
    public void Rejects_rag_with_anthropic_without_embeddings_and_says_why()
    {
        SingleError(RepositoryCatalog, "lean-crud", "backend.llm=anthropic", "backend.llm.extras=rag").ShouldBe(
            "backend.llm.embeddings has no value: its default follows backend.llm, and anthropic is not one of its options " +
            "(openai, azure-openai, ollama); set it with backend.llm.embeddings=<option>.");
    }

    [Fact]
    public void Rejects_extras_without_a_model_and_says_why()
    {
        SingleError(RepositoryCatalog, "lean-crud", "backend.llm.extras=rag").ShouldBe(
            "backend.llm.extras is set to rag, but it only applies when backend.llm != none, and backend.llm is none.");
    }

    [Fact]
    public void Rejects_embeddings_without_rag_and_says_why()
    {
        SingleError(RepositoryCatalog, "lean-crud", "backend.llm=openai", "backend.llm.embeddings=openai").ShouldBe(
            "backend.llm.embeddings is set to openai, but it only applies when backend.llm.extras has rag, and backend.llm.extras is empty.");
    }

    [Fact]
    public void Evals_brings_its_threshold_at_the_default()
    {
        ResolveSuccessfully("lean-crud", "backend.llm=openai", "backend.llm.extras=evals")
            .Settings.ShouldBe(new Dictionary<string, decimal> { ["backend.llm.extras.evals.threshold"] = 4m });
    }

    [Fact]
    public void Evals_takes_a_threshold_set_with_set()
    {
        ResolveSuccessfully("lean-crud", "backend.llm=openai", "backend.llm.extras=rag,evals", "backend.llm.extras.evals.threshold=3.5")
            .Settings["backend.llm.extras.evals.threshold"].ShouldBe(3.5m);
    }

    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("lean-crud")]
    public void A_project_without_an_option_that_declares_settings_has_none(string profile)
    {
        ResolveSuccessfully(profile).Settings.ShouldBeEmpty();
        ResolveSuccessfully(profile, "backend.llm=openai", "backend.llm.extras=rag").Settings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("0.5")]
    [InlineData("6")]
    public void Rejects_a_threshold_outside_its_range_and_says_which(string value)
    {
        SingleError(RepositoryCatalog, "lean-crud", "backend.llm=openai", "backend.llm.extras=evals", $"backend.llm.extras.evals.threshold={value}").ShouldBe(
            $"backend.llm.extras.evals.threshold is {value}, but it must be between 1 and 5.");
    }

    [Theory]
    [InlineData("high")]
    [InlineData("3,4")]
    [InlineData("")]
    public void Rejects_a_threshold_that_is_not_a_number(string value)
    {
        SingleError(RepositoryCatalog, "lean-crud", "backend.llm=openai", "backend.llm.extras=evals", $"backend.llm.extras.evals.threshold={value}").ShouldBe(
            $"backend.llm.extras.evals.threshold must be a number between 1 and 5, got '{value}'.");
    }

    [Fact]
    public void Rejects_a_threshold_without_evals_and_says_why()
    {
        SingleError(RepositoryCatalog, "lean-crud", "backend.llm=openai", "backend.llm.extras.evals.threshold=3").ShouldBe(
            "backend.llm.extras.evals.threshold is set to 3, but it only applies when backend.llm.extras has evals, and backend.llm.extras is empty.");
    }

    [Fact]
    public void Rejects_a_threshold_without_a_model_and_says_why()
    {
        SingleError(RepositoryCatalog, "lean-crud", "backend.llm.extras.evals.threshold=3").ShouldBe(
            "backend.llm.extras.evals.threshold is set to 3, but it only applies when backend.llm.extras has evals, and backend.llm.extras is not set.");
    }

    [Fact]
    public void Rejects_a_setting_the_option_does_not_declare_and_names_those_it_does()
    {
        SingleError(RepositoryCatalog, "lean-crud", "backend.llm=openai", "backend.llm.extras=evals", "backend.llm.extras.evals.treshold=3").ShouldBe(
            "unknown setting 'treshold' of backend.llm.extras=evals; its settings: threshold.");
        SingleError(RepositoryCatalog, "lean-crud", "backend.llm=openai", "backend.llm.extras=rag", "backend.llm.extras.rag.threshold=3").ShouldBe(
            "unknown setting 'threshold' of backend.llm.extras=rag; it has no settings.");
    }

    [Fact]
    public void A_key_that_is_neither_a_dimension_nor_a_setting_is_an_unknown_dimension()
    {
        SingleError(RepositoryCatalog, "lean-crud", "backend.llm.extra.evals.threshold=3").ShouldBe(
            "unknown dimension 'backend.llm.extra.evals.threshold'.");
    }

    [Theory]
    [InlineData("backend.mcp=", new string[0])]
    [InlineData("backend.mcp=server", new[] { "server" })]
    [InlineData("backend.mcp=client", new[] { "client" })]
    [InlineData("backend.mcp=server,client", new[] { "server", "client" })]
    public void Mcp_takes_any_set_of_server_and_client(string setting, string[] expected)
    {
        ResolveSuccessfully("saas-standard-react", setting).Project["backend.mcp"].ShouldBe(expected);
    }

    [Fact]
    public void Values_follow_catalog_order()
    {
        var choices = ResolveSuccessfully("saas-standard-vue");

        choices.Project.Keys.First().ShouldBe("backend.database");
        choices.Project.Keys.Last().ShouldBe("frontend.test.extras");
    }

    [Fact]
    public void Rejects_an_unknown_profile()
    {
        SingleError(RepositoryCatalog, "enterprise").ShouldBe("unknown profile 'enterprise'.");
    }

    [Theory]
    [InlineData("frontend.colour=red", "unknown dimension 'frontend.colour'.")]
    [InlineData("frontend.framework=svelte", "'svelte' is not an option of frontend.framework; options: react, vue.")]
    [InlineData("frontend.framework=react,vue", "frontend.framework takes one option, got 2: react, vue.")]
    [InlineData("frontend.framework", "override 'frontend.framework' must look like <dimension>=<option>.")]
    public void Rejects_an_invalid_override(string setting, string error)
    {
        SingleError(RepositoryCatalog, null, setting).ShouldBe(error);
    }

    [Fact]
    public void Rejects_setting_an_inactive_dimension_and_says_why()
    {
        SingleError(RepositoryCatalog, "lean-crud", "frontend.mfe-mixed-frameworks=yes").ShouldBe(
            "frontend.mfe-mixed-frameworks is set to yes, but it only applies when frontend.architecture == micro-frontends, and frontend.architecture is spa.");
    }

    [Fact]
    public void Rejects_a_profile_that_extends_itself()
    {
        var catalog = Catalog("""
            version: 1
            profiles:
              a: { extends: b }
              b: { extends: a }
            dimensions:
              x.y: { scope: project, default: o, options: { o: { tier: supported } } }
            """);

        SingleError(catalog, "a").ShouldBe("profile 'a' extends itself: a -> b -> a.");
    }

    [Fact]
    public void Rejects_a_profile_that_extends_an_unknown_profile()
    {
        var catalog = Catalog("""
            version: 1
            profiles:
              a: { extends: missing }
            dimensions:
              x.y: { scope: project, default: o, options: { o: { tier: supported } } }
            """);

        SingleError(catalog, "a").ShouldBe("profile 'a' extends unknown profile 'missing'.");
    }

    [Fact]
    public void Rejects_an_active_dimension_without_value_or_default()
    {
        var catalog = Catalog("""
            version: 1
            dimensions:
              x.y: { scope: project, options: { o: { tier: supported } } }
            """);

        SingleError(catalog, null).ShouldBe("x.y has no value and no default; set it with x.y=<option>.");
    }

    [Fact]
    public void A_default_from_takes_the_followed_dimensions_default()
    {
        var catalog = Catalog("""
            version: 1
            dimensions:
              b.b: { scope: project, default-from: a.a, options: { p: { tier: supported }, q: { tier: supported } } }
              a.a: { scope: project, default: q, options: { p: { tier: supported }, q: { tier: supported } } }
            """);

        var resolution = ChoiceResolver.Resolve(catalog, null, []);

        resolution.Errors.ShouldBeEmpty();
        resolution.Choices.ShouldNotBeNull().Project["b.b"].ShouldBe(["q"]);
    }

    [Fact]
    public void A_default_from_follows_a_chosen_value()
    {
        var catalog = Catalog("""
            version: 1
            dimensions:
              b.b: { scope: project, default-from: a.a, options: { p: { tier: supported }, q: { tier: supported } } }
              a.a: { scope: project, default: q, options: { p: { tier: supported }, q: { tier: supported } } }
            """);

        var resolution = ChoiceResolver.Resolve(catalog, null, ["a.a=p"]);

        resolution.Errors.ShouldBeEmpty();
        resolution.Choices.ShouldNotBeNull().Project["b.b"].ShouldBe(["p"]);
    }

    [Fact]
    public void A_default_from_an_inactive_dimension_gives_no_default_and_says_so()
    {
        var catalog = Catalog("""
            version: 1
            dimensions:
              b.b: { scope: project, default-from: a.a, options: { p: { tier: supported } } }
              a.a: { scope: project, active-when: "c.c == on", default: p, options: { p: { tier: supported } } }
              c.c: { scope: project, default: "off", options: { "on": { tier: supported }, "off": { tier: supported } } }
            """);

        SingleError(catalog, null).ShouldBe(
            "b.b has no value: its default follows a.a, and a.a is not set; set it with b.b=<option>.");
    }

    [Fact]
    public void A_default_is_dropped_when_another_default_deactivates_its_dimension()
    {
        var catalog = Catalog("""
            version: 1
            dimensions:
              b.b:
                scope: project
                active-when: "a.a != off"
                default: y
                options: { y: { tier: supported } }
              a.a:
                scope: project
                default: "off"
                options: { "on": { tier: supported }, "off": { tier: supported } }
            """);

        var resolution = ChoiceResolver.Resolve(catalog, null, []);

        resolution.Errors.ShouldBeEmpty();
        resolution.Choices.ShouldNotBeNull().Project.Keys.ShouldBe(["a.a"]);
    }
}
