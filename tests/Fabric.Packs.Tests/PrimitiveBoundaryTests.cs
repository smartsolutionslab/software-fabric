namespace Fabric.Packs.Tests;

/// <summary>
/// module.ddd-depth.strict: no aggregate (nor the entities and value objects reachable from it), command or domain
/// event of a strict module holds a primitive; queries and DTOs may. Value objects and identifiers map to EF Core by
/// convention.
/// </summary>
public sealed class PrimitiveBoundaryTests
{
    private const string Invoices = """
        using Shop.BuildingBlocks.Domain;

        namespace Shop.Modules.Billing.Domain.Invoices;

        public sealed record InvoiceId : IValueObject<Guid>
        {
            private InvoiceId(Guid value) => Value = value;

            public Guid Value { get; }

            public static InvoiceId New() => new(Guid.CreateVersion7());

            public static InvoiceId From(Guid value)
            {
                Ensure.That(value).IsNotEmpty();
                return new InvoiceId(value);
            }
        }

        public sealed record InvoiceNumber : IValueObject<string>
        {
            private InvoiceNumber(string value) => Value = value;

            public string Value { get; }

            public static InvoiceNumber From(string value)
            {
                Ensure.That(value).IsNotNullOrWhiteSpace();
                return new InvoiceNumber(value.Trim());
            }
        }

        public sealed class InvoiceLine(Guid id, int quantity) : Entity<Guid>(id)
        {
            public int Quantity { get; } = quantity;
        }

        [DeliberatelyUnhandled("The test only needs an event that carries a primitive.")]
        public sealed record InvoiceIssued(InvoiceId Invoice, decimal Total) : IDomainEvent;

        public sealed class Invoice : AggregateRoot<InvoiceId>
        {
            private readonly List<string> tags = [];
            private readonly List<InvoiceLine> lines = [];

            private Invoice(InvoiceId id, InvoiceNumber number)
                : base(id) => Number = number;

            public InvoiceNumber Number { get; private set; }

            public string Reference { get; private set; } = "none";

            public IReadOnlyList<string> Tags => tags;

            public IReadOnlyList<InvoiceLine> Lines => lines;

            public bool HasLines => lines.Count > 0;

            public static Invoice Issue(InvoiceNumber number)
            {
                var invoice = new Invoice(InvoiceId.New(), number);
                invoice.Raise(new InvoiceIssued(invoice.Id, 0m));
                return invoice;
            }
        }
        """;

    private const string Customers = """
        using Shop.BuildingBlocks.Domain;

        namespace Shop.Modules.Billing.Domain.Customers;

        public sealed record CustomerId : IValueObject<Guid>
        {
            private CustomerId(Guid value) => Value = value;

            public Guid Value { get; }

            public static CustomerId New() => new(Guid.CreateVersion7());

            public static CustomerId From(Guid value)
            {
                Ensure.That(value).IsNotEmpty();
                return new CustomerId(value);
            }
        }

        public sealed record CustomerName : IValueObject<string>
        {
            private CustomerName(string value) => Value = value;

            public string Value { get; }

            public static CustomerName From(string value)
            {
                Ensure.That(value).IsNotNullOrWhiteSpace();
                return new CustomerName(value.Trim());
            }
        }

        public sealed class Customer : AggregateRoot<CustomerId>
        {
            private Customer(CustomerId id, CustomerName name)
                : base(id) => Name = name;

            public CustomerName Name { get; private set; }

            public static Customer Register(CustomerName name) => new(CustomerId.New(), name);
        }
        """;

    private const string CustomerConfiguration = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Shop.Modules.Billing.Domain.Customers;

        namespace Shop.Modules.Billing.Infrastructure.Persistence.Configurations;

        public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
        {
            public void Configure(EntityTypeBuilder<Customer> builder)
            {
                builder.ToTable("customers");
                builder.HasKey(customer => customer.Id);
                builder.Property(customer => customer.Name);
            }
        }
        """;

    private const string Commands = """
        using Shop.BuildingBlocks.Messaging;
        using Shop.Modules.Billing.Domain.Invoices;

        namespace Shop.Modules.Billing.Application.Commands.Invoices;

        public sealed record IssueInvoice(string Number) : ICommand<Result<InvoiceId, ApiError>>;

        public sealed class IssueInvoiceHandler : ICommandHandler<IssueInvoice, Result<InvoiceId, ApiError>>
        {
            public async Task<Result<InvoiceId, ApiError>> HandleAsync(IssueInvoice command, CancellationToken cancellationToken)
            {
                await Task.Yield();
                return Success(Invoice.Issue(InvoiceNumber.From(command.Number)).Id);
            }
        }
        """;

    private const string Queries = """
        using Shop.BuildingBlocks.Messaging;

        namespace Shop.Modules.Billing.Application.Queries.Invoices;

        public sealed record InvoiceDto(string Number, decimal Total);

        public sealed record FindInvoiceNumber(string Number) : IQuery<Result<string, ApiError>>;

        public sealed class FindInvoiceNumberHandler : IQueryHandler<FindInvoiceNumber, Result<string, ApiError>>
        {
            public async Task<Result<string, ApiError>> HandleAsync(FindInvoiceNumber query, CancellationToken cancellationToken)
            {
                await Task.Yield();
                return Success(query.Number);
            }
        }
        """;

    private const string CustomerModelTests = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;
        using Shop.Modules.Billing.Domain.Customers;
        using Shop.Modules.Billing.Infrastructure.Persistence;

        namespace Shop.Modules.Billing.Tests;

        public class CustomerModelTests
        {
            [Fact]
            public void Value_objects_and_identifiers_map_to_their_backing_values_by_convention()
            {
                using var services = new ServiceCollection().BuildServiceProvider();
                var options = new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql("Host=localhost").Options;
                using var context = new BillingDbContext(options, services);

                var customer = context.Model.FindEntityType(typeof(Customer)).ShouldNotBeNull();

                customer.FindProperty(nameof(Customer.Id)).ShouldNotBeNull().GetTypeMapping().Converter.ShouldNotBeNull().ProviderClrType.ShouldBe(typeof(Guid));
                customer.FindProperty(nameof(Customer.Name)).ShouldNotBeNull().GetTypeMapping().Converter.ShouldNotBeNull().ProviderClrType.ShouldBe(typeof(string));
            }
        }
        """;

    [Fact]
    public void A_strict_module_passes_with_its_samples_and_fails_naming_each_primitive_an_aggregate_command_or_event_holds()
    {
        using var project = new GeneratedProject("saas-standard-react", "module.ddd-depth=strict");
        project.AddModule("billing");

        File.Exists(Path.Combine(project.ProjectDirectory, "src/Modules/Billing/Billing.Domain/Samples/SampleId.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(project.ProjectDirectory, "src/Modules/Billing/Billing.Domain/Samples/SampleName.cs")).ShouldBeTrue();

        project.Build();
        var (untouchedArchitecture, untouchedArchitectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        untouchedArchitecture.ShouldBe(0, untouchedArchitectureOutput);
        untouchedArchitectureOutput.ShouldContain("BillingHoldsNoPrimitives");
        var (untouchedModule, untouchedModuleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        untouchedModule.ShouldBe(0, untouchedModuleOutput);

        project.WriteTypes("src/Modules/Billing/Billing.Domain/Invoices", Invoices);
        project.WriteTypes("src/Modules/Billing/Billing.Domain/Customers", Customers);
        project.WriteTypes("src/Modules/Billing/Billing.Infrastructure/Persistence/Configurations", CustomerConfiguration);
        project.WriteTypes("src/Modules/Billing/Billing.Application/Commands/Invoices", Commands);
        project.WriteTypes("src/Modules/Billing/Billing.Application/Queries/Invoices", Queries);
        project.WriteTypes("tests/Modules/Billing.Tests", CustomerModelTests);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Invoice.Reference holds String");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Invoice.Tags holds String");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceLine.Id holds Guid");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceLine.Quantity holds Int32");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceIssued.Total holds Decimal");
        output.ShouldContain("Shop.Modules.Billing.Application.Commands.Invoices.IssueInvoice.Number holds String");
        output.ShouldNotContain("InvoiceNumber.Value holds");
        output.ShouldNotContain("InvoiceId.Value holds");
        output.ShouldNotContain("Invoice.HasLines holds");
        output.ShouldNotContain("Invoice.DomainEvents holds");
        output.ShouldNotContain("FindInvoiceNumber.Number holds");
        output.ShouldNotContain("InvoiceDto.Number holds");
        output.ShouldNotContain("Domain.Customers.");
        output.ShouldContain("failed: 1");

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
    }
}
