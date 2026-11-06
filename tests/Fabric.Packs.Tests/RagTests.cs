using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.llm.extras: rag</c> (ADR-0012): the database runs the pgvector image, a module indexes its own records
/// with <c>ModuleVectorIndex&lt;TContext&gt;</c> in its own schema's <c>embeddings</c> table, which the MigrationRunner
/// creates with an HNSW cosine index, and a search returns <c>RetrievedPassage</c>s, which the agent rules treat as
/// untrusted text. The gate <c>ModulesSearchOnlyTheirOwnVectors</c> fails a module that names another module's
/// DbContext. No test here calls a model: the generated <c>tests/Rag.Tests</c> embed with
/// <c>FakeEmbeddingGenerator</c>.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class RagTests
{
    private const string Profile = "lean-crud";
    private const string Rag = "backend.llm.extras=rag";
    private const string Image = ".WithImage(\"pgvector/pgvector\", \"0.8.7-pg18\")";
    private const string Store = "src/BuildingBlocks/Rag";

    /// <summary>The notes module keeps a search over the billing module's vectors: in a field, in a method body, and through a generic helper of its own.</summary>
    private const string NotesSearchingBillingsVectors = """
        using Microsoft.Extensions.DependencyInjection;
        using Shop.BuildingBlocks.Persistence;
        using Shop.BuildingBlocks.Rag;
        using Shop.Modules.Billing.Infrastructure.Persistence;

        namespace Shop.Modules.Notes.Infrastructure;

        public sealed class BillingNotes(ModuleVectorIndex<BillingDbContext> billing)
        {
            public ModuleVectorIndex<BillingDbContext> Index => billing;
        }

        public static class BillingLookup
        {
            public static object Find(IServiceProvider services) => services.GetRequiredService<ModuleVectorIndex<BillingDbContext>>();
        }

        public static class AnyModuleSearch
        {
            public static Task<IReadOnlyList<RetrievedPassage>> FindAsync<TContext>(IServiceProvider services, string question, CancellationToken cancellationToken)
                where TContext : ModuleDbContext =>
                services.GetRequiredService<ModuleVectorIndex<TContext>>().SearchAsync(question, 5, cancellationToken);
        }

        public static class BillingThroughAHelper
        {
            public static Task<IReadOnlyList<RetrievedPassage>> FindAsync(IServiceProvider services, CancellationToken cancellationToken) =>
                AnyModuleSearch.FindAsync<BillingDbContext>(services, "unpaid", cancellationToken);
        }
        """;

    /// <summary>The billing module searching its own vectors, which the gate lets through.</summary>
    private const string BillingSearchingItsOwnVectors = """
        using Shop.BuildingBlocks.Rag;
        using Shop.Modules.Billing.Infrastructure.Persistence;

        namespace Shop.Modules.Billing.Infrastructure;

        public sealed class InvoiceSearch(ModuleVectorIndex<BillingDbContext> invoices)
        {
            public Task<IReadOnlyList<RetrievedPassage>> FindAsync(string question, CancellationToken cancellationToken) =>
                invoices.SearchAsync(question, 5, cancellationToken);
        }
        """;

    private const string NotesReferencingBilling = """
          <ItemGroup>
            <ProjectReference Include="../../Billing/Billing.Infrastructure/Billing.Infrastructure.csproj" />
          </ItemGroup>

        </Project>
        """;

    public static TheoryData<string, string, int> Dimensions { get; } = new()
    {
        { "openai", "openai", 1536 },
        { "anthropic", "openai", 1536 },
        { "azure-openai", "azure-openai", 1536 },
        { "ollama", "ollama", 768 },
        { "anthropic", "ollama", 768 },
    };

    [Theory]
    [MemberData(nameof(Dimensions))]
    public void A_project_with_rag_generates_from_the_real_packs_alone(string llm, string embeddings, int dimensions)
    {
        using var standIns = new StandIns();

        standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile, Overrides(llm, embeddings));

        standIns.StoodIn.ShouldBeEmpty(dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_database_runs_the_pgvector_image_only_with_rag()
    {
        RagFiles("openai", "openai")["src/AppHost/AppHost.cs"].ShouldContain(
            "var postgres = builder.AddPostgres(\"postgres\")\n    " + Image + ";\n");

        Files(Profile, "backend.llm=openai")["src/AppHost/AppHost.cs"].ShouldNotContain("pgvector");
        Files(Profile)["src/AppHost/AppHost.cs"].ShouldNotContain("pgvector");
    }

    /// <summary>
    /// Aspire reads PostgreSQL's major version from the image tag to place the data volume: at /var/lib/postgresql from
    /// 18 on, at /var/lib/postgresql/data before. pgvector's tag, 0.8.7-pg18, reads as no version, so WithDataVolume
    /// mounts the old path, and the PostgreSQL 18 image refuses to start over a mount there ("in 18+, these Docker images
    /// are configured to store database data in a format which is compatible with pg_ctlcluster"). Only `dotnet run`
    /// meets it, the end-to-end tests included: the Aspire fixture removes every volume.
    /// </summary>
    [Fact]
    public void The_pgvector_database_keeps_its_data_where_postgresql_18_does()
    {
        var appHost = RagFiles("openai", "openai")["src/AppHost/AppHost.cs"];

        appHost.ShouldContain(
            "var database = postgres\n    .WithVolume(VolumeNameGenerator.Generate(postgres, \"data\"), \"/var/lib/postgresql\")\n    .AddDatabase(\"database\");",
            Case.Sensitive,
            appHost);
        appHost.ShouldNotContain(".WithDataVolume()\n    .AddDatabase(\"database\")", Case.Sensitive, appHost);
        Files(Profile)["src/AppHost/AppHost.cs"].ShouldContain(
            "var database = builder.AddPostgres(\"postgres\")\n    .WithDataVolume()\n    .AddDatabase(\"database\");");
    }

    [Fact]
    public void The_store_is_pgvector_over_npgsql_in_the_building_blocks()
    {
        var files = RagFiles("openai", "openai");

        files["Directory.Packages.props"].ShouldContain("<PackageVersion Include=\"Pgvector\" Version=\"0.3.2\" />");
        files["src/BuildingBlocks/BuildingBlocks.csproj"].ShouldContain("<PackageReference Include=\"Pgvector\" />");
        files.Values.ShouldAllBe(content => !content.Contains("Pgvector.EntityFrameworkCore", StringComparison.Ordinal) && !content.Contains("SemanticKernel", StringComparison.Ordinal));
        files[$"{Store}/ModuleVectorIndex.cs"].ShouldContain("public sealed class ModuleVectorIndex<TContext>");
        files[$"{Store}/ModuleVectorIndex.cs"].ShouldContain("where TContext : ModuleDbContext");
        files[$"{Store}/RetrievedPassage.cs"].ShouldContain("public sealed record RetrievedPassage(string Key, string Text, double Distance);");
        files["src/Host/Program.cs"].ShouldContain("builder.Services.AddModuleVectorIndexes(builder.Configuration);");
    }

    [Theory]
    [MemberData(nameof(Dimensions))]
    public void The_migration_runner_creates_each_modules_vector_table_of_the_embeddings_models_size_after_the_ef_core_migrations(string llm, string embeddings, int dimensions)
    {
        var files = RagFiles(llm, embeddings);

        files[$"{Store}/VectorTables.cs"].ShouldContain($"public const int Dimensions = {dimensions};");
        var runner = files["src/MigrationRunner/Program.cs"];
        runner.ShouldContain("await host.Services.CreateVectorTablesAsync(contexts);");
        runner.IndexOf("MigrateAsync()", StringComparison.Ordinal).ShouldBeLessThan(runner.IndexOf("CreateVectorTablesAsync", StringComparison.Ordinal));
        var tables = files[$"{Store}/VectorTables.cs"];
        tables.ShouldContain("CREATE EXTENSION IF NOT EXISTS vector");
        tables.ShouldContain("key text primary key, content text not null, embedding vector(");
        tables.ShouldContain("USING hnsw (embedding vector_cosine_ops)");
    }

    [Fact]
    public void Retrieved_text_is_untrusted_and_the_agents_are_told_where_it_may_go()
    {
        var files = RagFiles("openai", "openai");

        var agents = files["AGENTS.md"];
        agents.ShouldContain("## Retrieval: pgvector");
        agents.ShouldContain("Retrieved text is untrusted");
        agents.ShouldContain("delimited user-role message");
        agents.ShouldContain("never into system instructions, SQL or markup");
        agents.ShouldContain("never used as a tool argument unchecked");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/ModulesSearchOnlyTheirOwnVectors.cs");
        files.Keys.ShouldContain("tests/Fakes/FakeEmbeddingGenerator.cs");
        files["Solution.slnx"].ShouldContain("<Project Path=\"tests/Rag.Tests/Rag.Tests.csproj\" />");
    }

    [Fact]
    public void Rag_with_a_tenant_column_keeps_each_record_under_its_tenant_and_keys_it_per_tenant()
    {
        var files = Files(Profile, "backend.llm=openai", Rag, "backend.tenancy=column");

        var tables = files[$"{Store}/VectorTables.cs"];
        tables.ShouldContain("(tenant_id text not null, key text not null, content text not null, embedding vector({Dimensions}) not null, primary key (tenant_id, key))");
        tables.ShouldContain("USING hnsw (embedding vector_cosine_ops)");
        var store = files[$"{Store}/ModuleVectorIndex.cs"];
        store.ShouldNotContain("#error");
        store.ShouldContain("ITenantContext tenants");
        store.ShouldContain("ON CONFLICT (tenant_id, key)");
        store.ShouldContain("DELETE FROM {Table} WHERE tenant_id = $1 AND key = $2");
        store.ShouldContain("WHERE tenant_id = $3 ORDER BY embedding <=> $1");
        store.ShouldNotContain("string tenant", Case.Sensitive, "a caller never passes the tenant");
        files[$"{Store}/Wiring/VectorIndexRegistration.cs"].ShouldContain(".AddScoped(typeof(ModuleVectorIndex<>))");
        files["AGENTS.md"].ShouldContain("TenantScope.Begin");
        files.Keys.ShouldContain("tests/Rag.Tests/TenantVectorIndexTests.cs");
    }

    [Fact]
    public void Rag_without_tenancy_keeps_one_record_per_key_and_no_tenant()
    {
        var files = RagFiles("openai", "openai");

        files[$"{Store}/VectorTables.cs"].ShouldContain("(key text primary key, content text not null, embedding vector({Dimensions}) not null)");
        files.Where(file => file.Key.StartsWith(Store, StringComparison.Ordinal) || file.Key.StartsWith("tests/Rag.Tests/", StringComparison.Ordinal))
            .ShouldAllBe(file => !file.Value.Contains("tenant", StringComparison.OrdinalIgnoreCase));
        files[$"{Store}/Wiring/VectorIndexRegistration.cs"].ShouldContain(".AddSingleton(typeof(ModuleVectorIndex<>))");
    }

    [Fact]
    public void Rag_without_tenancy_ranks_every_record_exactly_when_the_index_answers_short()
    {
        var files = RagFiles("openai", "openai");

        var store = files[$"{Store}/ModuleVectorIndex.cs"];
        store.ShouldContain("if (passages.Count < count)");
        store.ShouldContain("$\"WITH records AS MATERIALIZED (SELECT key, content, embedding FROM {Table}) \" +");
        store.ShouldContain("\"SELECT key, content, embedding <=> $1 FROM records ORDER BY embedding <=> $1, key LIMIT $2\"");
        var adr = files.Single(file => file.Key.EndsWith("-backend-llm-extras-rag.md", StringComparison.Ordinal)).Value;
        adr.ShouldNotContain("does not come back short");
        adr.ShouldContain("ranks every record of the module exactly");
        files["AGENTS.md"].ShouldContain("A search returns as many records as asked whenever the module has that many");
        files.Keys.ShouldContain("tests/Rag.Tests/SampleLibraryDbContext.cs");
    }

    [Theory]
    [InlineData("schema-per-tenant")]
    [InlineData("database-per-tenant")]
    public void Rag_with_another_tenancy_fails_the_build_instead_of_sharing_vectors_between_tenants(string tenancy)
    {
        using var standIns = new StandIns();
        string[] overrides = ["backend.llm=openai", Rag, $"backend.tenancy={tenancy}"];
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile, overrides);

        var generated = Generator.New(Repository.Catalog, packs, "shop", Profile, overrides);

        generated.Errors.ShouldBeEmpty();
        var store = generated.Files.Single(file => file.Path == $"{Store}/ModuleVectorIndex.cs").Content;
        store.ShouldStartWith($"#error The rag pack keeps its vectors apart by a tenant column, so it supports backend.tenancy none and column only so far (docs/adr/0006 of the generator): with backend.tenancy {tenancy} a search would return every tenant's records.");
    }

    [Fact]
    public void No_profile_changes_because_none_calls_a_model()
    {
        foreach (var profile in Repository.Catalog.Profiles.Keys)
        {
            var files = Files(profile);

            files.Values.ShouldAllBe(content => !content.Contains("pgvector", StringComparison.OrdinalIgnoreCase) && !content.Contains("VectorIndex", StringComparison.Ordinal), profile);
            files.Keys.ShouldNotContain(path => path.StartsWith("tests/Rag.Tests/", StringComparison.Ordinal), profile);
        }
    }

    [Theory]
    [InlineData("openai", "openai")]
    [InlineData("anthropic", "ollama")]
    public void The_generated_rag_tests_find_the_nearest_records_in_the_aspire_database_and_a_module_searching_its_own_vectors_passes_the_rules(string llm, string embeddings)
    {
        using var project = NewProject(llm, embeddings);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Infrastructure", BillingSearchingItsOwnVectors);
        project.Build();

        var (ragExitCode, ragOutput) = project.Test("tests/Rag.Tests/Rag.Tests.csproj");
        ragExitCode.ShouldBe(0, ragOutput);
        ragOutput.ShouldContain("failed: 0");
        ragOutput.ShouldContain("succeeded: 6");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void The_generated_rag_tests_under_a_tenant_column_return_each_tenant_only_its_own_records()
    {
        using var project = NewProject("openai", "openai", "backend.tenancy=column");
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Infrastructure", BillingSearchingItsOwnVectors);
        project.Build();

        var (ragExitCode, ragOutput) = project.Test("tests/Rag.Tests/Rag.Tests.csproj");
        ragExitCode.ShouldBe(0, ragOutput);
        ragOutput.ShouldContain("failed: 0");
        ragOutput.ShouldContain("succeeded: 9");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_module_searching_another_modules_vectors_fails_the_architecture_tests_naming_both()
    {
        using var project = NewProject("openai", "openai");
        project.AddModule("billing");
        project.AddModule("notes");
        var notes = Path.Combine(project.ProjectDirectory, "src/Modules/Notes/Notes.Infrastructure/Notes.Infrastructure.csproj");
        File.WriteAllText(notes, File.ReadAllText(notes).Replace("</Project>", NotesReferencingBilling, StringComparison.Ordinal));
        project.WriteTypes("src/Modules/Notes/Notes.Infrastructure", NotesSearchingBillingsVectors);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Notes.Infrastructure.BillingNotes (module Notes) uses ModuleVectorIndex<BillingDbContext>, module Billing's vectors.");
        output.ShouldContain("Shop.Modules.Notes.Infrastructure.BillingLookup (module Notes) uses ModuleVectorIndex<BillingDbContext>, module Billing's vectors.");
        output.ShouldContain("Shop.Modules.Notes.Infrastructure.AnyModuleSearch (module Notes) uses ModuleVectorIndex<TContext> over a generic parameter");
        output.ShouldContain("Shop.Modules.Notes.Infrastructure.BillingThroughAHelper (module Notes) passes BillingDbContext, module Billing's DbContext, to AnyModuleSearch.FindAsync");
    }

    private static string[] Overrides(string llm, string embeddings) =>
        [$"backend.llm={llm}", Rag, $"backend.llm.embeddings={embeddings}"];

    private static Dictionary<string, string> RagFiles(string llm, string embeddings) =>
        Files(Profile, Overrides(llm, embeddings));

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    /// <summary>A lean-crud project with rag; fails while any of its options is generated by a stand-in.</summary>
    private static GeneratedProject NewProject(string llm, string embeddings, params string[] more)
    {
        var project = new GeneratedProject(Profile, [.. Overrides(llm, embeddings), .. more]);
        var standIns = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs", "adr"))
            .Where(file => File.ReadAllText(file).Contains("This option's pack is not written yet.", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        if (standIns.Count > 0)
        {
            project.Dispose();
        }

        standIns.ShouldBeEmpty();
        return project;
    }
}
