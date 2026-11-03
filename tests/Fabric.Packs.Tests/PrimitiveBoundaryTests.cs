using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>
/// module.ddd-depth.strict: no aggregate (nor the entities and value objects reachable from it), command or domain
/// event of a strict module holds a primitive; queries and DTOs may. Value objects and identifiers map to EF Core by
/// convention.
/// </summary>
public sealed partial class PrimitiveBoundaryTests
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

        public class Fee(InvoiceId id) : Entity<InvoiceId>(id);

        public sealed class LateFee(InvoiceId id, decimal surcharge) : Fee(id)
        {
            public decimal Surcharge { get; } = surcharge;
        }

        internal sealed class Ledger : AggregateRoot<InvoiceId>
        {
            public readonly string Note = "none";

            private Ledger(InvoiceId id)
                : base(id)
            {
            }

            internal static Ledger Open() => new(InvoiceId.New());
        }

        public sealed class Account : AggregateRoot<InvoiceId>, IValueObject
        {
            private Account(InvoiceId id)
                : base(id)
            {
            }

            public string Holder { get; private set; } = "none";

            public static Account Open() => new(InvoiceId.New());
        }

        [DeliberatelyUnhandled("The test only needs an event that carries a primitive behind the value-object marker.")]
        public sealed record InvoiceVoided(InvoiceId Invoice, string Reason) : IDomainEvent, IValueObject;

        [DeliberatelyUnhandled("The test only needs an event that carries a primitive.")]
        public sealed record InvoiceIssued(InvoiceId Invoice, decimal Total) : IDomainEvent;

        public sealed class Invoice : AggregateRoot<InvoiceId>
        {
            private readonly List<string> tags = [];
            private readonly List<InvoiceLine> lines = [];
            private readonly List<Fee> fees = [];
            private bool archived;
            private volatile bool flagged;

            private Invoice(InvoiceId id, InvoiceNumber number)
                : base(id) => Number = number;

            public InvoiceNumber Number { get; set; }

            public string Reference { get; private set; } = "none";

            public IReadOnlyList<string> Tags => tags;

            public IReadOnlyList<InvoiceLine> Lines => lines;

            public IReadOnlyList<Fee> Fees => fees;

            public bool HasLines => lines.Count > 0;

            public bool IsArchived => archived;

            public bool IsFlagged => flagged;

            public void Archive() => archived = true;

            public void Flag() => flagged = true;

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

    private const string Orders = """
        using System.Numerics;
        using Shop.BuildingBlocks.Domain;

        namespace Shop.Modules.Billing.Domain.Orders;

        internal sealed record TypedId<TTag> : IValueObject<Guid>
        {
            private TypedId(Guid value) => Value = value;

            public Guid Value { get; }

            public static TypedId<TTag> New() => new(Guid.CreateVersion7());

            public static TypedId<TTag> From(Guid value)
            {
                Ensure.That(value).IsNotEmpty();
                return new TypedId<TTag>(value);
            }
        }

        internal sealed record Positive<T> : IValueObject<T>
            where T : INumber<T>
        {
            private Positive(T value) => Value = value;

            public T Value { get; }

            public static Positive<T> From(T value)
            {
                if (value <= T.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "A positive value is greater than zero.");
                }

                return new Positive<T>(value);
            }
        }

        internal sealed class Shipment;

        internal sealed class Line<TTag>(TypedId<TTag> id) : Entity<TypedId<TTag>>(id);

        internal sealed class Order : AggregateRoot<TypedId<Order>>
        {
            private readonly List<Line<Shipment>> lines = [];
            private readonly List<Positive<int>> parcels = [];
            private readonly List<int> weights = [];

            private Order(TypedId<Order> id, Positive<int> quantity)
                : base(id) => Quantity = quantity;

            public Positive<int> Quantity { get; private set; }

            public IReadOnlyList<Line<Shipment>> Lines => lines;

            public IReadOnlyList<Positive<int>> Parcels => parcels;

            public IReadOnlyList<int> Weights => weights;

            public static Order Place(Positive<int> quantity) => new(TypedId<Order>.New(), quantity);
        }
        """;

    private const string OrdersVisibleToInfrastructure = """
        using System.Runtime.CompilerServices;

        [assembly: InternalsVisibleTo("Shop.Modules.Billing.Infrastructure")]
        """;

    private const string OrderConfiguration = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Shop.Modules.Billing.Domain.Orders;

        namespace Shop.Modules.Billing.Infrastructure.Persistence.Configurations;

        internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
        {
            public void Configure(EntityTypeBuilder<Order> builder)
            {
                builder.ToTable("orders");
                builder.HasKey(order => order.Id);
                builder.Property(order => order.Quantity);
                builder.Ignore(order => order.Lines);
                builder.Ignore(order => order.Parcels);
                builder.Ignore(order => order.Weights);
            }
        }
        """;

    private const string OrderLineConfiguration = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Shop.Modules.Billing.Domain.Orders;

        namespace Shop.Modules.Billing.Infrastructure.Persistence.Configurations;

        internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<Line<Shipment>>
        {
            public void Configure(EntityTypeBuilder<Line<Shipment>> builder)
            {
                builder.ToTable("order_lines");
                builder.HasKey(line => line.Id);
            }
        }
        """;

    private const string OrderModelTests = """
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;
        using Shop.Modules.Billing.Infrastructure.Persistence;

        namespace Shop.Modules.Billing.Tests;

        public class OrderModelTests
        {
            [Fact]
            public void Closed_generic_value_objects_map_to_their_backing_values_by_convention()
            {
                using var services = new ServiceCollection().BuildServiceProvider();
                var options = new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql("Host=localhost").Options;
                using var context = new BillingDbContext(options, services);

                var order = context.Model.FindEntityType("Shop.Modules.Billing.Domain.Orders.Order").ShouldNotBeNull();

                order.FindProperty("Id").ShouldNotBeNull().GetTypeMapping().Converter.ShouldNotBeNull().ProviderClrType.ShouldBe(typeof(Guid));
                order.FindProperty("Quantity").ShouldNotBeNull().GetTypeMapping().Converter.ShouldNotBeNull().ProviderClrType.ShouldBe(typeof(int));
            }

            [Fact]
            public void A_closed_generic_value_object_reached_only_through_a_generic_entity_maps_by_convention()
            {
                using var services = new ServiceCollection().BuildServiceProvider();
                var options = new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql("Host=localhost").Options;
                using var context = new BillingDbContext(options, services);

                var line = context.Model.GetEntityTypes().Single(type => type.ClrType.Name == "Line`1");

                line.FindProperty("Id").ShouldNotBeNull().GetTypeMapping().Converter.ShouldNotBeNull().ProviderClrType.ShouldBe(typeof(Guid));
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
        File.Exists(Path.Combine(project.ProjectDirectory, "tests/Architecture.Tests/Rules/BillingHoldsNoPrimitives.cs")).ShouldBeTrue();

        project.Build();
        var (untouchedArchitecture, untouchedArchitectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        untouchedArchitecture.ShouldBe(0, untouchedArchitectureOutput);
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
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Invoice.IsArchived holds Boolean");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Ledger.Note holds String");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.LateFee.Surcharge holds Decimal");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Account.Holder holds String");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceVoided.Reason holds String");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Invoice.IsFlagged holds Boolean");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Invoice.Number has a public setter.");
        output.ShouldNotContain("InvoiceNumber.Value holds");
        output.ShouldNotContain("InvoiceId.Value holds");
        output.ShouldNotContain("Invoice.HasLines holds");
        output.ShouldNotContain("Invoice.DomainEvents holds");
        output.ShouldNotContain("FindInvoiceNumber.Number holds");
        output.ShouldNotContain("InvoiceDto.Number holds");
        output.ShouldNotContain("Domain.Customers.");
        output.ShouldNotContain("Invoice.Reference has a public setter");
        output.ShouldContain("failed: 2");

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        moduleExitCode.ShouldBe(0, moduleOutput);
    }

    [Fact]
    public void Generic_value_objects_map_in_ef_and_end_the_walk_while_a_collection_of_primitives_still_fails()
    {
        using var project = new GeneratedProject("saas-strict-react");
        project.AddModule("billing");

        project.WriteTypes("src/Modules/Billing/Billing.Domain/Orders", Orders);
        project.Write("src/Modules/Billing/Billing.Domain/Orders/OrdersVisibleToInfrastructure.cs", OrdersVisibleToInfrastructure);
        project.WriteTypes("src/Modules/Billing/Billing.Infrastructure/Persistence/Configurations", OrderConfiguration);
        project.WriteTypes("src/Modules/Billing/Billing.Infrastructure/Persistence/Configurations", OrderLineConfiguration);
        project.WriteTypes("tests/Modules/Billing.Tests", OrderModelTests);
        project.Build();

        var (moduleExitCode, moduleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        List<string> named = [.. Violation().Matches(output).Select(match => match.Value).Distinct()];

        // Both halves at once, so a run names every case that fails rather than only the first.
        output.ShouldSatisfyAllConditions(
            () => moduleExitCode.ShouldBe(0, moduleOutput),
            () => exitCode.ShouldNotBe(0, output),
            () => named.ShouldBe(["Shop.Modules.Billing.Domain.Orders.Order.Weights holds Int32"]),
            () => output.ShouldContain("failed: 1"));
    }

    [GeneratedRegex(@"Shop\.Modules\.\S+ holds \w+")]
    private static partial Regex Violation();
}
