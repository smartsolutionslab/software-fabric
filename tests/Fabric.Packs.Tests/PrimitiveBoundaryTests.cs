using System.Diagnostics;
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

        internal sealed class Node<T>
        {
            public Node<List<T>>? Next { get; }
        }

        internal sealed class Route
        {
            public Node<Shipment>? Start { get; }
        }

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

    private const string Shipments = """
        using Shop.BuildingBlocks.Domain;

        namespace Shop.Modules.Billing.Domain.Shipments;

        public enum ShipmentStatus
        {
            Packed,
            Sent,
        }

        public sealed class Shipment : AggregateRoot<ShipmentId>
        {
            private readonly List<ShipmentStatus> history = [];

            private Shipment(ShipmentId id)
                : base(id) => Status = ShipmentStatus.Packed;

            public ShipmentStatus Status { get; private set; }

            public ShipmentStatus? Previous { get; private set; }

            public IReadOnlyList<ShipmentStatus> History => history;

            public DayOfWeek Dispatch { get; private set; }

            public static Shipment Pack() => new(ShipmentId.New());
        }

        public sealed record ShipmentId : IValueObject<Guid>
        {
            private ShipmentId(Guid value) => Value = value;

            public Guid Value { get; }

            public static ShipmentId New() => new(Guid.CreateVersion7());

            public static ShipmentId From(Guid value)
            {
                Ensure.That(value).IsNotEmpty();
                return new ShipmentId(value);
            }
        }
        """;

    private const string Routes = """
        using Shop.BuildingBlocks.Domain;

        namespace Shop.Modules.Billing.Domain.Routes;

        internal sealed class Waypoint;

        internal class Node<T>
        {
            public Node<List<T>>? Next { get; }
        }

        internal sealed class SpecialNode : Node<List<Waypoint>>
        {
            public string Remark { get; } = string.Empty;
        }

        internal sealed record Wrapper<T> : IValueObject<T>
            where T : class
        {
            private Wrapper(T value) => Value = value;

            public T Value { get; }

            public Wrapper<Link<T>>? Linked { get; }

            public static Wrapper<T> From(T value)
            {
                Ensure.That(value).IsNotNull();
                return new Wrapper<T>(value);
            }
        }

        internal sealed class Cargo
        {
            public string Text { get; } = string.Empty;
        }

        internal sealed class Link<T>
        {
            public string Label { get; } = string.Empty;
        }

        internal sealed class Marker;

        internal sealed class Manifest
        {
            public string Note { get; } = string.Empty;
        }

        internal sealed record Tag<TTag> : IValueObject<Guid>
        {
            private Tag(Guid value) => Value = value;

            public Guid Value { get; }

            public Tag<Manifest>? Related { get; }

            public Tag<List<TTag>>? Next { get; }

            public static Tag<TTag> From(Guid value)
            {
                Ensure.That(value).IsNotEmpty();
                return new Tag<TTag>(value);
            }
        }

        internal sealed class Route : AggregateRoot<RouteId>
        {
            private Route(RouteId id)
                : base(id)
            {
            }

            public Node<Waypoint>? Start { get; }

            public Wrapper<Wrapper<Cargo>>? Load { get; }

            public Tag<Marker>? Mark { get; }

            public int Hops { get; }

            public static Route Plan() => new(RouteId.New());
        }

        internal sealed record RouteId : IValueObject<Guid>
        {
            private RouteId(Guid value) => Value = value;

            public Guid Value { get; }

            public static RouteId New() => new(Guid.CreateVersion7());

            public static RouteId From(Guid value)
            {
                Ensure.That(value).IsNotEmpty();
                return new RouteId(value);
            }
        }
        """;

    private const string Crates = """
        using Shop.BuildingBlocks.Domain;

        namespace Shop.Modules.Billing.Domain.Crates;

        internal sealed class Marker;

        internal sealed record Box<T> : IValueObject<T>
            where T : class
        {
            private Box(T value) => Value = value;

            public T Value { get; }

            public Cell<T>? State { get; }

            public Box<List<string>>? Next { get; }

            public static Box<T> From(T value)
            {
                Ensure.That(value).IsNotNull();
                return new Box<T>(value);
            }
        }

        internal sealed class Cell<T>
            where T : class
        {
            public T? Value { get; }
        }

        internal sealed record Sleeve<T> : IValueObject<T>
            where T : class
        {
            private Sleeve(T value) => Value = value;

            public T Value { get; }

            public Holder<T>? State { get; }

            public Sleeve<Tuple<T, int>>? Next { get; }

            public static Sleeve<T> From(T value)
            {
                Ensure.That(value).IsNotNull();
                return new Sleeve<T>(value);
            }
        }

        internal sealed class Holder<T>
            where T : class
        {
            public T? Value { get; }
        }

        internal sealed class Crate : AggregateRoot<CrateId>
        {
            private Crate(CrateId id)
                : base(id)
            {
            }

            public Box<Marker>? Packing { get; }

            public Sleeve<Marker>? Cover { get; }

            public static Crate Pack() => new(CrateId.New());
        }

        internal sealed record CrateId : IValueObject<Guid>
        {
            private CrateId(Guid value) => Value = value;

            public Guid Value { get; }

            public static CrateId New() => new(Guid.CreateVersion7());

            public static CrateId From(Guid value)
            {
                Ensure.That(value).IsNotEmpty();
                return new CrateId(value);
            }
        }
        """;

    private const string Signals = """
        using Shop.BuildingBlocks.Domain;

        namespace Shop.Modules.Billing.Domain.Signals;

        internal sealed class Marker;

        internal interface INode<out T>
        {
            INode<IEnumerable<T>>? Next { get; }
        }

        internal sealed class SpecialNode : INode<List<Marker>>
        {
            public INode<IEnumerable<List<Marker>>>? Next => null;

            public string Remark { get; } = string.Empty;
        }

        internal sealed class Palette<T>
        {
            internal enum Status
            {
                On,
                Off,
            }
        }

        internal sealed class Lamp<T>
        {
            public Lamp<List<T>>? Next { get; }

            public Palette<T>.Status Status { get; }
        }

        internal sealed record Chain<T> : IValueObject<T>
        {
            private Chain(T value) => Value = value;

            public T Value { get; }

            public Chain<List<T>>? Next { get; }

            public Answer<T>? Answer { get; }

            public static Chain<T> From(T value) => new(value);
        }

        internal sealed class Answer<T>
        {
            private readonly T[] values = [];

            public T Value => values.Length > 0 ? values[0] : default!;
        }

        internal sealed class Signal : AggregateRoot<SignalId>
        {
            private Signal(SignalId id)
                : base(id)
            {
            }

            public INode<Marker>? Start { get; }

            public Lamp<Marker>? Lamp { get; }

            public Chain<bool>? Checks { get; }

            public static Signal Raise() => new(SignalId.New());
        }

        internal sealed record SignalId : IValueObject<Guid>
        {
            private SignalId(Guid value) => Value = value;

            public Guid Value { get; }

            public static SignalId New() => new(Guid.CreateVersion7());

            public static SignalId From(Guid value)
            {
                Ensure.That(value).IsNotEmpty();
                return new SignalId(value);
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

    [Fact]
    public void An_aggregate_holding_an_enum_passes_the_gate()
    {
        using var project = new GeneratedProject("saas-strict-react");
        project.AddModule("billing");

        project.WriteTypes("src/Modules/Billing/Billing.Domain/Shipments", Shipments);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void A_self_expanding_generic_type_ends_the_walk_without_hiding_what_it_holds_its_subclasses_or_following_a_phantom_tag()
    {
        using var project = new GeneratedProject("saas-strict-react");
        project.AddModule("billing");

        project.WriteTypes("src/Modules/Billing/Billing.Domain/Routes", Routes);
        project.Build();

        var (ended, exitCode, output) = TestWithin(project, "tests/Architecture.Tests/Architecture.Tests.csproj", TimeSpan.FromMinutes(3));

        List<string> named = [.. Violation().Matches(output).Select(match => match.Value).Distinct()];

        output.ShouldSatisfyAllConditions(
            () => ended.ShouldBeTrue($"The architecture tests did not end within 3 minutes.\n{output}"),
            () => exitCode.ShouldNotBe(0, output),
            () => named.ShouldBe(
            [
                "Shop.Modules.Billing.Domain.Routes.Cargo.Text holds String",
                "Shop.Modules.Billing.Domain.Routes.Route.Hops holds Int32",
                "Shop.Modules.Billing.Domain.Routes.SpecialNode.Remark holds String",
            ]),
            () => output.ShouldContain("].Label holds String"),
            () => output.ShouldNotContain("Manifest.Note holds"),
            () => output.ShouldContain("failed: 1"));
    }

    [Fact]
    public void State_built_from_a_larger_generic_types_arguments_is_walked_whether_or_not_the_growth_ends()
    {
        using var project = new GeneratedProject("saas-strict-react");
        project.AddModule("billing");

        project.WriteTypes("src/Modules/Billing/Billing.Domain/Crates", Crates);
        project.Build();

        var (ended, exitCode, output) = TestWithin(project, "tests/Architecture.Tests/Architecture.Tests.csproj", TimeSpan.FromMinutes(3));

        List<string> named = [.. Violation().Matches(output).Select(match => match.Value).Distinct()];

        // Box<List<string>> is one fixed type, so the graph is finite; Sleeve<Tuple<T, int>> grows without end.
        output.ShouldSatisfyAllConditions(
            () => ended.ShouldBeTrue($"The architecture tests did not end within 3 minutes.\n{output}"),
            () => exitCode.ShouldNotBe(0, output),
            () => output.ShouldMatch(@"Crates\.Cell`1\[\[System\.Collections\.Generic\.List`1\[\[System\.String,[^;]*\]\]\.Value holds String"),
            () => output.ShouldMatch(@"Crates\.Holder`1\[\[System\.Tuple`2\[\[Shop\.Modules\.Billing\.Domain\.Crates\.Marker,[^;]*\],\[System\.Int32,[^;]*\]\]\.Value holds Int32"),
            () => named.ShouldBeEmpty(),
            () => output.ShouldContain("failed: 1"));
    }

    [Fact]
    public void A_recurring_definition_keeps_the_enum_and_computed_answer_exemptions_and_reaches_a_covariant_subclass()
    {
        using var project = new GeneratedProject("saas-strict-react");
        project.AddModule("billing");

        project.WriteTypes("src/Modules/Billing/Billing.Domain/Signals", Signals);
        project.Build();

        var (ended, exitCode, output) = TestWithin(project, "tests/Architecture.Tests/Architecture.Tests.csproj", TimeSpan.FromMinutes(3));

        List<string> named = [.. Violation().Matches(output).Select(match => match.Value).Distinct()];

        // INode<IEnumerable<Marker>> is reached, and SpecialNode : INode<List<Marker>> is assignable to it. Answer<bool>.Value
        // is a computed bool, but Answer<List<bool>>.Value, reached through Chain<List<bool>>, is not.
        output.ShouldSatisfyAllConditions(
            () => ended.ShouldBeTrue($"The architecture tests did not end within 3 minutes.\n{output}"),
            () => exitCode.ShouldNotBe(0, output),
            () => named.ShouldBe(["Shop.Modules.Billing.Domain.Signals.SpecialNode.Remark holds String"]),
            () => output.ShouldMatch(@"Signals\.Answer`1\[\[System\.Collections\.Generic\.List`1\[\[System\.Boolean,[^;]*\]\]\.Value holds Boolean"),
            () => output.ShouldNotMatch(@"Signals\.Answer`1\[\[System\.Boolean,"),
            () => output.ShouldNotContain("value__"),
            () => output.ShouldContain("failed: 1"));
    }

    /// <summary>
    /// Runs one test project of the generated solution and stops it, with every process it started, once the limit
    /// passes: a walk that never ends spins without ever reaching a cancellation point, so the platform's own
    /// <c>--timeout</c> cannot end it.
    /// </summary>
    private static (bool Ended, int ExitCode, string Output) TestWithin(GeneratedProject project, string testProject, TimeSpan limit)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = project.ProjectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in (string[])["test", "--project", testProject, "-c", "Release", "--no-build", "--no-ansi"])
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var ended = process.WaitForExit(limit);
        if (!ended)
        {
            process.Kill(entireProcessTree: true);
        }

        process.WaitForExit();
        return (ended, ended ? process.ExitCode : -1, standardOutput.Result + standardError.Result);
    }

    [GeneratedRegex(@"Shop\.Modules\.\S+ holds \w+")]
    private static partial Regex Violation();
}
