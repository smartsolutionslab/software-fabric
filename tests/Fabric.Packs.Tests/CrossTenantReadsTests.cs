using System.Text.Json.Nodes;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// Staff read across tenants (#610): with <c>backend.tenancy=column</c>, <c>backend.authorization</c> containing roles
/// and Keycloak or generic OIDC, an endpoint mapped with <c>.RequireAuthorization(&lt;Module&gt;PolicyNames.ReadAllTenants)
/// .AcrossTenants()</c> reads every tenant's rows for a user holding the role <c>staff</c>, and writes nothing. Every
/// other combination generates what it did before.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class CrossTenantReadsTests
{
    private const string Profile = "saas-standard-react";
    private const string Roles = "backend.authorization=roles";
    private const string RolesAndClaims = "backend.authorization=roles,claims";

    private static readonly string[] CrossTenantFiles =
    [
        "src/BuildingBlocks/Tenancy/CrossTenantRole.cs",
        "src/BuildingBlocks/Tenancy/ReadsAcrossTenants.cs",
        "src/BuildingBlocks/Tenancy/Wiring/AcrossTenantsEndpoints.cs",
        "tests/Architecture.Tests/Rules/AcrossTenantsRequiresTheCrossTenantRole.cs",
    ];

    [Theory]
    [InlineData("backend.auth=keycloak", Roles)]
    [InlineData("backend.auth=keycloak", RolesAndClaims)]
    [InlineData("backend.auth=generic-oidc", Roles)]
    [InlineData("backend.auth=generic-oidc", RolesAndClaims)]
    public void With_a_tenant_column_and_roles_a_module_gets_a_read_all_tenants_policy_that_requires_the_staff_role(string auth, string authorization)
    {
        using var project = new GeneratedProject(Profile, auth, authorization);
        project.AddModule("billing");

        foreach (var file in CrossTenantFiles)
        {
            File.Exists(Path.Combine(project.ProjectDirectory, file)).ShouldBeTrue(file);
        }

        Read(project, "src/BuildingBlocks/Tenancy/CrossTenantRole.cs").ShouldContain("public const string Name = \"staff\";");
        Read(project, "src/Modules/Billing/Billing.Application/BillingPolicyNames.cs")
            .ShouldContain("public const string ReadAllTenants = \"billing.read-all-tenants\";");
        Read(project, "src/Modules/Billing/Billing.Api/BillingPolicies.cs")
            .ShouldContain("policies.AddPolicy(BillingPolicyNames.ReadAllTenants, policy => policy.RequireRole(CrossTenantRole.Name));");
        Read(project, "src/BuildingBlocks/Tenancy/TenantScope.cs").ShouldContain("AllTenants()");
    }

    [Theory]
    [InlineData("backend.auth=keycloak", "/tenants/tenant-a")]
    [InlineData("backend.auth=generic-oidc", "/tenant-a")]
    public void The_realm_has_a_staff_role_and_a_staff_user_in_no_tenant(string auth, string developersGroup)
    {
        var realm = Realm(Profile, auth, Roles);

        realm["roles"]!["realm"]!.AsArray().Select(role => (string?)role!["name"]).ShouldBe(["manager", "staff"]);
        var users = realm["users"]!.AsArray().ToDictionary(user => (string)user!["username"]!, user => user!);
        users["staff"]["realmRoles"]!.AsArray().Select(role => (string?)role).ShouldBe(["staff"]);
        users["staff"]["groups"].ShouldBeNull();
        users["developer"]["realmRoles"]!.AsArray().Select(role => (string?)role).ShouldBe(["manager"]);
        users["developer"]["groups"]!.AsArray().Select(group => (string?)group).ShouldBe([developersGroup]);
    }

    [Theory]
    [InlineData(Profile, new string[0])]
    [InlineData(Profile, new[] { "backend.authorization=claims" })]
    [InlineData(Profile, new[] { "backend.auth=entra-id", Roles })]
    [InlineData(Profile, new[] { "backend.auth=entra-id", RolesAndClaims })]
    [InlineData(Profile, new[] { "backend.tenancy=schema-per-tenant", Roles })]
    [InlineData("lean-crud", new[] { Roles })]
    public void Any_other_combination_has_no_cross_tenant_reads(string profile, string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        var files = generated.Files.ToDictionary(file => file.Path, file => file.Content);

        foreach (var file in CrossTenantFiles)
        {
            files.Keys.ShouldNotContain(file);
        }

        // An ADR may say why there is none; nothing else names it.
        files.Where(file => !file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Value.Contains("AcrossTenants", StringComparison.Ordinal))
            .Select(file => file.Key)
            .ShouldBeEmpty();
        if (files.TryGetValue("src/AppHost/realms/realm.json", out var realm))
        {
            realm.ShouldNotContain("staff");
        }
    }

    [Fact]
    public void The_tenant_column_adr_and_agent_rules_describe_cross_tenant_reads_instead_of_ignoring_the_query_filter()
    {
        var files = Files(Profile, Roles);
        var adr = Adr(files, "Tenancy: a tenant column");
        var agents = files["AGENTS.md"];

        adr.ShouldContain(".RequireAuthorization(<Module>PolicyNames.ReadAllTenants).AcrossTenants()");
        adr.ShouldContain("AcrossTenantsRequiresTheCrossTenantRole");
        adr.ShouldNotContain("reading across tenants needs `IgnoreQueryFilters()`");
        agents.ShouldContain(".AcrossTenants()");
        agents.ShouldContain("`staff` / `staff`");

        var withoutRoles = Adr(Files(Profile), "Tenancy: a tenant column");
        withoutRoles.ShouldContain("reading across tenants needs `IgnoreQueryFilters()`");
        withoutRoles.ShouldNotContain("AcrossTenants");
    }

    [Fact]
    public void With_entra_id_the_tenant_column_adr_says_why_staff_cannot_read_across_tenants_yet()
    {
        var adr = Adr(Files(Profile, "backend.auth=entra-id", Roles), "Tenancy: a tenant column");

        adr.ShouldContain("reading across tenants needs `IgnoreQueryFilters()`");
        adr.ShouldContain("`AcrossTenants()` is not generated with Entra ID");
    }

    [Fact]
    public void The_schema_per_tenant_adr_says_why_it_has_no_cross_tenant_reads()
    {
        var adr = Adr(Files(Profile, "backend.tenancy=schema-per-tenant", Roles), "Tenancy: a schema per tenant");

        adr.ShouldContain("no `AcrossTenants()`");
        adr.ShouldContain("A database per tenant");
    }

    /// <summary>
    /// The four behaviours of the decision, against the running Aspire stack: a staff user reads both tenants' rows
    /// through an endpoint that opts in, sees nothing through one that does not, a tenant's user is refused the endpoint
    /// that opts in, and a write inside it fails.
    /// </summary>
    [Fact]
    public void Staff_read_every_tenants_rows_only_where_an_endpoint_opts_in_and_cannot_write_there()
    {
        using var project = new GeneratedProject(Profile, Roles);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", NotesEndpoints("""
            endpoints.MapGet("/notes/all-tenants", ListAsync).RequireAuthorization(BillingPolicyNames.ReadAllTenants).AcrossTenants().ExcludeFromDescription();
            endpoints.MapPost("/notes/all-tenants", AddAsync).RequireAuthorization(BillingPolicyNames.ReadAllTenants).AcrossTenants().ExcludeFromDescription();
            """));
        project.Write("tests/Integration.Tests/CrossTenantTests.cs", CrossTenantIntegrationTests);
        MigrationTests.AddNotesWithAMigration(project);

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0", customMessage: output);
    }

    [Fact]
    public void An_endpoint_reading_across_tenants_whose_policy_does_not_require_the_staff_role_fails_the_architecture_tests_naming_it()
    {
        using var project = new GeneratedProject(Profile, Roles);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", NotesEndpoints("""
            endpoints.MapGet("/notes/all-tenants", ListAsync).RequireAuthorization(BillingPolicyNames.ReadAllTenants).AcrossTenants().ExcludeFromDescription();
            endpoints.MapGet("/notes/managed", ListAsync).RequireAuthorization(BillingPolicyNames.Manage).AcrossTenants().ExcludeFromDescription();
            endpoints.MapGet("/notes/signed-in", ListAsync).AcrossTenants().ExcludeFromDescription();
            endpoints.MapGet("/notes/anonymous", ListAsync).RequireAuthorization(BillingPolicyNames.ReadAllTenants).AllowAnonymous().AcrossTenants().ExcludeFromDescription();
            """));
        project.WriteTypes("src/Modules/Billing/Billing.Domain", NoteEntity);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("GET /billing/notes/managed reads across tenants (AcrossTenants()), but its authorization does not require the role staff (CrossTenantRole.Name).", Case.Sensitive, output);
        output.ShouldContain("GET /billing/notes/signed-in reads across tenants (AcrossTenants()), but its authorization does not require the role staff (CrossTenantRole.Name).", Case.Sensitive, output);
        output.ShouldContain("GET /billing/notes/anonymous reads across tenants (AcrossTenants()), but its authorization does not require the role staff (CrossTenantRole.Name).", Case.Sensitive, output);
        output.ShouldNotContain("GET /billing/notes/all-tenants reads across tenants", Case.Sensitive, output);
    }

    private const string NoteEntity = """
        using Shop.BuildingBlocks.Domain;

        namespace Shop.Modules.Billing.Domain;

        public sealed class Note(Guid id, string text) : Entity<Guid>(id)
        {
            public string Text { get; private set; } = text;
        }
        """;

    /// <summary>
    /// The billing module's notes: <c>POST</c> and <c>GET /billing/notes</c> in the caller's tenant, and the given
    /// mappings over the same two handlers. Out of the OpenAPI document, so the frontend's copy stays current.
    /// </summary>
    private static string NotesEndpoints(string mappings) => $$"""
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.EntityFrameworkCore;
        using Shop.BuildingBlocks.Api;
        using Shop.Modules.Billing.Application;
        using Shop.Modules.Billing.Domain;
        using Shop.Modules.Billing.Infrastructure.Persistence;

        namespace Shop.Modules.Billing.Api.Endpoints;

        public sealed record NewNote(string Text);

        public sealed class Notes : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapPost("/notes", AddAsync).ExcludeFromDescription();
                endpoints.MapGet("/notes", ListAsync).ExcludeFromDescription();
        {{mappings}}
            }

            private static async Task<IResult> AddAsync(NewNote note, BillingDbContext context, CancellationToken cancellationToken)
            {
                context.Add(new Note(Guid.CreateVersion7(), note.Text));
                await context.SaveChangesAsync(cancellationToken);
                return TypedResults.NoContent();
            }

            private static async Task<IResult> ListAsync(BillingDbContext context, CancellationToken cancellationToken) =>
                TypedResults.Ok(await context.Set<Note>().Select(note => note.Text).Order().ToListAsync(cancellationToken));
        }
        """;

    private const string CrossTenantIntegrationTests = """
        using System.Net;
        using System.Net.Http.Json;

        namespace Shop.Integration.Tests;

        public class CrossTenantTests(AspireFixture aspire)
        {
            private static readonly Uri Notes = new("/billing/notes", UriKind.Relative);
            private static readonly Uri AllTenants = new("/billing/notes/all-tenants", UriKind.Relative);

            [Fact]
            public async Task Staff_read_both_tenants_rows_through_an_endpoint_that_opts_in()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await AddAsync("developer", "a note of tenant a", cancellationToken);
                await AddAsync("developer-b", "a note of tenant b", cancellationToken);
                using var staff = await aspire.CreateHostClientAsync("staff");

                var notes = await staff.GetFromJsonAsync<string[]>(AllTenants, cancellationToken);

                notes.ShouldNotBeNull();
                notes.ShouldContain("a note of tenant a");
                notes.ShouldContain("a note of tenant b");
            }

            [Fact]
            public async Task Staff_see_nothing_through_an_endpoint_that_does_not_opt_in()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await AddAsync("developer", "another note of tenant a", cancellationToken);
                using var staff = await aspire.CreateHostClientAsync("staff");

                var notes = await staff.GetFromJsonAsync<string[]>(Notes, cancellationToken);

                notes.ShouldNotBeNull();
                notes.ShouldBeEmpty();
            }

            [Fact]
            public async Task A_tenants_user_is_forbidden_the_endpoint_that_opts_in()
            {
                using var developer = await aspire.CreateHostClientAsync("developer");

                using var response = await developer.GetAsync(AllTenants, TestContext.Current.CancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            }

            [Fact]
            public async Task A_write_inside_an_endpoint_that_opts_in_fails_and_saves_nothing()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var staff = await aspire.CreateHostClientAsync("staff");

                using var response = await staff.PostAsJsonAsync(AllTenants, new { Text = "a note of no tenant" }, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
                var notes = await staff.GetFromJsonAsync<string[]>(AllTenants, cancellationToken);
                notes.ShouldNotBeNull();
                notes.ShouldNotContain("a note of no tenant");
            }

            private async Task AddAsync(string user, string text, CancellationToken cancellationToken)
            {
                using var client = await aspire.CreateHostClientAsync(user);
                using var response = await client.PostAsJsonAsync(Notes, new { Text = text }, cancellationToken);
                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }
        }
        """;

    private static string Read(GeneratedProject project, string path) => File.ReadAllText(Path.Combine(project.ProjectDirectory, path));

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static JsonNode Realm(string profile, params string[] overrides) =>
        JsonNode.Parse(Files(profile, overrides)["src/AppHost/realms/realm.json"])!;

    private static string Adr(Dictionary<string, string> files, string title) =>
        files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Value.StartsWith("# " + title, StringComparison.Ordinal)).Value;
}
