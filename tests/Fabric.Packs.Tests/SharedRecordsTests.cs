using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// Records shared by all tenants (#623, owner decisions of 2026-11-12): with <c>backend.tenancy=column</c>,
/// <c>backend.authorization</c> containing roles and Keycloak or generic OIDC, an entity type configured with
/// <c>SharedByAllTenants()</c> has no tenant column and no tenant filter, so every tenant's users read its records; only
/// an endpoint mapped with <c>.RequireAuthorization(&lt;Module&gt;PolicyNames.WriteSharedRecords).WritesSharedRecords()</c>
/// writes them, and an event published there carries no tenant. With rag, <c>SharedVectorIndex&lt;TContext&gt;</c> keeps
/// their vectors apart from every tenant's. Every other combination generates what it did before.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class SharedRecordsTests
{
    private const string Profile = "saas-standard-react";
    private const string WithRag = "saas-ai-react";
    private const string Roles = "backend.authorization=roles";
    private const string RolesAndClaims = "backend.authorization=roles,claims";

    private static readonly string[] SharedRecordsFiles =
    [
        "src/BuildingBlocks/Tenancy/SharedRecordsWriter.cs",
        "src/BuildingBlocks/Tenancy/SharedRecordsBulkWriteGuard.cs",
        "src/BuildingBlocks/Tenancy/Wiring/SharedRecordsEndpoints.cs",
        "src/BuildingBlocks/Tenancy/Wiring/SharedRecordsModel.cs",
        "tests/Architecture.Tests/Rules/WritesSharedRecordsRequiresTheCrossTenantRole.cs",
    ];

    private static readonly string[] SharedVectorFiles =
    [
        "src/BuildingBlocks/Rag/SharedVectorIndex.cs",
        "tests/Rag.Tests/SharedVectorIndexTests.cs",
        "tests/Rag.Tests/StaffScope.cs",
        "tests/Rag.Tests/SampleSharedArticlesDbContext.cs",
    ];

    [Theory]
    [InlineData("backend.auth=keycloak", Roles)]
    [InlineData("backend.auth=keycloak", RolesAndClaims)]
    [InlineData("backend.auth=generic-oidc", Roles)]
    [InlineData("backend.auth=generic-oidc", RolesAndClaims)]
    public void With_a_tenant_column_and_roles_a_module_gets_a_write_shared_records_policy_that_requires_the_staff_role(string auth, string authorization)
    {
        using var project = new GeneratedProject(Profile, auth, authorization);
        project.AddModule("billing");

        foreach (var file in SharedRecordsFiles)
        {
            File.Exists(Path.Combine(project.ProjectDirectory, file)).ShouldBeTrue(file);
        }

        Read(project, "src/Modules/Billing/Billing.Application/BillingPolicyNames.cs")
            .ShouldContain("public const string WriteSharedRecords = \"billing.write-shared-records\";");
        Read(project, "src/Modules/Billing/Billing.Api/BillingPolicies.cs")
            .ShouldContain("policies.AddPolicy(BillingPolicyNames.WriteSharedRecords, policy => policy.RequireRole(CrossTenantRole.Name));");
        var scope = Read(project, "src/BuildingBlocks/Tenancy/TenantScope.cs");
        scope.ShouldContain("internal static IDisposable SharedRecords()");
        scope.ShouldContain("public static IDisposable Begin(EventMetadata metadata)");
        Read(project, "src/Contracts/EventMetadata.cs").ShouldContain("string? TenantId");
        Read(project, "src/BuildingBlocks/Tenancy/Wiring/SharedRecordsModel.cs").ShouldContain("SharedByAllTenants<TEntity>(this EntityTypeBuilder<TEntity> builder)");
    }

    [Fact]
    public void With_rag_shared_records_get_a_vector_index_and_a_table_of_their_own()
    {
        var files = Files(WithRag, RolesAndClaims);

        foreach (var file in SharedVectorFiles)
        {
            files.Keys.ShouldContain(file);
        }

        files["src/BuildingBlocks/Rag/VectorTables.cs"].ShouldContain(
            "CREATE TABLE IF NOT EXISTS {shared} (key text primary key, content text not null, embedding vector({Dimensions}) not null)");
        files["src/BuildingBlocks/Rag/Wiring/VectorIndexRegistration.cs"].ShouldContain(".AddScoped(typeof(SharedVectorIndex<>))");
        files["tests/Architecture.Tests/Rules/ModulesSearchOnlyTheirOwnVectors.cs"].ShouldContain("typeof(SharedVectorIndex<>)");

        // The tenant's own index stays as it was.
        files["src/BuildingBlocks/Rag/ModuleVectorIndex.cs"].ShouldBe(Files(WithRag)["src/BuildingBlocks/Rag/ModuleVectorIndex.cs"]);
    }

    [Theory]
    [InlineData(Profile, new string[0])]
    [InlineData(Profile, new[] { "backend.authorization=claims" })]
    [InlineData(Profile, new[] { "backend.auth=entra-id", Roles })]
    [InlineData(Profile, new[] { "backend.tenancy=schema-per-tenant", Roles })]
    [InlineData("lean-crud", new[] { Roles })]
    [InlineData(WithRag, new string[0])]
    [InlineData(WithRag, new[] { "backend.tenancy=none", RolesAndClaims })]
    public void Any_other_combination_has_no_shared_records(string profile, string[] overrides)
    {
        var files = Files(profile, overrides);

        foreach (var file in SharedRecordsFiles.Concat(SharedVectorFiles))
        {
            files.Keys.ShouldNotContain(file);
        }

        // An ADR may say why there are none; nothing else names them.
        string[] names = ["WritesSharedRecords", "SharedByAllTenants", "SharedVectorIndex", "shared_embeddings"];
        files.Where(file => !file.Key.StartsWith("docs/adr/", StringComparison.Ordinal) && names.Any(name => file.Value.Contains(name, StringComparison.Ordinal)))
            .Select(file => file.Key)
            .ShouldBeEmpty();
        files["src/Contracts/EventMetadata.cs"].ShouldNotContain("string? TenantId");
    }

    [Fact]
    public void The_tenant_column_adr_and_agent_rules_describe_shared_records()
    {
        var files = Files(Profile, Roles);
        var adr = Adr(files, "Tenancy: a tenant column");
        var agents = files["AGENTS.md"];

        adr.ShouldContain("`builder.SharedByAllTenants()`");
        adr.ShouldContain(".RequireAuthorization(<Module>PolicyNames.WriteSharedRecords).WritesSharedRecords()");
        adr.ShouldContain("WritesSharedRecordsRequiresTheCrossTenantRole");
        adr.ShouldContain("`TenantScope.Begin(message.Metadata)`");
        adr.ShouldContain("With `backend.tenancy: none` every record is shared already");
        agents.ShouldContain(".WritesSharedRecords()");
        agents.ShouldContain("using var tenant = TenantScope.Begin(message.Metadata);");
        agents.ShouldNotContain("TenantScope.Begin(message.Metadata.TenantId)");

        var withoutRoles = Files(Profile);
        Adr(withoutRoles, "Tenancy: a tenant column").ShouldNotContain("SharedByAllTenants");
        withoutRoles["AGENTS.md"].ShouldContain("using var tenant = TenantScope.Begin(message.Metadata.TenantId);");
    }

    [Fact]
    public void With_entra_id_the_tenant_column_adr_says_why_there_are_no_shared_records_yet()
    {
        var adr = Adr(Files(Profile, "backend.auth=entra-id", Roles), "Tenancy: a tenant column");

        adr.ShouldContain("`WritesSharedRecords()` is not generated with Entra ID");
    }

    [Fact]
    public void The_schema_per_tenant_adr_says_why_it_has_no_shared_records()
    {
        var adr = Adr(Files(Profile, "backend.tenancy=schema-per-tenant", Roles), "Tenancy: a schema per tenant");

        adr.ShouldContain("no records shared by all tenants");
        adr.ShouldContain("a database outside every tenant's");
    }

    [Fact]
    public void The_rag_adr_and_agent_rules_describe_the_shared_vector_index()
    {
        var files = Files(WithRag, RolesAndClaims);
        var adr = Adr(files, "Retrieval: pgvector");
        var agents = files["AGENTS.md"];

        adr.ShouldContain("`SharedVectorIndex<TContext>`");
        adr.ShouldContain("`shared_embeddings (key text primary key, content text not null, embedding vector(N) not null)`");
        adr.ShouldContain("never another tenant's");
        agents.ShouldContain("`SharedVectorIndex<TContext>`");
    }

    /// <summary>
    /// The decision against the running Aspire stack, in the pilot's choices: staff write an article through an endpoint
    /// that opts in and both tenants' users read it, every other way of writing one fails and changes nothing, each
    /// tenant's notes stay its own, and an event published with an article arrives without a tenant. Then the generated
    /// rag tests, shared vectors included, against the AppHost's database.
    /// </summary>
    [Fact]
    public void Staff_write_shared_records_that_every_tenant_reads_and_nothing_else_writes_them()
    {
        using var project = new GeneratedProject(WithRag, RolesAndClaims);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", ArticlesEndpoints);
        project.WriteTypes("src/Modules/Billing/Billing.Application", ArticlePublishedConsumer);
        project.Write("src/Contracts/Billing/ArticlePublishedV1.cs", ArticlePublishedEvent);
        project.Write("tests/Integration.Tests/SharedRecordsTests.cs", SharedRecordsIntegrationTests);
        AddArticlesAndNotesWithAMigration(project);

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0", customMessage: output);

        var (ragExitCode, ragOutput) = project.Test("tests/Rag.Tests/Rag.Tests.csproj");

        ragExitCode.ShouldBe(0, ragOutput);
        ragOutput.ShouldContain("failed: 0", customMessage: ragOutput);
        ragOutput.ShouldNotContain("succeeded: 0", customMessage: ragOutput);
    }

    [Fact]
    public void An_endpoint_writing_shared_records_without_the_staff_role_or_with_across_tenants_fails_the_architecture_tests_naming_it()
    {
        using var project = new GeneratedProject(Profile, Roles);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Domain", ArticleAndNote);
        project.WriteTypes("src/Modules/Billing/Billing.Api/Endpoints", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Http.HttpResults;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Modules.Billing.Application;

            namespace Shop.Modules.Billing.Api.Endpoints;

            public sealed class Articles : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints)
                {
                    endpoints.MapPost("/articles", Nothing).RequireAuthorization(BillingPolicyNames.WriteSharedRecords).WritesSharedRecords().ExcludeFromDescription();
                    endpoints.MapPost("/articles/managed", Nothing).RequireAuthorization(BillingPolicyNames.Manage).WritesSharedRecords().ExcludeFromDescription();
                    endpoints.MapPost("/articles/signed-in", Nothing).WritesSharedRecords().ExcludeFromDescription();
                    endpoints.MapPost("/articles/anonymous", Nothing).RequireAuthorization(BillingPolicyNames.WriteSharedRecords).AllowAnonymous().WritesSharedRecords().ExcludeFromDescription();
                    endpoints.MapPost("/articles/both", Nothing).RequireAuthorization(BillingPolicyNames.WriteSharedRecords).WritesSharedRecords().AcrossTenants().ExcludeFromDescription();
                }

                private static NoContent Nothing() => TypedResults.NoContent();
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("POST /billing/articles/managed writes records shared by all tenants (WritesSharedRecords()), but its authorization does not require the role staff (CrossTenantRole.Name).", Case.Sensitive, output);
        output.ShouldContain("POST /billing/articles/signed-in writes records shared by all tenants (WritesSharedRecords()), but its authorization does not require the role staff (CrossTenantRole.Name).", Case.Sensitive, output);
        output.ShouldContain("POST /billing/articles/anonymous writes records shared by all tenants (WritesSharedRecords()), but its authorization does not require the role staff (CrossTenantRole.Name).", Case.Sensitive, output);
        output.ShouldContain("POST /billing/articles/both is mapped with both WritesSharedRecords() and AcrossTenants()", Case.Sensitive, output);
        output.ShouldNotContain("POST /billing/articles writes records shared by all tenants", Case.Sensitive, output);
    }

    [Fact]
    public void A_shared_type_that_references_a_tenants_type_fails_the_model_build()
    {
        using var project = new GeneratedProject(Profile, Roles);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Domain", ArticleAndNote + """

            public sealed class Guide(Guid id, Guid noteId) : Entity<Guid>(id)
            {
                public Guid NoteId { get; private set; } = noteId;
            }
            """);
        WriteConfigurations(project, """
            public sealed class GuideConfiguration : IEntityTypeConfiguration<Guide>
            {
                public void Configure(EntityTypeBuilder<Guide> builder)
                {
                    builder.ToTable("guides");
                    builder.HasKey(guide => guide.Id);
                    builder.HasOne<Note>().WithMany().HasForeignKey(guide => guide.NoteId);
                    builder.SharedByAllTenants();
                }
            }
            """);

        var (exitCode, output) = AddMigration(project);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Guide is shared by all tenants, but references Note, whose records each belong to one tenant.", Case.Sensitive, output);
    }

    [Fact]
    public void A_type_declared_shared_below_the_root_of_its_hierarchy_fails_the_model_build()
    {
        using var project = new GeneratedProject(Profile, Roles);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Domain", """
            using Shop.BuildingBlocks.Domain;

            namespace Shop.Modules.Billing.Domain;

            public class Document(Guid id) : Entity<Guid>(id);

            public sealed class Handbook(Guid id) : Document(id);
            """);
        WriteConfigurations(project, """
            public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
            {
                public void Configure(EntityTypeBuilder<Document> builder)
                {
                    builder.ToTable("documents");
                    builder.HasKey(document => document.Id);
                }
            }

            public sealed class HandbookConfiguration : IEntityTypeConfiguration<Handbook>
            {
                public void Configure(EntityTypeBuilder<Handbook> builder) => builder.SharedByAllTenants();
            }
            """);

        var (exitCode, output) = AddMigration(project);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Handbook is declared shared by all tenants, but derives from Document: declare SharedByAllTenants() on Document, the root of its hierarchy.", Case.Sensitive, output);
    }

    private const string ArticleAndNote = """
        using Shop.BuildingBlocks.Domain;

        namespace Shop.Modules.Billing.Domain;

        public sealed class Article(Guid id, string title) : Entity<Guid>(id)
        {
            public string Title { get; private set; } = title;

            public void Rename(string title) => Title = title;
        }

        public sealed class Note(Guid id, string text) : Entity<Guid>(id)
        {
            public string Text { get; private set; } = text;
        }
        """;

    /// <summary>A shared type whose table name the provider must quote, as it holds a space.</summary>
    private const string Manual = """
        using Shop.BuildingBlocks.Domain;

        namespace Shop.Modules.Billing.Domain;

        public sealed class Manual(Guid id, string title) : Entity<Guid>(id)
        {
            public string Title { get; private set; } = title;
        }
        """;

    private const string ArticleAndNoteConfigurations = """
        public sealed class ArticleConfiguration : IEntityTypeConfiguration<Article>
        {
            public void Configure(EntityTypeBuilder<Article> builder)
            {
                builder.ToTable("articles");
                builder.HasKey(article => article.Id);
                builder.Property(article => article.Title);
                builder.SharedByAllTenants();
            }
        }

        public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
        {
            public void Configure(EntityTypeBuilder<Note> builder)
            {
                builder.ToTable("notes");
                builder.HasKey(note => note.Id);
                builder.Property(note => note.Text);
            }
        }

        public sealed class ManualConfiguration : IEntityTypeConfiguration<Manual>
        {
            public void Configure(EntityTypeBuilder<Manual> builder)
            {
                builder.ToTable("Shared Manuals");
                builder.HasKey(manual => manual.Id);
                builder.Property(manual => manual.Title);
                builder.SharedByAllTenants();
            }
        }
        """;

    private const string ArticlePublishedEvent = """
        namespace Shop.Contracts.Billing;

        public sealed record ArticlePublishedV1(string Title) : IntegrationEvent;
        """;

    /// <summary>Records each delivery: the tenant its metadata carries and the tenant the consumer works in inside <c>TenantScope.Begin(message.Metadata)</c>.</summary>
    private const string ArticlePublishedConsumer = """
        using System.Collections.Concurrent;
        using Shop.BuildingBlocks.Tenancy;
        using Shop.Contracts.Billing;

        namespace Shop.Modules.Billing.Application;

        public sealed class ArticlePublishedConsumer
        {
            private readonly ConcurrentQueue<string> deliveries = Deliveries;

            public static ConcurrentQueue<string> Deliveries { get; } = [];

            public void Handle(ArticlePublishedV1 message, ITenantContext tenants)
            {
                using var tenant = TenantScope.Begin(message.Metadata);
                deliveries.Enqueue($"{message.Title}: published in tenant {message.Metadata.TenantId ?? "none"}, handled in tenant {tenants.TenantId ?? "none"}");
            }
        }
        """;

    /// <summary>
    /// The billing module's articles (shared by all tenants) and notes (each tenant's own), out of the OpenAPI document
    /// so the frontend's copy stays current: one way to write an article that opts in, and every way that must fail.
    /// </summary>
    private const string ArticlesEndpoints = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.EntityFrameworkCore;
        using Shop.BuildingBlocks.Api;
        using Shop.Contracts.Billing;
        using Shop.Modules.Billing.Application;
        using Shop.Modules.Billing.Domain;
        using Shop.Modules.Billing.Infrastructure.Persistence;

        namespace Shop.Modules.Billing.Api.Endpoints;

        public sealed record NewArticle(string Title);

        public sealed record NewNote(string Text);

        public sealed class Articles : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapPost("/articles", AddAsync).RequireAuthorization(BillingPolicyNames.WriteSharedRecords).WritesSharedRecords().ExcludeFromDescription();
                endpoints.MapPost("/articles/published", PublishAsync).RequireAuthorization(BillingPolicyNames.WriteSharedRecords).WritesSharedRecords().ExcludeFromDescription();
                endpoints.MapPost("/articles/with-a-note", AddWithANoteAsync).RequireAuthorization(BillingPolicyNames.WriteSharedRecords).WritesSharedRecords().ExcludeFromDescription();
                endpoints.MapPost("/articles/without-opting-in", AddAsync).RequireAuthorization(BillingPolicyNames.WriteSharedRecords).ExcludeFromDescription();
                endpoints.MapPost("/articles/by-a-tenant", AddAsync).RequireAuthorization().ExcludeFromDescription();
                endpoints.MapPut("/articles/by-a-tenant", RenameAllAsync).RequireAuthorization().ExcludeFromDescription();
                endpoints.MapDelete("/articles/by-a-tenant", DeleteAllAsync).RequireAuthorization().ExcludeFromDescription();
                endpoints.MapGet("/articles", ListAsync).RequireAuthorization().ExcludeFromDescription();
                endpoints.MapGet("/articles/all-tenants", ListAsync).RequireAuthorization(BillingPolicyNames.ReadAllTenants).AcrossTenants().ExcludeFromDescription();
                endpoints.MapGet("/articles/deliveries", () => ArticlePublishedConsumer.Deliveries.ToArray()).RequireAuthorization().ExcludeFromDescription();
                endpoints.MapPost("/manuals", AddManualAsync).RequireAuthorization(BillingPolicyNames.WriteSharedRecords).WritesSharedRecords().ExcludeFromDescription();
                endpoints.MapGet("/manuals", ListManualsAsync).RequireAuthorization().ExcludeFromDescription();
                endpoints.MapPut("/manuals/by-a-tenant", RenameManualsAsync).RequireAuthorization().ExcludeFromDescription();
                endpoints.MapDelete("/manuals/by-a-tenant", DeleteManualsAsync).RequireAuthorization().ExcludeFromDescription();
                endpoints.MapPost("/notes", AddNoteAsync).RequireAuthorization().ExcludeFromDescription();
                endpoints.MapGet("/notes", ListNotesAsync).RequireAuthorization().ExcludeFromDescription();
            }

            private static async Task<IResult> AddAsync(NewArticle article, BillingDbContext context, CancellationToken cancellationToken)
            {
                context.Add(new Article(Guid.CreateVersion7(), article.Title));
                await context.SaveChangesAsync(cancellationToken);
                return TypedResults.NoContent();
            }

            private static async Task<IResult> PublishAsync(NewArticle article, BillingDbContext context, IBillingUnitOfWork unitOfWork, CancellationToken cancellationToken)
            {
                context.Add(new Article(Guid.CreateVersion7(), article.Title));
                await unitOfWork.PublishAsync(new ArticlePublishedV1(article.Title));
                await unitOfWork.CommitAsync(cancellationToken);
                return TypedResults.NoContent();
            }

            private static async Task<IResult> AddWithANoteAsync(NewArticle article, BillingDbContext context, CancellationToken cancellationToken)
            {
                context.Add(new Article(Guid.CreateVersion7(), article.Title));
                context.Add(new Note(Guid.CreateVersion7(), article.Title));
                await context.SaveChangesAsync(cancellationToken);
                return TypedResults.NoContent();
            }

            private static async Task<IResult> RenameAllAsync(BillingDbContext context, CancellationToken cancellationToken)
            {
                foreach (var article in await context.Set<Article>().ToListAsync(cancellationToken))
                {
                    article.Rename("renamed by a tenant");
                }

                await context.SaveChangesAsync(cancellationToken);
                return TypedResults.NoContent();
            }

            // A bulk write bypasses SaveChanges and the change tracker, and a shared type has no query filter.
            private static async Task<IResult> DeleteAllAsync(BillingDbContext context, CancellationToken cancellationToken) =>
                TypedResults.Ok(await context.Set<Article>().ExecuteDeleteAsync(cancellationToken));

            private static async Task<IResult> ListAsync(BillingDbContext context, CancellationToken cancellationToken) =>
                TypedResults.Ok(await context.Set<Article>().Select(article => article.Title).Order().ToListAsync(cancellationToken));

            private static async Task<IResult> AddManualAsync(NewArticle manual, BillingDbContext context, CancellationToken cancellationToken)
            {
                context.Add(new Manual(Guid.CreateVersion7(), manual.Title));
                await context.SaveChangesAsync(cancellationToken);
                return TypedResults.NoContent();
            }

            private static async Task<IResult> ListManualsAsync(BillingDbContext context, CancellationToken cancellationToken) =>
                TypedResults.Ok(await context.Set<Manual>().Select(manual => manual.Title).Order().ToListAsync(cancellationToken));

            private static async Task<IResult> RenameManualsAsync(BillingDbContext context, CancellationToken cancellationToken) =>
                TypedResults.Ok(await context.Set<Manual>().ExecuteUpdateAsync(manual => manual.SetProperty(row => row.Title, "renamed by a tenant"), cancellationToken));

            private static async Task<IResult> DeleteManualsAsync(BillingDbContext context, CancellationToken cancellationToken) =>
                TypedResults.Ok(await context.Set<Manual>().ExecuteDeleteAsync(cancellationToken));

            private static async Task<IResult> AddNoteAsync(NewNote note, BillingDbContext context, CancellationToken cancellationToken)
            {
                context.Add(new Note(Guid.CreateVersion7(), note.Text));
                await context.SaveChangesAsync(cancellationToken);
                return TypedResults.NoContent();
            }

            private static async Task<IResult> ListNotesAsync(BillingDbContext context, CancellationToken cancellationToken) =>
                TypedResults.Ok(await context.Set<Note>().Select(note => note.Text).Order().ToListAsync(cancellationToken));
        }
        """;

    private const string SharedRecordsIntegrationTests = """
        using System.Net;
        using System.Net.Http.Json;

        namespace Shop.Integration.Tests;

        public class SharedRecordsTests(AspireFixture aspire)
        {
            private static readonly Uri Articles = new("/billing/articles", UriKind.Relative);
            private static readonly Uri Notes = new("/billing/notes", UriKind.Relative);
            private static readonly Uri ByATenant = new("/billing/articles/by-a-tenant", UriKind.Relative);

            [Fact]
            public async Task Staff_write_an_article_through_an_endpoint_that_opts_in_and_both_tenants_users_read_it()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var staff = await aspire.CreateHostClientAsync("staff");

                using var response = await staff.PostAsJsonAsync(Articles, new { Title = "an article for every tenant" }, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                (await TitlesAsync("developer", Articles, cancellationToken)).ShouldContain("an article for every tenant");
                (await TitlesAsync("developer-b", Articles, cancellationToken)).ShouldContain("an article for every tenant");
                (await TitlesAsync("staff", Articles, cancellationToken)).ShouldContain("an article for every tenant");
                (await TitlesAsync("staff", new Uri("/billing/articles/all-tenants", UriKind.Relative), cancellationToken)).ShouldContain("an article for every tenant");
            }

            [Fact]
            public async Task Each_tenants_notes_stay_invisible_to_the_other_tenant()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var developer = await aspire.CreateHostClientAsync("developer");
                using var response = await developer.PostAsJsonAsync(Notes, new { Text = "a note of tenant a beside the articles" }, cancellationToken);
                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

                (await TitlesAsync("developer", Notes, cancellationToken)).ShouldContain("a note of tenant a beside the articles");
                (await TitlesAsync("developer-b", Notes, cancellationToken)).ShouldNotContain("a note of tenant a beside the articles");
            }

            [Fact]
            public async Task A_tenants_user_is_forbidden_the_endpoint_that_opts_in()
            {
                using var developer = await aspire.CreateHostClientAsync("developer");

                using var response = await developer.PostAsJsonAsync(Articles, new { Title = "an article a tenant may not write" }, TestContext.Current.CancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            }

            [Fact]
            public async Task A_tenants_user_saving_an_article_fails_and_saves_nothing()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var developer = await aspire.CreateHostClientAsync("developer");

                using var response = await developer.PostAsJsonAsync(ByATenant, new { Title = "an article saved by a tenant" }, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
                (await TitlesAsync("developer-b", Articles, cancellationToken)).ShouldNotContain("an article saved by a tenant");
            }

            [Fact]
            public async Task A_tenants_user_renaming_articles_fails_and_changes_nothing()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await AddAsync("an article no tenant renames", cancellationToken);
                using var developer = await aspire.CreateHostClientAsync("developer");

                using var response = await developer.PutAsync(ByATenant, null, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
                var titles = await TitlesAsync("developer-b", Articles, cancellationToken);
                titles.ShouldContain("an article no tenant renames");
                titles.ShouldNotContain("renamed by a tenant");
            }

            [Fact]
            public async Task A_tenants_bulk_delete_of_articles_fails_and_deletes_nothing()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await AddAsync("an article no tenant deletes", cancellationToken);
                using var developer = await aspire.CreateHostClientAsync("developer");

                using var response = await developer.DeleteAsync(ByATenant, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
                (await TitlesAsync("developer-b", Articles, cancellationToken)).ShouldContain("an article no tenant deletes");
            }

            [Fact]
            public async Task A_tenants_bulk_update_and_delete_of_a_table_whose_name_needs_quoting_fail_and_change_nothing()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                var manuals = new Uri("/billing/manuals", UriKind.Relative);
                var byATenant = new Uri("/billing/manuals/by-a-tenant", UriKind.Relative);
                using var staff = await aspire.CreateHostClientAsync("staff");
                using (var added = await staff.PostAsJsonAsync(manuals, new { Title = "a manual no tenant changes" }, cancellationToken))
                {
                    added.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                }

                using var developer = await aspire.CreateHostClientAsync("developer");

                using var renamed = await developer.PutAsync(byATenant, null, cancellationToken);
                using var deleted = await developer.DeleteAsync(byATenant, cancellationToken);

                renamed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
                deleted.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
                var titles = await TitlesAsync("developer-b", manuals, cancellationToken);
                titles.ShouldContain("a manual no tenant changes");
                titles.ShouldNotContain("renamed by a tenant");
            }

            [Fact]
            public async Task Staff_cannot_write_an_article_on_an_endpoint_that_does_not_opt_in()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var staff = await aspire.CreateHostClientAsync("staff");

                using var response = await staff.PostAsJsonAsync(new Uri("/billing/articles/without-opting-in", UriKind.Relative), new { Title = "an article without the opt-in" }, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
                (await TitlesAsync("developer", Articles, cancellationToken)).ShouldNotContain("an article without the opt-in");
            }

            [Fact]
            public async Task A_tenants_row_saved_beside_an_article_fails_and_saves_neither()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var staff = await aspire.CreateHostClientAsync("staff");

                using var response = await staff.PostAsJsonAsync(new Uri("/billing/articles/with-a-note", UriKind.Relative), new { Title = "an article with a note" }, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
                (await TitlesAsync("developer", Articles, cancellationToken)).ShouldNotContain("an article with a note");
            }

            [Fact]
            public async Task An_event_published_with_an_article_arrives_without_a_tenant()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                using var staff = await aspire.CreateHostClientAsync("staff");

                using var response = await staff.PostAsJsonAsync(new Uri("/billing/articles/published", UriKind.Relative), new { Title = "a published article" }, cancellationToken);

                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                var expected = "a published article: published in tenant none, handled in tenant none";
                string[] deliveries = [];
                for (var attempt = 0; attempt < 60 && !deliveries.Contains(expected); attempt++)
                {
                    await Task.Delay(500, cancellationToken);
                    deliveries = await TitlesAsync("developer", new Uri("/billing/articles/deliveries", UriKind.Relative), cancellationToken);
                }

                deliveries.ShouldContain(expected);
            }

            private async Task AddAsync(string title, CancellationToken cancellationToken)
            {
                using var staff = await aspire.CreateHostClientAsync("staff");
                using var response = await staff.PostAsJsonAsync(Articles, new { Title = title }, cancellationToken);
                response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            private async Task<string[]> TitlesAsync(string user, Uri uri, CancellationToken cancellationToken)
            {
                using var client = await aspire.CreateHostClientAsync(user);
                var titles = await client.GetFromJsonAsync<string[]>(uri, cancellationToken);
                titles.ShouldNotBeNull();
                return titles;
            }
        }
        """;

    private static void AddArticlesAndNotesWithAMigration(GeneratedProject project)
    {
        project.WriteTypes("src/Modules/Billing/Billing.Domain", ArticleAndNote);
        project.WriteTypes("src/Modules/Billing/Billing.Domain", Manual);
        WriteConfigurations(project, ArticleAndNoteConfigurations);
        var (exitCode, output) = AddMigration(project);
        exitCode.ShouldBe(0, output);
        project.Build();
    }

    private static void WriteConfigurations(GeneratedProject project, string configurations) =>
        project.WriteTypes("src/Modules/Billing/Billing.Infrastructure/Persistence", $"""
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Shop.Modules.Billing.Domain;

            namespace Shop.Modules.Billing.Infrastructure.Persistence;

            {configurations}
            """);

    /// <summary>Builds the project and adds the billing module's first migration, which builds its model.</summary>
    private static (int ExitCode, string Output) AddMigration(GeneratedProject project)
    {
        project.Build();
        var none = new Dictionary<string, string>();
        var (restoreExitCode, restoreOutput) = project.DotNetCommand(none, "tool", "restore");
        restoreExitCode.ShouldBe(0, restoreOutput);
        return project.DotNetCommand(
            none,
            "ef", "migrations", "add", "Initial",
            "--project", "src/Modules/Billing/Billing.Infrastructure",
            "--startup-project", "src/MigrationRunner",
            "--context", "BillingDbContext");
    }

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
