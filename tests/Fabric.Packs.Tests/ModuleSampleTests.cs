using System.Text.Json.Nodes;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// Every module a project adds starts with one sample record (#734, the owner's decision (b)): a <c>Sample</c> with a
/// name, created by <c>POST /&lt;module&gt;/samples</c>, listed a page at a time by <c>GET /&lt;module&gt;/samples</c> and
/// read back by <c>GET /&lt;module&gt;/samples/{id}</c>, built the way the module's style builds anything. It is there to
/// be renamed or deleted, and AGENTS.md says how.
/// </summary>
public sealed class ModuleSampleTests
{
    private const string Module = "src/Modules/Billing/";
    private const string Domain = Module + "Billing.Domain/Samples/";
    private const string Application = Module + "Billing.Application/";
    private const string Infrastructure = Module + "Billing.Infrastructure/";
    private const string Migration = Infrastructure + "Migrations/20261118000000_CreateSamples.cs";
    private const string Snapshot = Infrastructure + "Migrations/BillingModelSnapshot.cs";
    private const string Endpoints = Module + "Billing.Api/Endpoints/SampleEndpoints.cs";

    /// <summary>The use cases, endpoints and tests every style gives a module: only what is behind them differs.</summary>
    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-strict-react")]
    [InlineData("lean-crud")]
    [InlineData("industrial-event-driven-mfe")]
    [InlineData("industrial-event-driven-mfe", "module.cqrs=logical")]
    [InlineData("industrial-event-driven-mfe", "module.event-store=kurrentdb", "module.cqrs=logical")]
    public void Every_module_style_gives_a_new_module_a_sample_its_create_command_its_paged_list_and_its_endpoints(string profile, params string[] overrides)
    {
        var files = WithBilling(profile, overrides);

        files.Keys.ShouldContain(Domain + "Sample.cs");
        files[Application + "Commands/CreateSample/CreateSample.cs"].ShouldContain("public sealed record CreateSample(");
        files[Application + "Commands/CreateSample/CreateSampleHandler.cs"].ShouldContain("ICommandHandler<CreateSample, Result<Guid, ApiError>>");
        files[Application + "Queries/ListSamples/ListSamples.cs"].ShouldContain("public sealed record ListSamples(Page Page, PageSize PageSize) : IQuery<Result<PagedResult<BillingSampleDto>, ApiError>>;");
        files[Application + "Queries/GetSample/GetSampleHandler.cs"].ShouldContain("SampleFailures.NotFound(");
        files[Application + "DTOs/SampleMappings.cs"].ShouldContain("public static BillingSampleDto ToDto(this ");
        files[Endpoints].ShouldContain("endpoints.MapPost(\"/samples\", ");
        files[Endpoints].ShouldContain("endpoints.MapGet(\"/samples\", async (Page? page, PageSize? pageSize, ");
        files[Endpoints].ShouldContain("endpoints.MapGet(\"/samples/{id:guid}\", ");
        files[Endpoints].ShouldContain(".ToCreatedHttpResult(Location)");
        files.Keys.ShouldContain("tests/Modules/Billing.Tests/Samples/CreateSampleTests.cs");
        files.Keys.ShouldContain("tests/Modules/Billing.Tests/Samples/ListSamplesTests.cs");
        files.Keys.ShouldContain("tests/Integration.Tests/BillingSamplesTests.cs");
    }

    [Theory]
    [InlineData("saas-standard-react", "light")]
    [InlineData("saas-strict-react", "strict")]
    public void Under_ddd_a_sample_is_an_aggregate_root_with_a_typed_id_whose_creation_raises_an_event_nothing_reacts_to_yet(string profile, string depth)
    {
        var files = WithBilling(profile);

        files[Domain + "Sample.cs"].ShouldContain("public sealed class Sample : AggregateRoot<SampleId>");
        files[Domain + "Sample.cs"].ShouldNotContain("{ get; set; }");
        files[Domain + "Events/SampleCreated.cs"].ShouldContain("[DeliberatelyUnhandled(");
        files[Application + "Commands/CreateSample/CreateSampleHandler.cs"].ShouldContain("IBillingUnitOfWork");
        files[Application + "ISampleStore.cs"].ShouldContain("public interface ISampleStore");
        files[Infrastructure + "SampleStore.cs"].ShouldContain("public sealed class SampleStore(BillingDbContext context) : ISampleStore");
        if (depth == "strict")
        {
            files[Domain + "SampleId.cs"].ShouldContain("public sealed record SampleId : IValueObject<Guid>");
            files[Domain + "SampleName.cs"].ShouldContain("public sealed record SampleName : IValueObject<string>");
        }
        else
        {
            files[Domain + "SampleId.cs"].ShouldContain("public sealed record SampleId\n");
            files[Domain + "SampleName.cs"].ShouldContain("public sealed record SampleName\n");
        }
    }

    [Fact]
    public void Under_crud_a_sample_is_a_plain_entity_and_the_use_case_checks_its_name()
    {
        var files = WithBilling("lean-crud");

        files[Domain + "Sample.cs"].ShouldContain("public sealed class Sample\n");
        files.Keys.ShouldNotContain(Domain + "SampleId.cs");
        files[Application + "Commands/CreateSample/CreateSampleHandler.cs"].ShouldContain("SampleFailures.InvalidName(");
    }

    /// <summary>Physical CQRS: the query reads a Marten projection of the sample's events, never the aggregate.</summary>
    [Fact]
    public void Under_event_sourced_with_physical_cqrs_the_list_reads_a_projection_of_the_samples_events()
    {
        var files = WithBilling("industrial-event-driven-mfe");

        files[Domain + "Sample.cs"].ShouldContain("public sealed class Sample : EventSourcedAggregate<SampleId>");
        files[Domain + "Sample.cs"].ShouldContain("private void Apply(SampleCreated created)");
        files[Application + "ReadModels/SampleSummary.cs"].ShouldContain("public sealed class SampleSummary");
        files[Infrastructure + "Projections/SampleSummaryProjection.cs"].ShouldContain("SingleStreamProjection<SampleSummary, Guid>");
        files[Application + "Queries/ListSamples/ListSamplesHandler.cs"].ShouldContain("IBillingReadModels");
        files.Keys.ShouldNotContain(Migration);
    }

    /// <summary>
    /// Logical CQRS: as the dashboards module does (#608), the command writes the samples' table right after it stores
    /// the sample's events, on Marten and on KurrentDB, and the queries read that table.
    /// </summary>
    [Theory]
    [InlineData("marten")]
    [InlineData("kurrentdb")]
    public void Under_event_sourced_with_logical_cqrs_the_command_writes_a_table_the_queries_read(string store)
    {
        var files = WithBilling("industrial-event-driven-mfe", $"module.event-store={store}", "module.cqrs=logical");

        var handler = files[Application + "Commands/CreateSample/CreateSampleHandler.cs"];
        handler.IndexOf("samples.SaveAsync(sample, cancellationToken)", StringComparison.Ordinal)
            .ShouldBeLessThan(handler.IndexOf("summaries.RecordAsync(sample, cancellationToken)", StringComparison.Ordinal));
        files[Migration].ShouldContain("name: \"sample_summaries\"");
        files.Keys.ShouldNotContain(Infrastructure + "Projections/SampleSummaryProjection.cs");
    }

    [Theory]
    [InlineData("saas-standard-react", true)]
    [InlineData("lean-crud", false)]
    public void The_migration_creates_the_samples_table_in_the_modules_schema_with_the_tenant_column_only_under_tenancy_column(string profile, bool tenantColumn)
    {
        var files = WithBilling(profile);

        files[Migration].ShouldContain("name: \"samples\",\n            schema: \"billing\"");
        files[Migration].ShouldContain("type: \"character varying(100)\"");
        files[Migration].Contains("TenantId", StringComparison.Ordinal).ShouldBe(tenantColumn);
        files[Snapshot].ShouldContain("partial class BillingModelSnapshot : ModelSnapshot");
    }

    [Fact]
    public void On_sql_server_the_migration_uses_sql_servers_column_types()
    {
        var files = WithBilling("lean-crud", "backend.database=sqlserver");

        files[Migration].ShouldContain("type: \"uniqueidentifier\"");
        files[Migration].ShouldContain("type: \"nvarchar(100)\"");
        files[Snapshot].ShouldContain("SqlServerModelBuilderExtensions");
    }

    [Fact]
    public void With_authorization_creating_a_sample_needs_the_modules_manage_policy_and_listing_one_only_a_signed_in_user()
    {
        var files = WithBilling("saas-standard-react", "backend.authorization=roles");

        files[Endpoints].ShouldContain(".RequireAuthorization(BillingPolicyNames.Manage)");
        files["tests/Integration.Tests/BillingSamplesTests.cs"].ShouldContain("\"visitor\"");
        WithBilling("saas-standard-react")[Endpoints].ShouldNotContain("RequireAuthorization");
    }

    [Fact]
    public void The_dashboards_module_has_its_own_use_cases_and_no_sample()
    {
        var files = Generate("saas-ai-react");

        files.Keys.Where(path => path.StartsWith("src/Modules/Dashboards/", StringComparison.Ordinal) && path.Contains("Sample", StringComparison.Ordinal)).ShouldBeEmpty();
        Paths(files).ShouldNotContain("/dashboards/samples");
    }

    /// <summary>The frontend's copy of the OpenAPI document, and so its generated client, describes every module's sample.</summary>
    [Theory]
    [InlineData("saas-standard-react")]
    [InlineData("saas-standard-vue")]
    [InlineData("lean-crud")]
    [InlineData("industrial-event-driven-mfe")]
    public void A_new_modules_sample_endpoints_and_schemas_go_into_the_frontends_openapi_document(string profile)
    {
        var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, Record(profile, []), "billing", []);

        addition.Errors.ShouldBeEmpty();
        addition.OpenApi.ShouldNotBeNull();
        addition.OpenApi.Paths.Select(path => path.Key).ShouldBe(["/billing/samples", "/billing/samples/{id}"]);
        addition.OpenApi.Schemas.Select(schema => schema.Key).ShouldBe(["CreateBillingSampleRequest", "PagedResultOfBillingSampleDto", "BillingSampleDto"], ignoreOrder: true);
        var samples = addition.OpenApi.Paths[0].Value;
        samples["get"].ShouldNotBeNull();
        samples["post"]!["responses"]!["201"].ShouldNotBeNull();
    }

    [Fact]
    public void Without_an_identity_provider_the_document_still_describes_the_samples()
    {
        var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, Record("lean-crud", ["backend.auth=none"]), "billing", []);

        addition.Errors.ShouldBeEmpty();
        addition.OpenApi.ShouldNotBeNull();
        addition.OpenApi.Paths.Select(path => path.Key).ShouldBe(["/billing/samples", "/billing/samples/{id}"]);
    }

    [Fact]
    public void Agents_md_says_the_sample_is_there_to_be_renamed_or_deleted_and_how()
    {
        var agents = Generate("saas-standard-react")["AGENTS.md"];

        agents.ShouldContain("## Module sample");
        agents.ShouldContain("`Sample`");
        agents.ShouldContain("`20261118000000_CreateSamples`");
    }

    private static Dictionary<string, string> WithBilling(string profile, params string[] overrides)
    {
        var record = Record(profile, overrides);
        var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, record, "billing", []);
        addition.Errors.ShouldBeEmpty();
        var files = Generate(profile, overrides);
        foreach (var file in addition.Created)
        {
            files[file.Path] = file.Content;
        }

        return files;
    }

    private static FabricRecord Record(string profile, string[] overrides) =>
        FabricFile.Read(new StringReader(Generate(profile, overrides)[FabricFile.FileName]), FabricFile.FileName);

    private static Dictionary<string, string> Generate(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }

    private static List<string> Paths(Dictionary<string, string> files) =>
        [.. JsonNode.Parse(files["web/app/openapi.json"])!["paths"]!.AsObject().Select(path => path.Key)];
}
