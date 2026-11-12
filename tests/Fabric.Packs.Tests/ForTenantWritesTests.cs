using System.Text.Json.Nodes;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// Staff write into one tenant's rows (#629, owner decisions of 2026-11-12): with <c>backend.tenancy=column</c>,
/// <c>backend.authorization</c> containing roles and Keycloak or generic OIDC, an endpoint mapped with
/// <c>.RequireAuthorization(&lt;Module&gt;PolicyNames.WriteForTenants).ForTenant()</c> runs its handler in the tenant its
/// route names, for a user holding the role <c>staff</c>; a tenant missing from <c>Tenancy:Tenants</c> answers 404, and
/// <c>TenantScope.Begin</c> is banned from request code. Every other combination generates what it did before.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class ForTenantWritesTests
{
    private const string Profile = "saas-standard-react";
    private const string WithRag = "saas-ai-react";
    private const string Roles = "backend.authorization=roles";
    private const string RolesAndClaims = "backend.authorization=roles,claims";

    private static readonly string[] ForTenantFiles =
    [
        "src/BuildingBlocks/Tenancy/ITenantDirectory.cs",
        "src/BuildingBlocks/Tenancy/ConfiguredTenants.cs",
        "src/BuildingBlocks/Tenancy/TenantDirectoryPolicies.cs",
        "src/BuildingBlocks/Tenancy/WritesForTenant.cs",
        "src/BuildingBlocks/Tenancy/Wiring/TenantEndpoints.cs",
        "src/BuildingBlocks/Tenancy/Wiring/TenantDirectoryRegistration.cs",
        "src/AppHost/Wiring/TenantEnvironment.cs",
        "tests/Architecture.Tests/Rules/ForTenantRequiresTheCrossTenantRole.cs",
        "tests/Architecture.Tests/Rules/TenantScopeBeginsOnlyOutsideRequests.cs",
    ];

    [Theory]
    [InlineData("backend.auth=keycloak", Roles)]
    [InlineData("backend.auth=keycloak", RolesAndClaims)]
    [InlineData("backend.auth=generic-oidc", Roles)]
    [InlineData("backend.auth=generic-oidc", RolesAndClaims)]
    public void With_a_tenant_column_and_roles_a_module_gets_a_write_for_tenants_policy_and_the_host_a_tenant_list(string auth, string authorization)
    {
        using var project = new GeneratedProject(Profile, auth, authorization);
        project.AddModule("billing");

        foreach (var file in ForTenantFiles)
        {
            File.Exists(Path.Combine(project.ProjectDirectory, file)).ShouldBeTrue(file);
        }

        Read(project, "src/Modules/Billing/Billing.Application/BillingPolicyNames.cs")
            .ShouldContain("public const string WriteForTenants = \"billing.write-for-tenants\";");
        Read(project, "src/Modules/Billing/Billing.Api/BillingPolicies.cs")
            .ShouldContain("policies.AddPolicy(BillingPolicyNames.WriteForTenants, policy => policy.RequireRole(CrossTenantRole.Name));");
        Read(project, "src/BuildingBlocks/Tenancy/TenantScope.cs").ShouldContain("internal static IDisposable ForTenant(string tenantId)");
        Read(project, "src/BuildingBlocks/Tenancy/ConfiguredTenants.cs").ShouldContain("\"Tenancy:Tenants\"");
        Read(project, "src/Host/Program.cs").ShouldContain("app.MapGet(\"/tenants\"");
        var appHost = Read(project, "src/AppHost/AppHost.cs");
        appHost.ShouldContain("string[] tenants = [\"tenant-a\", \"tenant-b\"];");
        appHost.ShouldContain(".WithTenants(tenants)");
    }

    [Theory]
    [InlineData(Profile, new string[0])]
    [InlineData(Profile, new[] { "backend.authorization=claims" })]
    [InlineData(Profile, new[] { "backend.auth=entra-id", Roles })]
    [InlineData(Profile, new[] { "backend.tenancy=schema-per-tenant", Roles })]
    [InlineData("lean-crud", new[] { Roles })]
    [InlineData(WithRag, new string[0])]
    public void Any_other_combination_has_no_writes_for_a_named_tenant(string profile, string[] overrides)
    {
        var files = Files(profile, overrides);

        foreach (var file in ForTenantFiles.Where(file => file != "src/AppHost/Wiring/TenantEnvironment.cs" || !overrides.Contains("backend.tenancy=schema-per-tenant")))
        {
            files.Keys.ShouldNotContain(file);
        }

        // An ADR may say why there is none; nothing else names it.
        string[] names = ["ForTenant()", "WriteForTenants", "ITenantDirectory", "TenantScopeBeginsOnlyOutsideRequests"];
        files.Where(file => !file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && names.Any(name => file.Value.Contains(name, StringComparison.Ordinal)))
            .Select(file => file.Key)
            .ShouldBeEmpty();
        if (files.TryGetValue("web/app/openapi.json", out var document))
        {
            document.ShouldNotContain("\"/tenants\"");
        }
    }

    [Fact]
    public void The_frontends_copy_of_the_openapi_document_has_the_tenant_list()
    {
        var document = JsonNode.Parse(Files(WithRag, RolesAndClaims)["web/app/openapi.json"])!;

        var paths = document["paths"]!.AsObject().Select(path => path.Key).ToList();
        paths.ShouldContain("/tenants");
        paths.IndexOf("/tenants").ShouldBe(paths.IndexOf("/me/tenant") + 1);
    }

    [Fact]
    public void The_tenant_column_adr_is_amended_and_the_agent_rules_describe_writes_for_a_named_tenant()
    {
        var files = Files(Profile, Roles);
        var adr = Adr(files, "Tenancy: a tenant column");
        var agents = files["AGENTS.md"];

        adr.ShouldContain(".RequireAuthorization(<Module>PolicyNames.WriteForTenants).ForTenant()");
        adr.ShouldContain("ForTenantRequiresTheCrossTenantRole");
        adr.ShouldContain("TenantScopeBeginsOnlyOutsideRequests");
        adr.ShouldContain("`Tenancy:Tenants`");
        adr.ShouldContain("`tenant-not-found`");
        adr.ShouldContain("**Amended**");
        adr.ShouldNotContain("narrows the work to that tenant again");
        agents.ShouldContain(".RequireAuthorization(<Module>PolicyNames.WriteForTenants).ForTenant()");
        agents.ShouldContain("Never call `TenantScope.Begin` in an Api project or a command or query handler");

        var withoutRoles = Files(Profile);
        Adr(withoutRoles, "Tenancy: a tenant column").ShouldNotContain("ForTenant()");
        withoutRoles["AGENTS.md"].ShouldNotContain("ForTenant()");
    }

    [Fact]
    public void With_entra_id_the_tenant_column_adr_says_why_staff_cannot_write_for_a_tenant_yet()
    {
        var adr = Adr(Files(Profile, "backend.auth=entra-id", Roles), "Tenancy: a tenant column");

        adr.ShouldContain("`ForTenant()` is not generated with Entra ID");
    }

    [Fact]
    public void The_schema_per_tenant_adr_says_why_it_has_no_writes_for_a_named_tenant()
    {
        var adr = Adr(Files(Profile, "backend.tenancy=schema-per-tenant", Roles), "Tenancy: a schema per tenant");

        adr.ShouldContain("no `ForTenant()`");
    }

    /// <summary>
    /// The decision against the running Aspire stack, in the pilot's choices: staff write into a tenant the route names
    /// and only that tenant's users see it, a bulk write there reaches that tenant's rows alone, an unknown tenant answers
    /// 404 and saves nothing, a tenant's user is refused, staff save nothing on an endpoint without the opt-in, and only
    /// staff list the tenants. The generated OpenApiDocumentTests check the frontend's copy of the document as well.
    /// </summary>
    [Fact]
    public void Staff_write_into_the_tenant_an_endpoint_names_and_nowhere_else()
    {
        using var project = new GeneratedProject(WithRag, RolesAndClaims);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", NotesEndpoints("""
            endpoints.MapPost("/tenants/{tenant}/notes", AddAsync).RequireAuthorization(BillingPolicyNames.WriteForTenants).ForTenant().ExcludeFromDescription();
            endpoints.MapDelete("/tenants/{tenant}/notes", DeleteAllAsync).RequireAuthorization(BillingPolicyNames.WriteForTenants).ForTenant().ExcludeFromDescription();
            endpoints.MapGet("/notes/all-tenants", ListAsync).RequireAuthorization(BillingPolicyNames.ReadAllTenants).AcrossTenants().ExcludeFromDescription();
            """));
        project.Write("tests/Integration.Tests/ForTenantTests.cs", ForTenantIntegrationTests);
        MigrationTests.AddNotesWithAMigration(project);

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0", customMessage: output);
    }

    [Fact]
    public void A_wrongly_authorized_or_combined_for_tenant_endpoint_and_a_begin_in_request_code_fail_the_architecture_tests_naming_them()
    {
        using var project = new GeneratedProject(WithRag, RolesAndClaims);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Domain", NoteEntity);
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", NotesEndpoints("""
            endpoints.MapPost("/tenants/{tenant}/notes", AddAsync).RequireAuthorization(BillingPolicyNames.WriteForTenants).ForTenant().ExcludeFromDescription();
            endpoints.MapPost("/tenants/{tenant}/notes/managed", AddAsync).RequireAuthorization(BillingPolicyNames.Manage).ForTenant().ExcludeFromDescription();
            endpoints.MapPost("/tenants/{tenant}/notes/signed-in", AddAsync).ForTenant().ExcludeFromDescription();
            endpoints.MapPost("/tenants/{tenant}/notes/anonymous", AddAsync).RequireAuthorization(BillingPolicyNames.WriteForTenants).AllowAnonymous().ForTenant().ExcludeFromDescription();
            endpoints.MapGet("/tenants/{tenant}/notes/across", ListAsync).RequireAuthorization(BillingPolicyNames.WriteForTenants).ForTenant().AcrossTenants().ExcludeFromDescription();
            endpoints.MapPost("/tenants/{tenant}/notes/shared", AddAsync).RequireAuthorization(BillingPolicyNames.WriteForTenants).ForTenant().WritesSharedRecords().ExcludeFromDescription();
            endpoints.MapPost("/notes/narrowed", AddInTenantAAsync).RequireAuthorization(BillingPolicyNames.ReadAllTenants).ExcludeFromDescription();
            """));
        project.WriteTypes("src/Modules/Billing/Billing.Application", """
            using Shop.BuildingBlocks.Messaging;
            using Shop.BuildingBlocks.Tenancy;

            namespace Shop.Modules.Billing.Application;

            public sealed record NarrowToATenant(string Tenant) : ICommand<Result<string, ApiError>>;

            public sealed class NarrowToATenantHandler(ITenantContext tenants) : ICommandHandler<NarrowToATenant, Result<string, ApiError>>
            {
                public Task<Result<string, ApiError>> HandleAsync(NarrowToATenant command, CancellationToken cancellationToken)
                {
                    Ensure.That(command).IsNotNull();

                    using var tenant = TenantScope.Begin(command.Tenant);
                    return Task.FromResult<Result<string, ApiError>>(Success(tenants.TenantId ?? string.Empty));
                }
            }

            /// <summary>Background work, which may begin a tenant's scope.</summary>
            public static class NightlyWork
            {
                public static IDisposable InTenantA() => TenantScope.Begin("tenant-a");
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("POST /billing/tenants/{tenant}/notes/managed writes for the tenant its route names (ForTenant()), but its authorization does not require the role staff (CrossTenantRole.Name).", Case.Sensitive, output);
        output.ShouldContain("POST /billing/tenants/{tenant}/notes/signed-in writes for the tenant its route names (ForTenant()), but its authorization does not require the role staff (CrossTenantRole.Name).", Case.Sensitive, output);
        output.ShouldContain("POST /billing/tenants/{tenant}/notes/anonymous writes for the tenant its route names (ForTenant()), but its authorization does not require the role staff (CrossTenantRole.Name).", Case.Sensitive, output);
        output.ShouldContain("GET /billing/tenants/{tenant}/notes/across is mapped with both ForTenant() and AcrossTenants()", Case.Sensitive, output);
        output.ShouldContain("POST /billing/tenants/{tenant}/notes/shared is mapped with both ForTenant() and WritesSharedRecords()", Case.Sensitive, output);
        output.ShouldNotContain("POST /billing/tenants/{tenant}/notes writes for the tenant", Case.Sensitive, output);
        output.ShouldContain("src/Modules/Billing/Billing.Api/Endpoints/Notes.cs:", Case.Sensitive, output);
        output.ShouldContain("src/Modules/Billing/Billing.Application/NarrowToATenantHandler.cs:", Case.Sensitive, output);
        output.ShouldContain("calls TenantScope.Begin in request code", Case.Sensitive, output);
        output.ShouldNotContain("NightlyWork.cs:", Case.Sensitive, output);
    }

    [Fact]
    public void An_endpoint_mapped_with_for_tenant_whose_route_names_no_tenant_fails_when_the_host_maps_it()
    {
        using var project = new GeneratedProject(WithRag, RolesAndClaims);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Domain", NoteEntity);
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", NotesEndpoints("""
            endpoints.MapPost("/notes/for-a-tenant", AddAsync).RequireAuthorization(BillingPolicyNames.WriteForTenants).ForTenant().ExcludeFromDescription();
            """));
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("POST /billing/notes/for-a-tenant is mapped with ForTenant(), but its route has no {tenant} parameter", Case.Sensitive, output);
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
    /// mappings over the same handlers. Out of the OpenAPI document, so the frontend's copy stays current.
    /// </summary>
    private static string NotesEndpoints(string mappings) => $$"""
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.EntityFrameworkCore;
        using Shop.BuildingBlocks.Api;
        using Shop.BuildingBlocks.Tenancy;
        using Shop.Modules.Billing.Application;
        using Shop.Modules.Billing.Domain;
        using Shop.Modules.Billing.Infrastructure.Persistence;

        namespace Shop.Modules.Billing.Api.Endpoints;

        public sealed record NewNote(string Text);

        public sealed class Notes : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapPost("/notes", AddAsync).RequireAuthorization().ExcludeFromDescription();
                endpoints.MapGet("/notes", ListAsync).RequireAuthorization().ExcludeFromDescription();
        {{mappings}}
            }

            internal static async Task<IResult> AddAsync(NewNote note, BillingDbContext context, CancellationToken cancellationToken)
            {
                context.Add(new Note(Guid.CreateVersion7(), note.Text));
                await context.SaveChangesAsync(cancellationToken);
                return TypedResults.NoContent();
            }

            // The hand-built way into one tenant that TenantScopeBeginsOnlyOutsideRequests refuses.
            internal static async Task<IResult> AddInTenantAAsync(NewNote note, BillingDbContext context, CancellationToken cancellationToken)
            {
                using var tenant = TenantScope.Begin("tenant-a");
                return await AddAsync(note, context, cancellationToken);
            }

            // A bulk write bypasses SaveChanges and the change tracker, but not the query filter.
            internal static async Task<IResult> DeleteAllAsync(BillingDbContext context, CancellationToken cancellationToken) =>
                TypedResults.Ok(await context.Set<Note>().ExecuteDeleteAsync(cancellationToken));

            internal static async Task<IResult> ListAsync(BillingDbContext context, CancellationToken cancellationToken) =>
                TypedResults.Ok(await context.Set<Note>().Select(note => note.Text).Order().ToListAsync(cancellationToken));
        }
        """;

    private const string ForTenantIntegrationTests = """
        using System.Net;
        using System.Net.Http.Json;

        namespace Shop.Integration.Tests;

        public class ForTenantTests(AspireFixture aspire)
        {
            private static readonly Uri Notes = new("/billing/notes", UriKind.Relative);
            private static readonly Uri AllTenants = new("/billing/notes/all-tenants", UriKind.Relative);
            private static readonly Uri TenantA = new("/billing/tenants/tenant-a/notes", UriKind.Relative);
            private static readonly Uri TenantB = new("/billing/tenants/tenant-b/notes", UriKind.Relative);
            private static readonly Uri Tenants = new("/tenants", UriKind.Relative);

            [Fact]
            public async Task Staff_write_into_the_tenant_the_route_names_and_only_that_tenants_users_see_it()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var staff = await aspire.CreateHostClientAsync("staff");

                using var response = await staff.PostAsJsonAsync(TenantA, new { Text = "a note staff wrote for tenant a" }, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                (await TextsAsync("developer", Notes, cancellationToken)).ShouldContain("a note staff wrote for tenant a");
                (await TextsAsync("developer-b", Notes, cancellationToken)).ShouldNotContain("a note staff wrote for tenant a");
            }

            [Fact]
            public async Task A_bulk_write_for_a_tenant_reaches_that_tenants_rows_alone()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await AddAsync("developer-b", "a note of tenant b a bulk delete for tenant a must not reach", cancellationToken);
                await AddAsync("developer", "a note of tenant a a bulk delete for tenant a removes", cancellationToken);
                using var staff = await aspire.CreateHostClientAsync("staff");

                using var response = await staff.DeleteAsync(TenantA, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                (await TextsAsync("developer", Notes, cancellationToken)).ShouldNotContain("a note of tenant a a bulk delete for tenant a removes");
                (await TextsAsync("developer-b", Notes, cancellationToken)).ShouldContain("a note of tenant b a bulk delete for tenant a must not reach");
            }

            [Fact]
            public async Task A_tenant_that_is_not_listed_answers_404_tenant_not_found_and_nothing_is_saved()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var staff = await aspire.CreateHostClientAsync("staff");

                using var response = await staff.PostAsJsonAsync(new Uri("/billing/tenants/tenant-aa/notes", UriKind.Relative), new { Text = "a note for a tenant nobody listed" }, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
                (await response.Content.ReadAsStringAsync(cancellationToken)).ShouldContain("tenant-not-found");
                (await TextsAsync("staff", AllTenants, cancellationToken)).ShouldNotContain("a note for a tenant nobody listed");
            }

            [Fact]
            public async Task A_tenants_user_is_forbidden_the_endpoint_that_opts_in()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var developer = await aspire.CreateHostClientAsync("developer");

                using var response = await developer.PostAsJsonAsync(TenantB, new { Text = "a note of tenant a's user for tenant b" }, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
                (await TextsAsync("developer-b", Notes, cancellationToken)).ShouldNotContain("a note of tenant a's user for tenant b");
            }

            [Fact]
            public async Task Staff_save_nothing_on_an_endpoint_that_does_not_opt_in()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var staff = await aspire.CreateHostClientAsync("staff");

                using var response = await staff.PostAsJsonAsync(Notes, new { Text = "a note of staff without a tenant" }, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
                (await TextsAsync("staff", AllTenants, cancellationToken)).ShouldNotContain("a note of staff without a tenant");
            }

            [Fact]
            public async Task Staff_list_the_tenants_and_a_tenants_user_is_forbidden_the_list()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var developer = await aspire.CreateHostClientAsync("developer");

                (await TextsAsync("staff", Tenants, cancellationToken)).ShouldBe(["tenant-a", "tenant-b"]);
                using var response = await developer.GetAsync(Tenants, cancellationToken);
                response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            }

            private async Task AddAsync(string user, string text, CancellationToken cancellationToken)
            {
                using var client = await aspire.CreateHostClientAsync(user);
                using var response = await client.PostAsJsonAsync(Notes, new { Text = text }, cancellationToken);
                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            private async Task<string[]> TextsAsync(string user, Uri uri, CancellationToken cancellationToken)
            {
                using var client = await aspire.CreateHostClientAsync(user);
                var texts = await client.GetFromJsonAsync<string[]>(uri, cancellationToken);
                texts.ShouldNotBeNull();
                return texts;
            }
        }
        """;

    private static string Read(GeneratedProject project, string path) => File.ReadAllText(Path.Combine(project.ProjectDirectory, path));

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        using var standIns = new StandIns();
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, profile, overrides);
        var generated = Generator.New(Repository.Catalog, packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static string Adr(Dictionary<string, string> files, string title) =>
        files.Single(file => file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && file.Value.StartsWith("# " + title, StringComparison.Ordinal)).Value;
}
