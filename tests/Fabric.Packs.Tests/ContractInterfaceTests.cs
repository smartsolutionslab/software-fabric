using System.Net;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>module.communication: [integration-events, contract-interface]</c>: a module answers other modules synchronously,
/// in-process, through <c>I&lt;Module&gt;Api</c> in <c>src/Contracts/&lt;Module&gt;/</c>. Its Api project implements it
/// by dispatching its own commands and queries; each module registers its implementation by convention. Modules still
/// never reference each other's projects, so two modules may call each other.
/// </summary>
[Collection(DockerPorts.Name)]
public sealed class ContractInterfaceTests
{
    private const string Profile = "lean-crud";

    private const string ContractInterface = "module.communication=integration-events,contract-interface";

    private const string DatabasePassword = "fabric-tests";

    private const string ArchitectureTests = "tests/Architecture.Tests/Architecture.Tests.csproj";

    private const string BillingApiContract = """
        namespace Shop.Contracts.Billing;

        /// <summary>What other modules may ask the billing module.</summary>
        public interface IBillingApi
        {
            Task<Result<int, ApiError>> CountInvoicesAsync(string customer, CancellationToken cancellationToken);
        }
        """;

    private const string OrderingApiContract = """
        namespace Shop.Contracts.Ordering;

        /// <summary>What other modules may ask the ordering module.</summary>
        public interface IOrderingApi
        {
            Task<Result<int, ApiError>> CountOrdersAsync(string customer, CancellationToken cancellationToken);
        }
        """;

    /// <summary>Billing counts two invoices per order, and asks ordering for the orders.</summary>
    private const string CountInvoices = """
        using Shop.BuildingBlocks.Messaging;
        using Shop.Contracts.Ordering;

        namespace Shop.Modules.Billing.Application.Queries.CountInvoices;

        public sealed record CountInvoices(string Customer) : IQuery<Result<int, ApiError>>;

        public sealed class CountInvoicesHandler(IOrderingApi ordering) : IQueryHandler<CountInvoices, Result<int, ApiError>>
        {
            public async Task<Result<int, ApiError>> HandleAsync(CountInvoices query, CancellationToken cancellationToken)
            {
                Ensure.That(query).IsNotNull();

                var orders = await ordering.CountOrdersAsync(query.Customer, cancellationToken);
                return orders.Match<Result<int, ApiError>>(count => Success(count * 2), error => Failure(error));
            }
        }
        """;

    private const string BillingApi = """
        using Shop.BuildingBlocks.Messaging;
        using Shop.Contracts.Billing;
        using Shop.Modules.Billing.Application.Queries.CountInvoices;

        namespace Shop.Modules.Billing.Api;

        public sealed class BillingApi(IDispatcher dispatcher) : IBillingApi
        {
            public Task<Result<int, ApiError>> CountInvoicesAsync(string customer, CancellationToken cancellationToken) =>
                dispatcher.QueryAsync(new CountInvoices(customer), cancellationToken);
        }
        """;

    /// <summary>Ordering counts one order per letter of the customer's name.</summary>
    private const string CountOrders = """
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Ordering.Application.Queries.CountOrders;

        public sealed record CountOrders(string Customer) : IQuery<Result<int, ApiError>>;

        public sealed class CountOrdersHandler : IQueryHandler<CountOrders, Result<int, ApiError>>
        {
            public Task<Result<int, ApiError>> HandleAsync(CountOrders query, CancellationToken cancellationToken)
            {
                Ensure.That(query).IsNotNull();

                return Task.FromResult<Result<int, ApiError>>(Success(query.Customer.Length));
            }
        }
        """;

    /// <summary>Ordering's own endpoint asks billing for the customer's invoices.</summary>
    private const string CountCustomerInvoices = """
        using Shop.BuildingBlocks.Messaging;
        using Shop.Contracts.Billing;

        namespace Shop.Modules.Ordering.Application.Queries.CountCustomerInvoices;

        public sealed record CountCustomerInvoices(string Customer) : IQuery<Result<int, ApiError>>;

        public sealed class CountCustomerInvoicesHandler(IBillingApi billing) : IQueryHandler<CountCustomerInvoices, Result<int, ApiError>>
        {
            public Task<Result<int, ApiError>> HandleAsync(CountCustomerInvoices query, CancellationToken cancellationToken)
            {
                Ensure.That(query).IsNotNull();

                return billing.CountInvoicesAsync(query.Customer, cancellationToken);
            }
        }
        """;

    private const string OrderingApi = """
        using Shop.BuildingBlocks.Messaging;
        using Shop.Contracts.Ordering;
        using Shop.Modules.Ordering.Application.Queries.CountOrders;

        namespace Shop.Modules.Ordering.Api;

        public sealed class OrderingApi(IDispatcher dispatcher) : IOrderingApi
        {
            public Task<Result<int, ApiError>> CountOrdersAsync(string customer, CancellationToken cancellationToken) =>
                dispatcher.QueryAsync(new CountOrders(customer), cancellationToken);
        }
        """;

    private const string CustomerInvoices = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.BuildingBlocks.Messaging;
        using Shop.Modules.Ordering.Application.Queries.CountCustomerInvoices;

        namespace Shop.Modules.Ordering.Api.Endpoints;

        public sealed class CustomerInvoices : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/customers/{customer}/invoices", async (string customer, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                    (await dispatcher.QueryAsync(new CountCustomerInvoices(customer), cancellationToken)).ToHttpResult()).AllowAnonymous();
        }
        """;

    private static Dictionary<string, string> Dependencies(Container postgres) => new()
    {
        ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
        ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
    };

    [Fact]
    public void A_project_with_module_apis_generates_from_the_real_packs_alone()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, [ContractInterface]);

        generated.Errors.ShouldBeEmpty();
        generated.Files.Where(file => file.Path.StartsWith("docs/adr/", StringComparison.Ordinal))
            .Select(file => file.Content)
            .ShouldAllBe(content => !content.Contains("This option's pack is not written yet.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_new_module_gets_its_api_in_contracts_and_the_implementation_in_its_api_project_and_no_line_elsewhere()
    {
        var created = AddModule(ContractInterface);

        created.Keys.ShouldContain("src/Contracts/Billing/IBillingApi.cs");
        created["src/Contracts/Billing/IBillingApi.cs"].ShouldContain("namespace Shop.Contracts.Billing;");
        created["src/Contracts/Billing/IBillingApi.cs"].ShouldContain("public interface IBillingApi;");
        created.Keys.ShouldContain("src/Modules/Billing/Billing.Api/BillingApi.cs");
        created["src/Modules/Billing/Billing.Api/BillingApi.cs"].ShouldContain("public sealed class BillingApi : IBillingApi;");
        created.Where(file => file.Value.Contains("BillingApi", StringComparison.Ordinal)).Select(file => file.Key).Order(StringComparer.Ordinal)
            .ShouldBe(["src/Contracts/Billing/IBillingApi.cs", "src/Modules/Billing/Billing.Api/BillingApi.cs"]);
    }

    [Fact]
    public void A_module_without_contract_interface_gets_no_api()
    {
        var created = AddModule();

        created.Keys.ShouldNotContain("src/Contracts/Billing/IBillingApi.cs");
        created.Keys.ShouldNotContain("src/Modules/Billing/Billing.Api/BillingApi.cs");
    }

    [Fact]
    public void Every_module_registers_its_api_by_convention_and_its_test_stands_in_for_the_others()
    {
        var files = Files(Profile, ContractInterface);

        files["src/BuildingBlocks/Modules/ModuleInfrastructure.cs"].ShouldContain("services.AddModuleApis<TModule>();");
        files.Keys.ShouldContain("src/BuildingBlocks/Modules/ModuleApis.cs");
        files.Keys.ShouldContain("tests/Architecture.Tests/Rules/ModuleApisBelongToTheirModule.cs");
    }

    [Fact]
    public void No_profile_has_module_apis()
    {
        foreach (var profile in Repository.Catalog.Profiles.Keys)
        {
            var files = Files(profile);
            files.Keys.ShouldNotContain("src/BuildingBlocks/Modules/ModuleApis.cs", customMessage: profile);
            files["src/BuildingBlocks/Modules/ModuleInfrastructure.cs"].ShouldNotContain("AddModuleApis", customMessage: profile);
        }
    }

    [Fact]
    public async Task Two_modules_call_each_other_through_their_contracts_in_the_running_host_and_the_rules_still_hold()
    {
        using var project = new GeneratedProject(Profile, ContractInterface);
        project.AddModule("billing");
        project.AddModule("ordering");
        project.WriteTypes("src/Contracts/Billing", BillingApiContract);
        project.WriteTypes("src/Contracts/Ordering", OrderingApiContract);
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/CountInvoices", CountInvoices);
        project.WriteTypes("src/Modules/Billing/Billing.Api", BillingApi);
        project.WriteTypes("src/Modules/Ordering/Ordering.Application/Queries/CountOrders", CountOrders);
        project.WriteTypes("src/Modules/Ordering/Ordering.Application/Queries/CountCustomerInvoices", CountCustomerInvoices);
        project.WriteTypes("src/Modules/Ordering/Ordering.Api", OrderingApi);
        project.WriteTypes("src/Modules/Ordering/Ordering.Api/Endpoints", CustomerInvoices);
        project.Build();

        var (architectureExitCode, architectureOutput) = project.Test(ArchitectureTests);
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");

        // Each module's test starts that module alone: the API it calls is another module's, so the test stands in for it.
        foreach (var module in new[] { "Billing", "Ordering" })
        {
            var (moduleExitCode, moduleOutput) = project.Test($"tests/Modules/{module}.Tests/{module}.Tests.csproj");
            moduleExitCode.ShouldBe(0, moduleOutput);
            moduleOutput.ShouldContain("failed: 0");
        }

        using var postgres = Container.Postgres(DatabasePassword);
        RunMigrationRunner(project, postgres);
        using var host = project.StartHost(Dependencies(postgres));

        // Ordering asks billing, which asks ordering back: four orders for "acme", two invoices each.
        var response = await host.GetAsync("/ordering/customers/acme/invoices");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("8");
    }

    [Fact]
    public void A_module_implementing_another_modules_api_fails_the_architecture_tests_naming_both()
    {
        using var project = new GeneratedProject(Profile, ContractInterface);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Modules/Shipping/Shipping.Api/BillingStandIn.cs", """
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Api;

            public sealed class BillingStandIn : IBillingApi;
            """);
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Shipping.Api.BillingStandIn implements Shop.Contracts.Billing.IBillingApi, the Billing module's API; only Shop.Modules.Billing.Api may implement it.");
        output.ShouldContain("Shop.Contracts.Billing.IBillingApi has 2 implementations (Shop.Modules.Billing.Api.BillingApi, Shop.Modules.Shipping.Api.BillingStandIn); it needs exactly one.");
    }

    [Fact]
    public void An_implementation_in_contracts_fails_the_architecture_tests_as_one_outside_the_module()
    {
        using var project = new GeneratedProject(Profile, ContractInterface);
        project.AddModule("billing");
        project.Write("src/Contracts/Billing/BillingStandIn.cs", """
            namespace Shop.Contracts.Billing;

            public sealed class BillingStandIn : IBillingApi;
            """);
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Contracts.Billing.BillingStandIn implements Shop.Contracts.Billing.IBillingApi, the Billing module's API; only Shop.Modules.Billing.Api may implement it.");
        output.ShouldContain("Shop.Contracts.Billing.IBillingApi has 2 implementations (Shop.Contracts.Billing.BillingStandIn, Shop.Modules.Billing.Api.BillingApi); it needs exactly one.");
    }

    /// <summary>What Aspire does before it starts the host: every module's migrations, then the runner's further steps.</summary>
    private static void RunMigrationRunner(GeneratedProject project, Container postgres)
    {
        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string> { ["ConnectionStrings__database"] = Dependencies(postgres)["ConnectionStrings__database"] },
            $"src/MigrationRunner/bin/Release/{GeneratedProject.TargetFramework}/MigrationRunner.dll");
        exitCode.ShouldBe(0, output);
    }

    /// <summary>The files <c>fabric add-module billing</c> creates in a lean-crud project with the overrides.</summary>
    private static Dictionary<string, string> AddModule(params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, overrides);
        generated.Errors.ShouldBeEmpty();
        var fabricYaml = generated.Files.Single(file => file.Path == FabricFile.FileName).Content;
        var record = FabricFile.Read(new StringReader(fabricYaml), FabricFile.FileName);

        var addition = Generator.AddModule(Repository.Catalog, Repository.Packs.Packs, record, "billing", []);

        addition.Errors.ShouldBeEmpty();
        return addition.Created.ToDictionary(file => file.Path, file => file.Content);
    }

    private static Dictionary<string, string> Files(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return generated.Files.ToDictionary(file => file.Path, file => file.Content);
    }
}
