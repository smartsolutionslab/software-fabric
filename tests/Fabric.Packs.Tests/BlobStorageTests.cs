using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>backend.blob-storage</c> (ADR-0020): with <c>azure-blob</c> the AppHost runs Azurite with a data volume and hands
/// the host (or each service, its own container) the connection <c>blobs</c>; a module keeps its files through
/// <c>ModuleBlobStore&lt;TModule&gt;</c> in <c>src/BuildingBlocks/Blobs</c>, the only code that names the Azure Storage
/// SDK (O4), in a container of its own, create-only (O5), with stream operations alone (O1, O2). Under a tenant column
/// or a schema per tenant the store prefixes every name with the tenant it reads from <c>ITenantContext</c> (B2, O3), and
/// the generated integration test reads another tenant's blob by its full name and is refused. <c>none</c> keeps the SDK
/// out of every module. The dimension may be changed after generation (X1).
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class BlobStorageTests
{
    private const string Profile = "lean-crud";
    private const string AzureBlob = "backend.blob-storage=azure-blob";
    private const string Store = "src/BuildingBlocks/Blobs";
    private const string IntegrationTests = "tests/Integration.Tests/BlobStoreTests.cs";
    private const string Azurite = "mcr.microsoft.com/azure-storage/azurite:3.35.0";

    private static readonly string[] Services = ["backend.topology=services", "backend.tenancy=none", AzureBlob];

    /// <summary>The notes module keeps the billing module's files: in a field, through a generic helper of its own.</summary>
    private const string NotesReadingBillingsFiles = """
        using Shop.BuildingBlocks.Blobs;
        using Shop.Modules.Billing.Infrastructure;

        namespace Shop.Modules.Notes.Infrastructure;

        public sealed class BillingFiles(ModuleBlobStore<InvoiceFiles> billing)
        {
            public ModuleBlobStore<InvoiceFiles> Store => billing;
        }

        public sealed class AnyModuleFiles<TModule>(ModuleBlobStore<TModule> store)
            where TModule : class
        {
            public ModuleBlobStore<TModule> Store => store;
        }
        """;

    /// <summary>The billing module keeping its own files, which the gates let through.</summary>
    private const string BillingKeepingItsOwnFiles = """
        using Shop.BuildingBlocks.Blobs;

        namespace Shop.Modules.Billing.Infrastructure;

        public sealed class InvoiceFiles(ModuleBlobStore<InvoiceFiles> files)
        {
            public Task UploadAsync(Guid invoice, Stream content, CancellationToken cancellationToken) =>
                files.UploadAsync(BlobName.From($"invoices/{invoice}"), content, cancellationToken);
        }
        """;

    /// <summary>The notes module building a client of its own from the SDK, which bypasses the store.</summary>
    private const string NotesNamingTheSdk = """
        using Azure.Storage.Blobs;

        namespace Shop.Modules.Notes.Infrastructure;

        public sealed class RawFiles
        {
            public BlobContainerClient Container { get; } = new BlobServiceClient("UseDevelopmentStorage=true").GetBlobContainerClient("billing");
        }
        """;

    private const string NotesReferencingBilling = """
          <ItemGroup>
            <ProjectReference Include="../../Billing/Billing.Infrastructure/Billing.Infrastructure.csproj" />
          </ItemGroup>

        </Project>
        """;

    private const string NotesReferencingTheSdk = """
          <ItemGroup>
            <PackageReference Include="Azure.Storage.Blobs" />
          </ItemGroup>

        </Project>
        """;

    public static TheoryData<string[]> Choices { get; } = new()
    {
        new[] { AzureBlob },
        new[] { AzureBlob, "backend.tenancy=column" },
        new[] { AzureBlob, "backend.tenancy=schema-per-tenant", "backend.bus.transport=rabbitmq" },
        new[] { AzureBlob, "backend.tenancy=column", "backend.test.integration=testcontainers" },
    };

    public static TheoryData<string> Tenancies { get; } = ["column", "schema-per-tenant"];

    [Fact]
    public void The_catalog_offers_none_and_azure_blob_for_the_project_defaults_to_none_and_lets_it_change_after_generation()
    {
        var dimension = Repository.Catalog.Dimensions["backend.blob-storage"];

        dimension.Scope.ToString().ShouldBe("Project");
        dimension.Options.Keys.ShouldBe(["none", "azure-blob"], ignoreOrder: true);
        dimension.Changeable.ShouldBeTrue("ADR-0020 X1: a pilot moves to the pack with fabric change.");
        Repository.Packs.Packs.Keys.ShouldContain("backend.blob-storage.none");
        Repository.Packs.Packs.Keys.ShouldContain("backend.blob-storage.azure-blob");
    }

    [Theory]
    [MemberData(nameof(Choices))]
    public void A_project_with_azure_blob_generates_from_the_real_packs_alone(string[] overrides)
    {
        using var standIns = new StandIns();

        standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile, overrides);

        standIns.StoodIn.ShouldNotContain(option => option.StartsWith("backend.blob-storage.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_apphost_runs_azurite_with_a_data_volume_and_hands_the_host_the_blobs_connection()
    {
        var files = Files(Profile, AzureBlob);

        var appHost = files["src/AppHost/AppHost.cs"];
        appHost.ShouldContain(
            "var blobs = builder.AddAzureStorage(\"storage\")\n    .RunAsEmulator(azurite => azurite.WithDataVolume())\n    .AddBlobs(\"blobs\");",
            Case.Sensitive,
            appHost);
        appHost.ShouldContain("    .WithReference(blobs)\n    .WaitFor(blobs)", Case.Sensitive, appHost);
        files["src/AppHost/AppHost.csproj"].ShouldContain("<PackageReference Include=\"Aspire.Hosting.Azure.Storage\" />");
        files["Directory.Packages.props"].ShouldContain("<PackageVersion Include=\"Aspire.Hosting.Azure.Storage\" Version=\"13.6.5\" />");
    }

    [Fact]
    public void With_services_each_service_reads_its_own_container_as_blobs_and_the_account_has_no_shared_connection()
    {
        var files = Files("saas-standard-react", Services);

        var appHost = files["src/AppHost/AppHost.cs"];
        appHost.ShouldContain("var storage = builder.AddAzureStorage(\"storage\")\n    .RunAsEmulator(azurite => azurite.WithDataVolume());", Case.Sensitive, appHost);
        appHost.ShouldContain("    var blobs = storage.AddBlobContainer($\"{module}-blobs\", module);", Case.Sensitive, appHost);
        appHost.ShouldContain("        .WithReference(blobs, \"blobs\")", Case.Sensitive, appHost);
        appHost.ShouldNotContain(".AddBlobs(\"blobs\")");
        files[$"{Store}/BlobContainers.cs"].ShouldContain("ContainerName");
        files.Keys.ShouldContain("tests/Blobs.Tests/AppHostBlobContainersTests.cs");
    }

    [Fact]
    public void Only_the_building_block_names_the_azure_storage_sdk_and_its_architecture_rules_say_so()
    {
        var files = Files(Profile, AzureBlob);

        files["Directory.Packages.props"].ShouldContain("<PackageVersion Include=\"Azure.Storage.Blobs\" Version=\"12.30.1\" />");
        files["Directory.Packages.props"].ShouldContain("<PackageVersion Include=\"Azure.Identity\" Version=\"1.21.0\" />");
        files["src/BuildingBlocks/BuildingBlocks.csproj"].ShouldContain("<PackageReference Include=\"Azure.Storage.Blobs\" />");
        files.Where(file => file.Key.StartsWith("src/", StringComparison.Ordinal) && file.Key.EndsWith(".cs", StringComparison.Ordinal) && file.Value.Contains("using Azure", StringComparison.Ordinal))
            .Select(file => file.Key)
            .ShouldAllBe(path => path.StartsWith(Store + "/", StringComparison.Ordinal));
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/BlobStorageSdkStaysInBuildingBlocks.cs");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/ModulesUseOnlyTheirOwnContainer.cs");
        files["src/Host/Program.cs"].ShouldContain("builder.Services.AddModuleBlobStores(builder.Configuration);");
    }

    [Fact]
    public void The_store_streams_create_only_under_a_guarded_name_and_has_no_signed_urls_no_copy_and_no_container_parameter()
    {
        var files = Files(Profile, AzureBlob);

        var store = files[$"{Store}/ModuleBlobStore.cs"];
        store.ShouldContain("public sealed class ModuleBlobStore<TModule>");
        store.ShouldContain("public async Task UploadAsync(BlobName name, Stream content, CancellationToken cancellationToken)");
        store.ShouldContain("public async Task<Option<Stream>> OpenReadAsync(BlobName name, CancellationToken cancellationToken)");
        store.ShouldNotContain("overwrite: true");
        store.ShouldNotContain("Sas", Case.Sensitive, "O1: no signed URLs until a pilot has signed one in Azure.");
        store.ShouldNotContain("Copy", Case.Sensitive, "O2: no copy between containers.");
        store.ShouldNotContain("string container", Case.Sensitive, "no operation takes a container.");
        files[$"{Store}/BlobName.cs"].ShouldContain("public const int MaxLength = 1024;");
        files.Keys.ShouldContain("tests/Blobs.Tests/BlobNameTests.cs");
        files[IntegrationTests].ShouldContain("A_second_write_to_a_name_is_refused_and_the_name_keeps_its_first_bytes");
    }

    [Fact]
    public void With_nullable_absence_a_missing_blob_is_null()
    {
        var store = Files(Profile, AzureBlob, "backend.absence=nullable")[$"{Store}/ModuleBlobStore.cs"];

        store.ShouldContain("public async Task<Stream?> OpenReadAsync(BlobName name, CancellationToken cancellationToken)");
        store.ShouldNotContain("Option<");
    }

    [Theory]
    [MemberData(nameof(Tenancies))]
    public void Under_tenancy_the_store_prefixes_every_name_with_the_tenant_it_reads_from_the_tenant_context(string tenancy)
    {
        string[] overrides = tenancy == "schema-per-tenant"
            ? [AzureBlob, "backend.tenancy=schema-per-tenant", "backend.bus.transport=rabbitmq"]
            : [AzureBlob, "backend.tenancy=column"];
        var files = Files(Profile, overrides);

        var store = files[$"{Store}/ModuleBlobStore.cs"];
        store.ShouldNotContain("#error");
        store.ShouldContain("ITenantContext tenants");
        store.ShouldContain("tenants.TenantId");
        store.ShouldNotContain("string tenant", Case.Sensitive, "a caller never passes the tenant.");
        files[$"{Store}/Wiring/BlobStorageRegistration.cs"].ShouldContain(".AddScoped(typeof(ModuleBlobStore<>))");
        var tests = files[IntegrationTests];
        tests.ShouldContain("A_tenant_cannot_read_another_tenants_blob_by_its_full_name");
        tests.ShouldContain("Without_a_tenant_the_store_opens_nothing_and_writes_nothing");
        files["AGENTS.md"].ShouldContain("TenantScope.Begin");
    }

    [Fact]
    public void Without_tenancy_names_are_kept_as_given()
    {
        var files = Files(Profile, AzureBlob);

        files[$"{Store}/ModuleBlobStore.cs"].ShouldNotContain("tenant", Case.Insensitive);
        files[$"{Store}/Wiring/BlobStorageRegistration.cs"].ShouldContain(".AddSingleton(typeof(ModuleBlobStore<>))");
        files[IntegrationTests].ShouldNotContain("tenant", Case.Insensitive);
    }

    [Fact]
    public void With_a_database_per_tenant_the_store_fails_the_build_instead_of_sharing_files_between_tenants()
    {
        using var standIns = new StandIns();
        string[] overrides = [AzureBlob, "backend.tenancy=database-per-tenant"];
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile, overrides);

        var generated = Generator.New(Repository.Catalog, packs, "shop", Profile, overrides);

        generated.Errors.ShouldBeEmpty();
        generated.Files.Single(file => file.Path == $"{Store}/ModuleBlobStore.cs").Content.ShouldStartWith("#error ");
    }

    [Fact]
    public void With_testcontainers_the_application_fixture_starts_azurite_and_gives_the_host_its_blobs()
    {
        var fixture = Files(Profile, AzureBlob, "backend.tenancy=column", "backend.test.integration=testcontainers")["tests/Integration.Tests/ContainerFixture.cs"];

        fixture.ShouldContain(Azurite);
        fixture.ShouldContain("Resources.Add(\"storage\", \"blob\",");
        fixture.ShouldContain("settings[\"ConnectionStrings:blobs\"] =");
    }

    [Fact]
    public void The_agents_are_told_to_take_the_modules_store_and_to_name_files_themselves()
    {
        var files = Files(Profile, AzureBlob);

        var agents = files["AGENTS.md"];
        agents.ShouldContain("## Blob storage: Azure Blob Storage");
        agents.ShouldContain("never a `BlobServiceClient`");
        agents.ShouldContain("Guid v7");
        agents.ShouldContain("never from user input");
        agents.ShouldContain("size and content type");
        agents.ShouldContain("src/Contracts");
        files.Keys.ShouldContain("docs/adr/fabric/backend-blob-storage-azure-blob.md");
        files["Solution.slnx"].ShouldContain("<Project Path=\"tests/Blobs.Tests/Blobs.Tests.csproj\" />");
    }

    [Fact]
    public void With_none_no_module_may_reference_the_azure_storage_sdk()
    {
        var files = Files(Profile);

        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/NoBlobStorage.cs");
        files["AGENTS.md"].ShouldContain("## Blob storage: none");
        files.Keys.ShouldContain("docs/adr/fabric/backend-blob-storage-none.md");
        files.Keys.ShouldNotContain(path => path.StartsWith(Store, StringComparison.Ordinal));
        files.Values.ShouldAllBe(content => !content.Contains("AddAzureStorage", StringComparison.Ordinal));
    }

    [Fact]
    public void A_module_keeping_its_own_files_passes_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, AzureBlob);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Infrastructure", BillingKeepingItsOwnFiles);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_module_keeping_another_modules_files_or_naming_the_sdk_fails_the_architecture_tests_naming_both()
    {
        using var project = new GeneratedProject(Profile, AzureBlob);
        project.AddModule("billing");
        project.AddModule("notes");
        project.WriteTypes("src/Modules/Billing/Billing.Infrastructure", BillingKeepingItsOwnFiles);
        var notes = Path.Combine(project.ProjectDirectory, "src/Modules/Notes/Notes.Infrastructure/Notes.Infrastructure.csproj");
        File.WriteAllText(notes, File.ReadAllText(notes).Replace("</Project>", NotesReferencingBilling, StringComparison.Ordinal));
        File.WriteAllText(notes, File.ReadAllText(notes).Replace("</Project>", NotesReferencingTheSdk, StringComparison.Ordinal));
        project.WriteTypes("src/Modules/Notes/Notes.Infrastructure", NotesReadingBillingsFiles);
        project.WriteTypes("src/Modules/Notes/Notes.Infrastructure", NotesNamingTheSdk);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Notes.Infrastructure.BillingFiles (module Notes) uses ModuleBlobStore<InvoiceFiles>, module Billing's container.");
        output.ShouldContain("Shop.Modules.Notes.Infrastructure.AnyModuleFiles`1 (module Notes) uses ModuleBlobStore<TModule> over a generic parameter");
        output.ShouldContain("Shop.Modules.Notes.Infrastructure references Azure.Storage.Blobs");
    }

    [Fact]
    public void The_generated_blob_unit_and_apphost_model_tests_pass_without_docker()
    {
        using var project = new GeneratedProject(Profile, AzureBlob);
        project.Build();

        var (exitCode, output) = project.Test("tests/Blobs.Tests/Blobs.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void The_generated_services_apphost_gives_every_service_its_own_container_and_no_other()
    {
        using var project = new GeneratedProject("saas-standard-react", Services);
        project.AddModule("billing");
        project.AddModule("order-handling");
        project.Build();

        var (exitCode, output) = project.Test("tests/Blobs.Tests/Blobs.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("Every_service_reads_its_own_container_as_blobs_and_no_other");
    }

    [Theory]
    [MemberData(nameof(Choices))]
    public void The_generated_blob_store_tests_pass_against_the_applications_azurite(string[] overrides)
    {
        using var project = new GeneratedProject(Profile, overrides);
        project.Build();

        var (exitCode, output) = BlobStoreTests(project);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    /// <summary>
    /// The B2 gate itself: a store that forgets the tenant prefix, as fabric-pilot's hand-written one does, fails the
    /// generated integration test that reads another tenant's blob by its full name.
    /// </summary>
    [Fact]
    public void A_store_that_drops_the_tenant_prefix_fails_the_generated_integration_test()
    {
        using var project = new GeneratedProject(Profile, AzureBlob, "backend.tenancy=column");
        var store = Path.Combine(project.ProjectDirectory, Store, "ModuleBlobStore.cs");
        var source = File.ReadAllText(store);
        source.ShouldContain("BlobName.From($\"{tenant}/{name}\")");
        File.WriteAllText(store, source.Replace("BlobName.From($\"{tenant}/{name}\")", "name", StringComparison.Ordinal));
        project.Build();

        var (exitCode, output) = BlobStoreTests(project);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("A_tenant_cannot_read_another_tenants_blob_by_its_full_name");
    }

    private static (int ExitCode, string Output) BlobStoreTests(GeneratedProject project) =>
        project.DotNetCommand(
            new Dictionary<string, string>(),
            "test", "--project", "tests/Integration.Tests/Integration.Tests.csproj", "-c", "Release", "--no-build", "--no-ansi",
            "--", "--filter-class", "Shop.Integration.Tests.BlobStoreTests");

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
    }
}
