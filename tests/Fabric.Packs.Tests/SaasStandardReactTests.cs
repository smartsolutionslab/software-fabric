using System.Net;

namespace Fabric.Packs.Tests;

/// <summary>Generates saas-standard-react from the real packs (stand-ins for the rest) and checks the result.</summary>
[Collection(FixedPorts.Name)]
public sealed class SaasStandardReactTests
{
    private const string Profile = "saas-standard-react";

    /// <summary>What a module's endpoint looks like with minimal-apis and result; the module packs (M1-04) will add real ones.</summary>
    private const string ProbeEndpoint = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;

        namespace Shop.Modules.Billing.Api;

        public sealed class Probe : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/probe", () => Result<string, ApiError>.Success("pong").ToHttpResult());
        }
        """;

    private const string DatabasePassword = "fabric-tests";

    private static Dictionary<string, string> Dependencies(Container postgres, Container rabbitMq) => new()
    {
        ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
        ["ConnectionStrings__messaging"] = $"amqp://guest:guest@127.0.0.1:{rabbitMq.Port}",
        ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
    };

    [Fact]
    public void The_repository_packs_load_without_errors()
    {
        Repository.Packs.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generates_builds_and_still_builds_after_adding_a_module()
    {
        using var project = new GeneratedProject(Profile);
        project.Build();

        project.AddModule("billing");
        project.Build();

        File.Exists(Path.Combine(project.ProjectDirectory, "src", "Modules", "Billing", "Billing.Api", "bin", "Release", "net10.0", "Shop.Modules.Billing.Api.dll")).ShouldBeTrue();
    }

    [Fact]
    public async Task The_host_finds_module_endpoints_serves_openapi_and_requires_a_token()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/Probe.cs", ProbeEndpoint);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var rabbitMq = Container.RabbitMq();
        using var host = project.StartHost(Dependencies(postgres, rabbitMq));

        (await host.GetAsync("/alive")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.GetAsync("/openapi/v1.json")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.GetAsync("/billing/probe")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void A_new_module_builds_and_its_tests_and_the_architecture_rules_pass()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Build();

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
        moduleOutput.ShouldContain("failed: 0");

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);
        architectureOutput.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_domain_aggregate_built_on_the_building_blocks_keeps_the_architecture_rules_green()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/Invoice.cs", """
            using Shop.BuildingBlocks.Domain;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceId(Guid Value);

            public sealed class Invoice(InvoiceId id) : AggregateRoot<InvoiceId>(id);
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void An_aggregate_fails_with_a_domain_error_that_the_application_maps_to_an_api_error()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/Invoice.cs", """
            using Shop.BuildingBlocks.Domain;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceId(Guid Value);

            public sealed record InvoiceAlreadyPaid(InvoiceId Invoice) : DomainError("billing.invoice-already-paid", "The invoice is already paid.");

            public static class InvoiceErrors
            {
                public static DomainError AlreadyPaid(InvoiceId invoice) => new InvoiceAlreadyPaid(invoice);
            }

            public sealed class Invoice(InvoiceId id) : AggregateRoot<InvoiceId>(id)
            {
                public bool IsPaid { get; private set; }

                public Result<Unit, DomainError> Pay()
                {
                    if (IsPaid)
                    {
                        return Failure(InvoiceErrors.AlreadyPaid(Id));
                    }

                    IsPaid = true;
                    return Success(Unit.Value);
                }
            }
            """);
        project.Write("src/Modules/Billing/Billing.Application/Commands/PayInvoice/PayInvoice.cs", """
            using System.Net;
            using Shop.BuildingBlocks.Messaging;
            using Shop.Modules.Billing.Domain.Invoices;

            namespace Shop.Modules.Billing.Application.Commands.PayInvoice;

            public sealed record PayInvoice(Guid Invoice) : ICommand<Result<Unit, ApiError>>;

            public static class InvoiceFailures
            {
                public static ApiError From(DomainError error) => error switch
                {
                    InvoiceAlreadyPaid => ApiError.From(error, HttpStatusCode.Conflict),
                    _ => ApiError.From(error, HttpStatusCode.UnprocessableEntity),
                };
            }

            public sealed class PayInvoiceHandler : ICommandHandler<PayInvoice, Result<Unit, ApiError>>
            {
                public Task<Result<Unit, ApiError>> HandleAsync(PayInvoice command, CancellationToken cancellationToken)
                {
                    Ensure.That(command).IsNotNull();

                    var invoice = new Invoice(new InvoiceId(command.Invoice));
                    return Task.FromResult(invoice.Pay().MapError(InvoiceFailures.From));
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void A_domain_error_that_speaks_http_fails_the_architecture_tests()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/InvoiceNotFound.cs", """
            using System.Net;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceNotFound(Guid Invoice) : ApiError("billing.invoice-not-found", "No such invoice.", HttpStatusCode.NotFound);
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceNotFound is an ApiError");
    }

    [Fact]
    public void Breaking_an_architecture_rule_fails_the_generated_tests()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/InvoicesController.cs", """
            namespace Shop.Modules.Billing.Api;

            public sealed class InvoicesController : Microsoft.AspNetCore.Mvc.ControllerBase;
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("InvoicesController is a controller");
    }

    [Fact]
    public void A_rule_violation_is_reported_through_the_chosen_assertion_library()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/InvoicesController.cs", """
            namespace Shop.Modules.Billing.Api;

            public sealed class InvoicesController : Microsoft.AspNetCore.Mvc.ControllerBase;
            """);
        project.Build();

        var (_, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        output.ShouldContain("Shouldly.ShouldAssertException");
        output.ShouldContain("No module contains an MVC controller.");
    }

    [Fact]
    public async Task An_integration_event_published_by_one_module_reaches_a_consumer_in_another()
    {
        using var project = new GeneratedProject(Profile);
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
                public static ConcurrentBag<Guid> Received { get; } = [];

                public static void Handle(InvoicePaidV1 message) => Received.Add(message.InvoiceId);
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
        using var rabbitMq = Container.RabbitMq();
        using var host = project.StartHost(Dependencies(postgres, rabbitMq));

        (await host.PostAsync($"/billing/invoices/{Guid.NewGuid()}/pay")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var received = "0";
        for (var attempt = 0; attempt < 60 && received == "0"; attempt++)
        {
            await Task.Delay(500, TestContext.Current.CancellationToken);
            received = await (await host.GetAsync("/shipping/received")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        received.ShouldBe("1");
    }

    [Fact]
    public void A_tenant_cannot_read_another_tenants_rows()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Domain/Note.cs", """
            using Shop.BuildingBlocks.Domain;

            namespace Shop.Modules.Billing.Domain;

            public sealed class Note(Guid id, string text) : Entity<Guid>(id)
            {
                public string Text { get; private set; } = text;
            }
            """);
        project.Write("src/Modules/Billing/Billing.Infrastructure/Persistence/NoteConfiguration.cs", """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Shop.Modules.Billing.Domain;

            namespace Shop.Modules.Billing.Infrastructure.Persistence;

            public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
            {
                public void Configure(EntityTypeBuilder<Note> builder)
                {
                    builder.ToTable("notes");
                    builder.HasKey(note => note.Id);
                    builder.Property(note => note.Text);
                }
            }
            """);
        project.Write("tests/Modules/Billing.Tests/TenancyTests.cs", """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Shop.BuildingBlocks.Tenancy;
            using Shop.Modules.Billing.Api;
            using Shop.Modules.Billing.Domain;
            using Shop.Modules.Billing.Infrastructure.Persistence;

            namespace Shop.Modules.Billing.Tests;

            public class TenancyTests
            {
                [Fact]
                public async Task A_tenant_sees_only_its_own_rows()
                {
                    var cancellationToken = TestContext.Current.CancellationToken;
                    var services = new ServiceCollection().AddLogging().AddTenancy();
                    var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
                    new BillingModule().AddServices(services, configuration);
                    await using var provider = services.BuildServiceProvider();

                    using (TenantScope.Begin("tenant-a"))
                    {
                        await using var scope = provider.CreateAsyncScope();
                        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                        await context.Database.EnsureCreatedAsync(cancellationToken);
                        context.Add(new Note(Guid.NewGuid(), "only for tenant a"));
                        await context.SaveChangesAsync(cancellationToken);
                    }

                    (await CountAsync(provider, "tenant-b", cancellationToken)).ShouldBe(0);
                    (await CountAsync(provider, "tenant-a", cancellationToken)).ShouldBe(1);
                }

                private static async Task<int> CountAsync(ServiceProvider provider, string tenant, CancellationToken cancellationToken)
                {
                    using var tenantScope = TenantScope.Begin(tenant);
                    await using var scope = provider.CreateAsyncScope();
                    return await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Set<Note>().CountAsync(cancellationToken);
                }
            }
            """);
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        var (exitCode, output) = project.Test(
            "tests/Modules/Billing.Tests/Billing.Tests.csproj",
            new Dictionary<string, string> { ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}" });

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void The_generated_integration_tests_pass_against_the_running_aspire_stack()
    {
        using var project = new GeneratedProject(Profile);
        project.Build();

        var (exitCode, output) = project.Test("tests/Integration.Tests/Integration.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        output.ShouldContain("succeeded: 7");
    }

    [Theory]
    [InlineData("src/Modules/Billing/Billing.Domain/Invoice.cs", "## What changed\nPaid invoices.", 1)]
    [InlineData("src/Modules/Billing/Billing.Domain/Invoice.cs", "## Tests observed failing\n\n```\nfailed Billing.Tests.An_invoice_can_be_paid\n```", 0)]
    [InlineData("docs/adr/0001-base-core.md", "## What changed\nTypo.", 0)]
    [InlineData("tests/Modules/Billing.Tests/InvoiceTests.cs", "## What changed\nMore tests.", 0)]
    public void The_red_first_check_wants_failing_output_when_source_changes(string changedFile, string body, int expectedExitCode)
    {
        using var project = new GeneratedProject(Profile);
        var bodyFile = Path.Combine(project.ProjectDirectory, "pr-body.md");
        var changedFiles = Path.Combine(project.ProjectDirectory, "changed-files.txt");
        File.WriteAllText(bodyFile, body);
        File.WriteAllText(changedFiles, changedFile + "\n");

        var (exitCode, output) = Shell.Run(project.ProjectDirectory, "bash", ".github/scripts/check-red-first.sh", bodyFile, changedFiles);

        exitCode.ShouldBe(expectedExitCode, output);
    }
}
