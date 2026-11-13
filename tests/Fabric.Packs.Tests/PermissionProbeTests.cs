using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// Every module's permission probe, <c>GET /&lt;module&gt;/permissions/manage</c>, is an ordinary endpoint (#609, owner's
/// decision): a <c>CanManage</c> query through the dispatcher answering a typed <c>PermissionDto</c>, described in the
/// OpenAPI document, and called by the module's page through the generated client, which shows "Manage" only on a 200.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed partial class PermissionProbeTests
{
    private const string Roles = "backend.authorization=roles";
    private const string MicroFrontends = "frontend.architecture=micro-frontends";

    public static TheoryData<string, string[]> Projects => new()
    {
        { "saas-ai-react", [Roles] },
        { "saas-ai-react", ["backend.authorization=claims"] },
        { "saas-ai-react", ["backend.authorization=roles,claims"] },
        { "saas-standard-vue", [Roles, "backend.llm=openai", "backend.llm.agents=agent-framework", "frontend.agent-ui=ag-ui", "frontend.dashboards=user-editable"] },
    };

    /// <summary>The module page, the client call it makes, and the generated hook or path it names, per router and client.</summary>
    public static TheoryData<string, string[], string, string> Pages => new()
    {
        { "saas-standard-react", [Roles], "web/app/src/modules/billing/BillingPage.tsx", "useGetBillingPermissionsManageQuery" },
        { "saas-standard-react", [Roles, "frontend.routing=tanstack-router"], "web/app/src/modules/billing/BillingPage.tsx", "useGetBillingPermissionsManageQuery" },
        { "lean-crud", [Roles], "web/app/src/modules/billing/BillingPage.tsx", "useGetBillingPermissionsManage" },
        { "lean-crud", [Roles, "frontend.api-client=openapi-fetch"], "web/app/src/modules/billing/BillingPage.tsx", "api.GET('/billing/permissions/manage')" },
        { "saas-standard-vue", [Roles], "web/app/src/modules/billing/BillingPage.vue", "useGetBillingPermissionsManage" },
        { "saas-standard-vue", [Roles, "frontend.state=pinia"], "web/app/src/modules/billing/BillingPage.vue", "getBillingPermissionsManage" },
        { "saas-standard-vue", [Roles, "frontend.state=pinia", "frontend.api-client=openapi-fetch"], "web/app/src/modules/billing/BillingPage.vue", "api.GET('/billing/permissions/manage')" },
        // A remote has no API client of its own: the shell's page around it asks the probe.
        { "industrial-event-driven-mfe", [Roles], "web/app/src/modules/billing/BillingPage.tsx", "useGetBillingPermissionsManage" },
        { "saas-standard-react", [Roles, MicroFrontends, "frontend.design-system=tokens"], "web/app/src/modules/billing/BillingPage.tsx", "useGetBillingPermissionsManageQuery" },
        { "saas-standard-react", [Roles, MicroFrontends, "frontend.design-system=tokens", "frontend.routing=tanstack-router"], "web/app/src/modules/billing/BillingPage.tsx", "useGetBillingPermissionsManageQuery" },
        { "saas-standard-vue", [Roles, MicroFrontends, "frontend.design-system=tokens", "frontend.mfe-technique=native-federation"], "web/app/src/modules/billing/BillingPage.vue", "useGetBillingPermissionsManage" },
        { "saas-standard-vue", [Roles, MicroFrontends, "frontend.design-system=tokens", "frontend.mfe-technique=web-components", "frontend.state=pinia", "frontend.api-client=openapi-fetch"], "web/app/src/modules/billing/BillingPage.vue", "api.GET('/billing/permissions/manage')" },
    };

    /// <summary>A micro-frontend shell's module page, which mounts the module's remote, and the test add-module writes beside it.</summary>
    public static TheoryData<string, string[], string, string> RemotePages => new()
    {
        { "industrial-event-driven-mfe", [Roles], "web/app/src/modules/billing/BillingPage.tsx", "web/app/src/modules/billing/BillingPage.probe.test.tsx" },
        { "saas-standard-react", [Roles, MicroFrontends, "frontend.design-system=tokens", "frontend.routing=tanstack-router"], "web/app/src/modules/billing/BillingPage.tsx", "web/app/src/modules/billing/BillingPage.probe.test.tsx" },
        { "saas-standard-vue", [Roles, MicroFrontends, "frontend.design-system=tokens", "frontend.mfe-technique=native-federation"], "web/app/src/modules/billing/BillingPage.vue", "web/app/src/modules/billing/BillingPage.probe.test.ts" },
    };

    [Theory]
    [MemberData(nameof(Projects))]
    public void Every_generated_modules_probe_dispatches_the_CanManage_query_and_is_described(string profile, string[] overrides)
    {
        var files = Generate(profile, overrides);
        var endpoints = files.Where(file => EndpointFile().IsMatch(file.Key)).ToList();
        endpoints.Count.ShouldBeGreaterThan(1);

        foreach (var (path, content) in endpoints)
        {
            var module = EndpointFile().Match(path).Groups["module"].Value;
            content.ShouldContain("dispatcher.QueryAsync(new CanManage(), cancellationToken)", customMessage: path);
            content.ShouldContain(".ToHttpResult()", customMessage: path);
            content.ShouldContain($".RequireAuthorization({module}PolicyNames.Manage)", customMessage: path);
            content.ShouldNotContain("ExcludeFromDescription", customMessage: path);
            files.ShouldContainKey($"src/Modules/{module}/{module}.Application/Queries/CanManage/CanManage.cs");
            files[$"src/Modules/{module}/{module}.Application/Queries/CanManage/CanManageHandler.cs"]
                .ShouldContain($"{module}PolicyNames.Manage.ToDto()", customMessage: path);
        }

        files["src/BuildingBlocks/Modules/PermissionDto.cs"].ShouldContain("public sealed record PermissionDto(string Name);");
        files.ShouldContainKey("src/BuildingBlocks/Modules/PermissionMappings.cs");
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void The_frontends_openapi_copy_lists_every_modules_probe_answering_a_PermissionDto_where_the_host_maps_it(string profile, string[] overrides)
    {
        var document = JsonNode.Parse(Generate(profile, overrides)["web/app/openapi.json"])!;
        var paths = document["paths"]!.AsObject().Select(path => path.Key).ToList();

        // The host maps its own endpoints first, then each module's, modules by name and a module's endpoints by type name.
        var assistant = paths.IndexOf("/assistant/permissions/manage");
        var dashboards = paths.IndexOf("/dashboards/permissions/manage");
        assistant.ShouldBeGreaterThan(paths.IndexOf("/me"));
        dashboards.ShouldBe(assistant + 1);
        paths.IndexOf("/dashboards").ShouldBe(dashboards + 1);

        var answer = document["paths"]!["/dashboards/permissions/manage"]!["get"]!["responses"]!["200"]!;
        ((string?)answer["content"]!["application/json"]!["schema"]!["$ref"]).ShouldBe("#/components/schemas/PermissionDto");
        var schema = document["components"]!["schemas"]!["PermissionDto"]!;
        schema["required"]!.AsArray().Select(name => (string?)name).ShouldBe(["name"]);
        ((string?)schema["properties"]!["name"]!["type"]).ShouldBe("string");
    }

    /// <summary>
    /// The generated integration test compares the copy with the served document re-serialised indented, byte for byte;
    /// a contribution that leaves a stray line break between entries fails it only there, after a Docker run.
    /// </summary>
    [Theory]
    [MemberData(nameof(Projects))]
    [InlineData("saas-standard-react", new[] { Roles, "frontend.dashboards=user-editable" })]
    [InlineData("saas-standard-react", new[] { Roles, "frontend.dashboards=user-editable", "backend.mediator=wolverine" })]
    public void The_frontends_openapi_copy_is_written_as_the_integration_test_writes_it(string profile, string[] overrides)
    {
        var copy = Generate(profile, overrides)["web/app/openapi.json"];

        (JsonNode.Parse(copy)!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n").ShouldBe(copy);
    }

    [Fact]
    public void Without_authorization_choices_neither_the_probe_nor_PermissionDto_is_generated()
    {
        var files = Generate("saas-ai-react", []);

        files.Keys.ShouldNotContain(path => path.Contains("CanManage", StringComparison.Ordinal));
        files.Keys.ShouldNotContain("src/BuildingBlocks/Modules/PermissionDto.cs");
        files["web/app/openapi.json"].ShouldNotContain("permissions/manage");
        files["web/app/openapi.json"].ShouldNotContain("PermissionDto");
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void A_new_modules_page_asks_its_probe_through_the_generated_client_and_shows_Manage(string profile, string[] overrides, string page, string call)
    {
        using var project = new GeneratedProject(profile, overrides);
        project.AddModule("billing");

        var content = File.ReadAllText(Path.Combine(project.ProjectDirectory, page));
        content.ShouldContain("useCanManage");
        content.ShouldContain("Manage");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web/app/src/modules/billing/useCanManage.ts")).ShouldContain(call);
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web/app/openapi.json")).ShouldContain("\"/billing/permissions/manage\"");
    }

    [Theory]
    [MemberData(nameof(RemotePages))]
    public void A_micro_frontend_shells_page_mounts_the_remote_and_its_test_checks_Manage_shows_only_on_200(string profile, string[] overrides, string page, string test)
    {
        using var project = new GeneratedProject(profile, overrides);
        project.AddModule("billing");

        var content = File.ReadAllText(Path.Combine(project.ProjectDirectory, page));
        content.ShouldContain("useCanManage");
        content.ShouldContain("module=\"billing\"");
        var pageTest = File.ReadAllText(Path.Combine(project.ProjectDirectory, test));
        pageTest.ShouldContain("vi.mock('./useCanManage'");
        pageTest.ShouldContain("Manage");
    }

    [Fact]
    public void Without_authorization_choices_a_micro_frontend_shells_page_only_mounts_the_remote()
    {
        using var project = new GeneratedProject("industrial-event-driven-mfe");
        project.AddModule("billing");

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web/app/src/modules/billing/BillingPage.tsx")).ShouldNotContain("useCanManage");
        File.Exists(Path.Combine(project.ProjectDirectory, "web/app/src/modules/billing/useCanManage.ts")).ShouldBeFalse();
        File.Exists(Path.Combine(project.ProjectDirectory, "web/app/src/modules/billing/BillingPage.probe.test.tsx")).ShouldBeFalse();
    }

    [Fact]
    public void Without_authorization_choices_a_new_modules_page_asks_nothing()
    {
        using var project = new GeneratedProject("saas-standard-react");
        project.AddModule("billing");

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web/app/src/modules/billing/BillingPage.tsx")).ShouldNotContain("useCanManage");
        File.Exists(Path.Combine(project.ProjectDirectory, "web/app/src/modules/billing/useCanManage.ts")).ShouldBeFalse();
    }

    /// <summary>
    /// The generated web app, with a module added: codegen from the copy add-module extended gives the page its hook,
    /// so build, lint and the page's own tests pass (Node and pnpm required).
    /// </summary>
    [Theory]
    [InlineData("saas-standard-react", new[] { Roles })]
    [InlineData("lean-crud", new[] { Roles, "frontend.api-client=openapi-fetch" })]
    [InlineData("saas-standard-vue", new[] { Roles })]
    [InlineData("saas-standard-vue", new[] { Roles, "frontend.state=pinia" })]
    [InlineData("industrial-event-driven-mfe", new[] { Roles })]
    [InlineData("saas-standard-vue", new[] { Roles, MicroFrontends, "frontend.design-system=tokens", "frontend.mfe-technique=native-federation" })]
    public void The_web_app_builds_lints_and_tests_with_a_new_modules_page_calling_its_probe(string profile, string[] overrides)
    {
        using var project = new GeneratedProject(profile, overrides);
        project.AddModule("billing");
        var web = Path.Combine(project.ProjectDirectory, "web");

        foreach (var command in new[] { "install", "build", "lint", "test" })
        {
            var (exitCode, output) = Shell.Run(web, "pnpm", command);
            exitCode.ShouldBe(0, $"pnpm {command}:\n{output}");
        }
    }

    /// <summary>
    /// After <c>fabric new</c> and <c>add-module</c>, the document the host serves holds the new module's probe and is the
    /// frontend's copy, byte for byte: the generated integration tests pass, the copy's own test included (Docker required).
    /// </summary>
    [Fact]
    public void After_add_module_the_served_document_has_the_probe_and_the_frontends_copy_is_current()
    {
        using var project = new GeneratedProject("saas-ai-react", Roles);
        project.AddModule("billing");
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "web/app/openapi.json")).ShouldContain("\"/billing/permissions/manage\"");
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        output.ShouldNotContain("The_frontends_copy_of_the_openapi_document", customMessage: output);
        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0", customMessage: output);
    }

    [Fact]
    public void Add_module_adds_the_probe_and_PermissionDto_to_a_copy_without_modules()
    {
        var addition = Add(Record("lean-crud", [Roles]), "billing");

        var openApi = addition.OpenApi.ShouldNotBeNull();
        openApi.Paths.Select(path => path.Key).ShouldBe(["/billing/permissions/manage"]);
        openApi.Schemas.Select(schema => schema.Key).ShouldBe(["PermissionDto"]);
        openApi.LaterModules.ShouldBeEmpty();
    }

    [Fact]
    public void Add_module_adds_only_the_probe_once_PermissionDto_is_there_and_names_the_modules_mapped_after_it()
    {
        var addition = Add(Record("saas-ai-react", [Roles]), "billing");

        var openApi = addition.OpenApi.ShouldNotBeNull();
        openApi.Paths.Select(path => path.Key).ShouldBe(["/billing/permissions/manage"]);
        openApi.Schemas.ShouldBeEmpty();
        openApi.LaterModules.ShouldBe(["/dashboards"]);
    }

    [Fact]
    public void Add_module_adds_nothing_to_the_copy_without_authorization_choices()
    {
        Add(Record("saas-ai-react", []), "billing").OpenApi.ShouldBeNull();
    }

    [Fact]
    public void A_new_modules_probe_lands_in_the_copy_where_the_host_maps_it()
    {
        using var project = new GeneratedProject("saas-ai-react", Roles);
        project.AddModule("billing");
        project.AddModule("zeta");

        var paths = JsonNode.Parse(File.ReadAllText(Path.Combine(project.ProjectDirectory, "web/app/openapi.json")))!["paths"]!.AsObject()
            .Select(path => path.Key)
            .ToList();

        paths.IndexOf("/billing/permissions/manage").ShouldBe(paths.IndexOf("/assistant/permissions/manage") + 1);
        paths.IndexOf("/dashboards/permissions/manage").ShouldBe(paths.IndexOf("/billing/permissions/manage") + 1);
        paths.Last().ShouldBe("/zeta/permissions/manage");
    }

    [Fact]
    public void A_regenerated_project_has_the_probe_of_every_module_add_module_added()
    {
        var withBilling = Record("lean-crud", [Roles]);
        var addition = Add(withBilling, "billing");
        var record = FabricFile.Read(new StringReader(addition.FabricYaml!), FabricFile.FileName);

        var regenerated = Generator.Regenerate(Repository.Catalog, Repository.Packs.Packs, record);

        regenerated.Errors.ShouldBeEmpty();
        var document = JsonNode.Parse(regenerated.Files.Single(file => file.Path == "web/app/openapi.json").Content)!;
        document["paths"]!.AsObject().Select(path => path.Key).ShouldContain("/billing/permissions/manage");
        document["components"]!["schemas"]!.AsObject().Select(schema => schema.Key).ShouldContain("PermissionDto");
    }

    private static FabricRecord Record(string profile, string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return FabricFile.Read(new StringReader(generated.Files.Single(file => file.Path == FabricFile.FileName).Content), FabricFile.FileName);
    }

    private static ModuleAddition Add(FabricRecord project, string name)
    {
        var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, project, name, []);
        addition.Errors.ShouldBeEmpty();
        return addition;
    }

    private static Dictionary<string, string> Generate(string profile, string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    [GeneratedRegex("^src/Modules/(?<module>[^/]+)/[^/]+\\.Api/Endpoints/CanManageEndpoint\\.cs$")]
    private static partial Regex EndpointFile();
}
