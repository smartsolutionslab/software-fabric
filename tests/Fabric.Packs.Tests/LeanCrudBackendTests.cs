using System.Globalization;
using System.Net;

namespace Fabric.Packs.Tests;

/// <summary>
/// lean-crud's backend from the real packs: no broker, no tenancy, CRUD modules without CQRS. Its frontend packs are
/// M2-02b; until then the project uses saas-standard-react's frontend so it generates and builds.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class LeanCrudBackendTests
{
    private const string Profile = "lean-crud";
    private const string DatabasePassword = "fabric-tests";

    private static readonly string[] Frontend = ["frontend.state=redux-toolkit", "frontend.api-client=rtk-query-codegen"];

    private static readonly string[] BackendOptions = ["backend.bus.transport.none", "backend.tenancy.none", "module.style.crud", "module.cqrs.none"];

    private static Dictionary<string, string> Dependencies(Container postgres) => new()
    {
        ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
        ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
    };

    [Fact]
    public void Every_backend_option_of_lean_crud_has_a_pack()
    {
        foreach (var option in BackendOptions)
        {
            Repository.Packs.Packs.ShouldContainKey(option);
        }
    }

    [Fact]
    public void A_lean_crud_project_builds_and_a_new_modules_tests_and_the_architecture_rules_pass()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Build();

        var standIns = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "docs", "adr"))
            .Where(file => File.ReadAllText(file).Contains("This option's pack is not written yet.", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();
        standIns.ShouldBeEmpty();

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public async Task An_integration_event_crosses_two_modules_in_process_through_the_outbox_without_a_broker()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IIntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/PayInvoice.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Contracts.Billing;
            using Shop.Modules.Billing.Application;

            namespace Shop.Modules.Billing.Api;

            public sealed class PayInvoice : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapPost("/invoices/{id}/pay", async (Guid id, IBillingUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
                    {
                        await unitOfWork.PublishAsync(new InvoicePaidV1(id));
                        await unitOfWork.CommitAsync(cancellationToken);
                        return Results.Accepted();
                    }).AllowAnonymous();
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", """
            using System.Collections.Concurrent;
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer
            {
                private readonly ConcurrentBag<Guid> received = Received;

                public static ConcurrentBag<Guid> Received { get; } = [];

                public Task Handle(InvoicePaidV1 message, CancellationToken cancellationToken)
                {
                    received.Add(message.InvoiceId);
                    return Task.CompletedTask;
                }
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Api/Received.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Modules.Shipping.Application;

            namespace Shop.Modules.Shipping.Api;

            public sealed class Received : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints) =>
                    endpoints.MapGet("/received", () => InvoicePaidConsumer.Received.Count).AllowAnonymous();
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var host = project.StartHost(Dependencies(postgres));

        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var received = "0";
        for (var attempt = 0; attempt < 60 && received == "0"; attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            received = await (await host.GetAsync("/shipping/received")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        received.ShouldBe("1");
        postgres.Psql(DatabasePassword, "select count(*) from billing.outbox_messages where delivered_at is not null").ShouldBe("1");
    }

    [Fact]
    public async Task A_failing_consumer_is_not_retried_before_its_backoff_has_passed_while_other_events_flow()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.AddModule("shipping");
        project.Write("src/Contracts/Billing/InvoicePaidV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoicePaidV1(Guid InvoiceId) : IIntegrationEvent;
            """);
        project.Write("src/Contracts/Billing/InvoiceVoidedV1.cs", """
            namespace Shop.Contracts.Billing;

            public sealed record InvoiceVoidedV1(Guid InvoiceId) : IIntegrationEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Api/Publish.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;
            using Shop.BuildingBlocks.Api;
            using Shop.Contracts.Billing;
            using Shop.Modules.Billing.Application;

            namespace Shop.Modules.Billing.Api;

            public sealed class Publish : IEndpoint
            {
                public void Map(IEndpointRouteBuilder endpoints)
                {
                    endpoints.MapPost("/invoices/{id}/pay", async (Guid id, IBillingUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
                    {
                        await unitOfWork.PublishAsync(new InvoicePaidV1(id));
                        await unitOfWork.CommitAsync(cancellationToken);
                        return Results.Accepted();
                    }).AllowAnonymous();
                    endpoints.MapPost("/invoices/{id}/void", async (Guid id, IBillingUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
                    {
                        await unitOfWork.PublishAsync(new InvoiceVoidedV1(id));
                        await unitOfWork.CommitAsync(cancellationToken);
                        return Results.Accepted();
                    }).AllowAnonymous();
                }
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoicePaidConsumer.cs", """
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoicePaidConsumer
            {
                public static void Handle(InvoicePaidV1 message) => _ = message;
            }
            """);
        project.Write("src/Modules/Shipping/Shipping.Application/InvoiceVoidedConsumer.cs", """
            using Shop.Contracts.Billing;

            namespace Shop.Modules.Shipping.Application;

            public sealed class InvoiceVoidedConsumer
            {
                public static void Handle(InvoiceVoidedV1 message) =>
                    throw new InvalidOperationException($"The carrier is down; {message.InvoiceId} cannot be cancelled yet.");
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var host = project.StartHost(Dependencies(postgres));

        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/void")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Keep the dispatcher busy: each delivered event sends it straight round its loop again.
        for (var paid = 0; paid < 40; paid++)
        {
            (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        const string Voided = "from billing.outbox_messages where type like 'Shop.Contracts.Billing.InvoiceVoidedV1,%'";
        int.Parse(postgres.Psql(DatabasePassword, $"select attempts {Voided}"), CultureInfo.InvariantCulture).ShouldBeInRange(1, 3);
        postgres.Psql(DatabasePassword, $"select delivered_at is null and next_attempt_at > now() {Voided}").ShouldBe("t");
    }

    [Fact]
    public void A_consumer_that_handles_something_other_than_an_integration_event_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("shipping");
        project.Write("src/Modules/Shipping/Shipping.Application/TextConsumer.cs", """
            namespace Shop.Modules.Shipping.Application;

            public sealed class TextConsumer
            {
                public static void Handle(string text) => _ = text;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Shipping.Application.TextConsumer.Handle does not handle an integration event.");
    }

    [Fact]
    public void An_entity_that_knows_ef_core_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Billing.Domain.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
              </ItemGroup>
            </Project>
            """);
        project.Write("src/Modules/Billing/Billing.Domain/Invoice.cs", """
            using Microsoft.EntityFrameworkCore;

            namespace Shop.Modules.Billing.Domain;

            [Index(nameof(Number))]
            public sealed class Invoice
            {
                public Guid Id { get; set; }

                public string Number { get; set; } = string.Empty;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Domain references Microsoft.EntityFrameworkCore.Abstractions; map entities in Infrastructure.");
    }

    [Fact]
    public void A_tenant_id_in_a_project_without_tenancy_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoice.cs", """
            namespace Shop.Modules.Billing.Domain;

            public sealed class Invoice
            {
                public Guid Id { get; set; }

                public string TenantId { get; set; } = string.Empty;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoice has a TenantId, but this project has no tenancy.");
    }

    [Fact]
    public void A_read_model_in_a_module_without_cqrs_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Application/InvoiceReadModel.cs", """
            namespace Shop.Modules.Billing.Application;

            public sealed record InvoiceReadModel(Guid Id, string Number);
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Application.InvoiceReadModel is a separate read model; without CQRS a module reads the entities it writes.");
    }

    [Fact]
    public void The_generated_integration_tests_pass_against_the_running_aspire_stack()
    {
        using var project = new GeneratedProject(Profile, Frontend);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }
}
