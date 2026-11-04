namespace Fabric.Packs.Tests;

/// <summary>
/// module.ddd-depth.strict in an event-sourced module: value objects round-trip through Marten's JSON by convention,
/// and the strict rules walk from the event-sourced aggregates and events. The industrial profile's frontend packs are
/// not written yet, so its frontend data layer comes from saas-standard-react (as in EventDrivenBackendTests).
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class StrictEventSourcedTests
{
    private const string DatabasePassword = "fabric-tests";

    private static readonly string[] Choices =
        ["module.ddd-depth=strict", "frontend.state=redux-toolkit", "frontend.api-client=rtk-query-codegen"];

    private const string Invoices = """
        using Shop.BuildingBlocks.EventSourcing;

        namespace Shop.Modules.Billing.Domain.Invoices;

        public sealed record InvoiceId : IValueObject<Guid>, IAggregateId
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
                return new InvoiceNumber(value);
            }
        }

        public sealed record LineCount : IValueObject<int>
        {
            private LineCount(int value) => Value = value;

            public int Value { get; }

            public static LineCount From(int value)
            {
                Ensure.That(value).AtLeast(1);
                return new LineCount(value);
            }
        }

        public sealed record InvoiceIssued(InvoiceId Invoice, InvoiceNumber Number, LineCount Lines) : IDomainEvent;

        public sealed record InvoiceRenumbered(InvoiceId Invoice, InvoiceNumber Number, InvoiceNumber? Previous) : IDomainEvent;

        public sealed class Invoice : EventSourcedAggregate<InvoiceId>
        {
            private Invoice()
            {
            }

            public InvoiceNumber Number { get; private set; } = default!;

            public LineCount Lines { get; private set; } = default!;

            public static Invoice Issue(InvoiceId id, InvoiceNumber number, LineCount lines)
            {
                var invoice = new Invoice();
                invoice.Raise(new InvoiceIssued(id, number, lines));
                return invoice;
            }

            public void Renumber(InvoiceNumber number) => Raise(new InvoiceRenumbered(Id, number, Number));

            private void Apply(InvoiceIssued issued)
            {
                Id = issued.Invoice;
                Number = issued.Number;
                Lines = issued.Lines;
            }

            private void Apply(InvoiceRenumbered renumbered) => Number = renumbered.Number;
        }
        """;

    private const string EventStoreTests = """"
        using System.Text;
        using System.Text.Json;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;
        using JasperFx;
        using Marten;
        using Shop.BuildingBlocks.EventStore;
        using Shop.Modules.Billing.Api;
        using Shop.Modules.Billing.Application;
        using Shop.Modules.Billing.Domain.Invoices;
        using Shop.Modules.Billing.Infrastructure.EventStore;

        namespace Shop.Modules.Billing.Tests;

        public class EventStoreTests
        {
            [Fact]
            public async Task An_aggregate_and_its_events_built_from_value_objects_round_trip_through_the_event_store()
            {
                var cancellationToken = TestContext.Current.CancellationToken;
                await using var provider = Provider();
                var id = InvoiceId.New();

                await using (var scope = provider.CreateAsyncScope())
                {
                    var invoice = Invoice.Issue(id, InvoiceNumber.From("INV-1"), LineCount.From(3));
                    invoice.Renumber(InvoiceNumber.From("INV-2"));
                    await scope.ServiceProvider.GetRequiredService<IBillingAggregateStore>().SaveAsync(invoice, cancellationToken);
                }

                await using (var scope = provider.CreateAsyncScope())
                {
                    var loaded = await scope.ServiceProvider.GetRequiredService<IBillingAggregateStore>().LoadAsync<Invoice>(id, cancellationToken);

                    loaded.HasValue.ShouldBeTrue();
                    loaded.Value.Id.ShouldBe(id);
                    loaded.Value.Number.ShouldBe(InvoiceNumber.From("INV-2"));
                    loaded.Value.Lines.ShouldBe(LineCount.From(3));
                    loaded.Value.Version.ShouldBe(2);
                }
            }

            [Fact]
            public void A_value_object_is_stored_as_its_backing_value()
            {
                var json = Serializer().ToJson(new InvoiceIssued(InvoiceId.From(Guid.Parse("0190a6d4-0000-7000-8000-000000000001")), InvoiceNumber.From("INV-1"), LineCount.From(3)));

                json.ShouldBe("""{"Invoice":"0190a6d4-0000-7000-8000-000000000001","Number":"INV-1","Lines":3}""");
            }

            [Fact]
            public void A_value_read_from_the_event_store_passes_the_guards_of_its_value_object()
            {
                using var stored = new MemoryStream(Encoding.UTF8.GetBytes("""{"Invoice":"0190a6d4-0000-7000-8000-000000000001","Number":"  ","Lines":3}"""));

                Should.Throw<GuardException>(() => Serializer().FromJson<InvoiceIssued>(stored));
            }

            [Fact]
            public void A_null_for_a_value_object_that_is_not_nullable_is_refused()
            {
                using var stored = new MemoryStream(Encoding.UTF8.GetBytes("""{"Invoice":"0190a6d4-0000-7000-8000-000000000001","Number":null,"Lines":3}"""));

                Should.Throw<JsonException>(() => Serializer().FromJson<InvoiceIssued>(stored));
            }

            [Fact]
            public void An_omitted_value_object_is_refused_rather_than_read_as_null()
            {
                using var stored = new MemoryStream(Encoding.UTF8.GetBytes("""{"Invoice":"0190a6d4-0000-7000-8000-000000000001","Lines":3}"""));

                Should.Throw<JsonException>(() => Serializer().FromJson<InvoiceIssued>(stored));
            }

            [Fact]
            public void A_value_object_declared_nullable_may_be_null()
            {
                using var stored = new MemoryStream(Encoding.UTF8.GetBytes("""{"Invoice":"0190a6d4-0000-7000-8000-000000000001","Number":"INV-2","Previous":null}"""));

                Serializer().FromJson<InvoiceRenumbered>(stored).Previous.ShouldBeNull();
            }

            [Fact]
            public void A_value_object_keys_a_dictionary_by_its_backing_value_and_passes_its_guards_when_read()
            {
                Dictionary<InvoiceNumber, LineCount> lines = new() { [InvoiceNumber.From("INV-1")] = LineCount.From(3) };

                var json = Serializer().ToJson(lines);
                using var stored = new MemoryStream(Encoding.UTF8.GetBytes(json));
                using var blank = new MemoryStream(Encoding.UTF8.GetBytes("""{"  ":3}"""));

                json.ShouldBe("""{"INV-1":3}""");
                Serializer().FromJson<Dictionary<InvoiceNumber, LineCount>>(stored).ShouldBe(lines);
                Should.Throw<GuardException>(() => Serializer().FromJson<Dictionary<InvoiceNumber, LineCount>>(blank));
            }

            private static ISerializer Serializer()
            {
                var options = new StoreOptions();
                ModuleEventStore.Of(typeof(BillingAggregateStore).Assembly).ShouldNotBeNull().Configure(options, "Host=localhost", AutoCreate.None);
                return options.Serializer();
            }

            private static ServiceProvider Provider()
            {
                var services = new ServiceCollection().AddLogging();
                var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
                new BillingModule().AddServices(services, configuration);
                return services.BuildServiceProvider();
            }
        }
        """";

    [Fact]
    public void Value_objects_round_trip_through_marten_and_a_new_strict_module_passes_its_tests()
    {
        using var project = new GeneratedProject("industrial-event-driven-mfe", Choices);
        project.AddModule("billing");
        project.Build();

        var (untouchedModule, untouchedModuleOutput) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");
        untouchedModule.ShouldBe(0, untouchedModuleOutput);
        var (untouchedArchitecture, untouchedArchitectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        untouchedArchitecture.ShouldBe(0, untouchedArchitectureOutput);

        project.WriteTypes("src/Modules/Billing/Billing.Domain/Invoices", Invoices);
        project.Write("tests/Modules/Billing.Tests/EventStoreTests.cs", EventStoreTests);
        project.Build();

        var (architectureExitCode, architectureOutput) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
        architectureExitCode.ShouldBe(0, architectureOutput);

        using var postgres = Container.Postgres(DatabasePassword);
        var database = Database(postgres);
        var (runnerExitCode, runnerOutput) = project.DotNetCommand(database, "src/MigrationRunner/bin/Release/net10.0/MigrationRunner.dll");
        runnerExitCode.ShouldBe(0, runnerOutput);

        var (exitCode, output) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj", database);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        postgres.Psql(DatabasePassword, "select data->>'Number' from billing.mt_events order by seq_id limit 1").ShouldBe("INV-1");
    }

    [Fact]
    public void The_strict_rules_walk_from_event_sourced_aggregates_and_events()
    {
        using var project = new GeneratedProject("industrial-event-driven-mfe", Choices);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Domain/Invoices", Invoices);
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/InvoiceVoided.cs", """
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceVoided(InvoiceId Invoice, string Reason) : IDomainEvent;
            """);
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/InvoiceNoted.cs", """
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceNoted(InvoiceId Invoice) : IDomainEvent
            {
                public InvoiceNumber Number { get; init; } = default!;

                public IReadOnlyList<LineCount> Counts { get; init; } = [];
            }
            """);
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/Money.cs", """
            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record Money : IValueObject
            {
                private Money(decimal amount, string currency)
                {
                    Amount = amount;
                    Currency = currency;
                }

                public decimal Amount { get; }

                public string Currency { get; }

                public static Money From(decimal amount, string currency)
                {
                    Ensure.That(currency).IsNotNullOrWhiteSpace();
                    return new Money(amount, currency);
                }
            }
            """);
        project.Write("src/Modules/Billing/Billing.Domain/Invoices/Ledger.cs", """
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed class Ledger : EventSourcedAggregate<InvoiceId>
            {
                private Ledger()
                {
                }

                public string Note { get; set; } = "none";

                private void Apply(InvoiceVoided voided) => Id = voided.Invoice;

                private void Apply(InvoiceNoted noted) => Id = noted.Invoice;
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceVoided.Reason holds String");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Ledger.Note holds String");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Ledger.Note has a public setter.");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Money is a value object over more than one value");
        output.ShouldNotContain("Domain.Invoices.Invoice.");
        output.ShouldNotContain("InvoiceNumber is a value object over");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceNoted.Number carries a value object outside its constructor");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceNoted.Counts carries a value object outside its constructor");
        output.ShouldNotContain("InvoiceNoted.Invoice carries");
        output.ShouldNotContain("InvoiceIssued.Number carries");
        output.ShouldNotContain("InvoiceNumber.Value holds");
    }

    [Fact]
    public void An_event_whose_value_object_is_a_parameter_only_of_a_constructor_json_does_not_select_is_named()
    {
        // System.Text.Json prefers the public parameterless constructor, so {} leaves Number null without From.
        var (exitCode, output) = ArchitectureTestsWith("""
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceDrafted : IDomainEvent
            {
                public InvoiceDrafted()
                {
                }

                public InvoiceDrafted(InvoiceNumber number) => Number = number;

                public InvoiceNumber Number { get; init; } = default!;
            }

            public sealed class Draft : EventSourcedAggregate<InvoiceId>
            {
                private Draft()
                {
                }

                public InvoiceNumber Number { get; private set; } = default!;

                private void Apply(InvoiceDrafted drafted) => Number = drafted.Number;
            }
            """);

        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceDrafted.Number carries a value object outside its constructor");
        output.ShouldNotContain("InvoiceIssued.Number carries");
        exitCode.ShouldNotBe(0, output);
    }

    [Fact]
    public void An_event_whose_value_object_that_is_not_nullable_is_an_optional_parameter_is_named()
    {
        // RespectRequiredConstructorParameters lets an optional parameter be omitted, so {} would read Number as null.
        var (exitCode, output) = ArchitectureTestsWith("""
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceReissued(InvoiceId Invoice, InvoiceNumber Number = null!, InvoiceNumber? Previous = null) : IDomainEvent;

            public sealed class Reissue : EventSourcedAggregate<InvoiceId>
            {
                private Reissue()
                {
                }

                private void Apply(InvoiceReissued reissued) => Id = reissued.Invoice;
            }
            """);

        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceReissued.Number carries a value object through an optional constructor parameter");
        output.ShouldNotContain("InvoiceReissued.Previous");
        output.ShouldNotContain("InvoiceReissued.Invoice");
        exitCode.ShouldNotBe(0, output);
    }

    [Fact]
    public void A_value_object_a_nested_domain_type_holds_outside_its_constructor_is_named()
    {
        // The event takes Details by constructor, but "Details":{} still leaves Details.Number null without From.
        var (exitCode, output) = ArchitectureTestsWith("""
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceDetails
            {
                public InvoiceNumber Number { get; init; } = default!;
            }

            public sealed record InvoiceDetailed(InvoiceId Invoice, InvoiceDetails Details) : IDomainEvent;

            public sealed class Detail : EventSourcedAggregate<InvoiceId>
            {
                private Detail()
                {
                }

                private void Apply(InvoiceDetailed detailed) => Id = detailed.Invoice;
            }
            """);

        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceDetails.Number carries a value object outside its constructor");
        output.ShouldNotContain("InvoiceDetailed.Details carries");
        output.ShouldNotContain("InvoiceDetailed.Invoice carries");
        exitCode.ShouldNotBe(0, output);
    }

    [Fact]
    public void A_nested_domain_type_that_carries_a_value_object_is_held_and_checked_like_the_value_object_itself()
    {
        // An omitted Lines leaves the list empty, not refused; an omitted Count reads as its default null.
        var (exitCode, output) = ArchitectureTestsWith("""
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceLine(InvoiceNumber Number, LineCount Count = null!);

            public sealed record InvoiceItemized(InvoiceId Invoice) : IDomainEvent
            {
                public IReadOnlyList<InvoiceLine> Lines { get; init; } = [];
            }

            public sealed class Itemization : EventSourcedAggregate<InvoiceId>
            {
                private Itemization()
                {
                }

                private void Apply(InvoiceItemized itemized) => Id = itemized.Invoice;
            }
            """);

        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceItemized.Lines carries a value object outside its constructor");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceLine.Count carries a value object through an optional constructor parameter");
        output.ShouldNotContain("InvoiceLine.Number carries");
        exitCode.ShouldNotBe(0, output);
    }

    [Fact]
    public void The_walk_into_nested_domain_types_ends_on_types_that_refer_to_themselves()
    {
        var (exitCode, output) = ArchitectureTestsWith("""
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceChain(InvoiceNumber Number, InvoiceChain? Next, InvoiceTrail Trail);

            public sealed record InvoiceTrail
            {
                public InvoiceChain? Chain { get; init; }

                public LineCount Count { get; init; } = default!;
            }

            public sealed record InvoiceChained(InvoiceId Invoice, InvoiceChain Chain) : IDomainEvent;

            public sealed class Chaining : EventSourcedAggregate<InvoiceId>
            {
                private Chaining()
                {
                }

                private void Apply(InvoiceChained chained) => Id = chained.Invoice;
            }
            """);

        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceTrail.Chain carries a value object outside its constructor");
        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceTrail.Count carries a value object outside its constructor");
        output.ShouldNotContain("InvoiceChain.Next carries");
        output.ShouldNotContain("InvoiceChained.Chain carries");
        exitCode.ShouldNotBe(0, output);
    }

    [Fact]
    public void The_walk_does_not_follow_the_tag_of_a_generic_value_object_into_the_type_it_names()
    {
        // TypedId<Invoice> is stored as its Guid; the Invoice aggregate it names is never read from the event. Internal,
        // because a public generic type may not declare From (CA1000).
        var (exitCode, output) = ArchitectureTestsWith("""
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            internal sealed record TypedId<TTag> : IValueObject<Guid>
            {
                private TypedId(Guid value) => Value = value;

                public Guid Value { get; }

                public static TypedId<TTag> From(Guid value)
                {
                    Ensure.That(value).IsNotEmpty();
                    return new TypedId<TTag>(value);
                }
            }

            internal sealed record InvoiceTagged(InvoiceId Invoice, TypedId<Invoice> Tag) : IDomainEvent;

            internal sealed class Tagging : EventSourcedAggregate<InvoiceId>
            {
                private Tagging()
                {
                }

                private void Apply(InvoiceTagged tagged) => Id = tagged.Invoice;
            }
            """);

        output.ShouldNotContain("Invoices.Invoice.Number carries");
        output.ShouldNotContain("Invoices.Invoice.Lines carries");
        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void Each_closed_type_of_a_generic_domain_type_an_event_holds_is_checked()
    {
        // Envelope<Marker> carries no value object, Envelope<InvoiceNumber> does: {} leaves its Value null without From.
        var (exitCode, output) = ArchitectureTestsWith("""
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record Envelope<T>
            {
                public T Value { get; init; } = default!;
            }

            public sealed record Marker;

            public sealed record InvoiceEnveloped(InvoiceId Invoice, Envelope<Marker> First, Envelope<InvoiceNumber> Second) : IDomainEvent;

            public sealed class Enveloping : EventSourcedAggregate<InvoiceId>
            {
                private Enveloping()
                {
                }

                private void Apply(InvoiceEnveloped enveloped) => Id = enveloped.Invoice;
            }
            """);

        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Envelope`1[Shop.Modules.Billing.Domain.Invoices.InvoiceNumber].Value carries a value object outside its constructor");
        output.ShouldNotContain("Envelope`1[Shop.Modules.Billing.Domain.Invoices.Marker].Value carries");
        exitCode.ShouldNotBe(0, output);
    }

    [Fact]
    public void A_property_json_ignores_is_not_walked()
    {
        // Source is never written or read, so the Invoice aggregate it holds is not stored with the event.
        var (exitCode, output) = ArchitectureTestsWith("""
            using System.Text.Json.Serialization;
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record InvoiceSourced(InvoiceId Invoice) : IDomainEvent
            {
                [JsonIgnore]
                public Invoice? Source { get; init; }
            }

            public sealed class Sourcing : EventSourcedAggregate<InvoiceId>
            {
                private Sourcing()
                {
                }

                private void Apply(InvoiceSourced sourced) => Id = sourced.Invoice;
            }
            """);

        output.ShouldNotContain("InvoiceSourced.Source carries");
        output.ShouldNotContain("Invoices.Invoice.Number carries");
        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void A_domain_collection_is_checked_through_its_elements_not_as_an_object()
    {
        // System.Text.Json reads a collection as a JSON array, so its Item indexer is no constructor member.
        var (exitCode, output) = ArchitectureTestsWith("""
            using System.Collections.ObjectModel;
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed class InvoiceNumberCollection : Collection<InvoiceNumber>;

            public sealed record InvoiceEntry
            {
                public InvoiceNumber Number { get; init; } = default!;
            }

            public sealed class InvoiceEntryCollection : Collection<InvoiceEntry>;

            public sealed record InvoiceCollected(InvoiceId Invoice, InvoiceNumberCollection Numbers, InvoiceEntryCollection Entries) : IDomainEvent;

            public sealed class Collecting : EventSourcedAggregate<InvoiceId>
            {
                private Collecting()
                {
                }

                private void Apply(InvoiceCollected collected) => Id = collected.Invoice;
            }
            """);

        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.InvoiceEntry.Number carries a value object outside its constructor");
        output.ShouldNotContain("Collection.Item carries");
        output.ShouldNotContain("InvoiceCollected.Numbers carries");
        output.ShouldNotContain("InvoiceCollected.Entries carries");
        exitCode.ShouldNotBe(0, output);
    }

    [Fact]
    public void The_walk_does_not_follow_a_phantom_type_argument_of_a_domain_wrapper()
    {
        // Reference<Invoice> stores only its Id; the Invoice aggregate it names is never read from the event.
        var (exitCode, output) = ArchitectureTestsWith("""
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record Reference<TTag>(InvoiceId Id);

            public sealed record InvoiceReferenced(InvoiceId Invoice, Reference<Invoice> Source) : IDomainEvent;

            public sealed class Referencing : EventSourcedAggregate<InvoiceId>
            {
                private Referencing()
                {
                }

                private void Apply(InvoiceReferenced referenced) => Id = referenced.Invoice;
            }
            """);

        output.ShouldNotContain("Invoices.Invoice.Number carries");
        output.ShouldNotContain("Invoices.Invoice.Lines carries");
        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void The_walk_ends_on_a_collection_of_itself()
    {
        // The walk reads a collection through its elements, which here are the collection again.
        var (exitCode, output) = ArchitectureTestsWith("""
            using System.Collections.ObjectModel;
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed class RecursiveCollection : Collection<RecursiveCollection>;

            public sealed record InvoiceNested(InvoiceId Invoice, RecursiveCollection Nested) : IDomainEvent;

            public sealed class Nesting : EventSourcedAggregate<InvoiceId>
            {
                private Nesting()
                {
                }

                private void Apply(InvoiceNested nested) => Id = nested.Invoice;
            }
            """);

        output.ShouldNotContain("Stack overflow");
        output.ShouldNotContain("InvoiceNested.Nested carries");
        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public void A_member_a_recurring_generic_type_holds_through_its_substituted_argument_is_checked()
    {
        // Node<Marker> holds a Node<Envelope<Marker>>, whose Value carries an InvoiceNumber: {"Next":{}} leaves it null.
        var (exitCode, output) = ArchitectureTestsWith("""
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record Envelope<T>(InvoiceNumber Number);

            public sealed record Node<T>(Node<Envelope<T>>? Next)
            {
                public T Value { get; init; } = default!;
            }

            public sealed record Marker;

            public sealed record InvoiceLinked(InvoiceId Invoice, Node<Marker> Root) : IDomainEvent;

            public sealed class Linking : EventSourcedAggregate<InvoiceId>
            {
                private Linking()
                {
                }

                private void Apply(InvoiceLinked linked) => Id = linked.Invoice;
            }
            """);

        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Node`1[Shop.Modules.Billing.Domain.Invoices.Envelope`1[Shop.Modules.Billing.Domain.Invoices.Marker]].Value carries a value object outside its constructor");
        output.ShouldNotContain("Node`1[Shop.Modules.Billing.Domain.Invoices.Marker].Value carries");
        output.ShouldNotContain("InvoiceLinked.Root carries");
        exitCode.ShouldNotBe(0, output);
    }

    [Fact]
    public void A_larger_closed_type_of_a_generic_type_whose_graph_ends_is_checked()
    {
        // Box<Marker> holds the fixed Box<List<InvoiceNumber>>, which holds only itself: the graph ends, so it is walked.
        var (exitCode, output) = ArchitectureTestsWith("""
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed record Box<T>(Box<List<InvoiceNumber>>? Next)
            {
                public T Value { get; init; } = default!;
            }

            public sealed record Marker;

            public sealed record InvoiceBoxed(InvoiceId Invoice, Box<Marker> Box) : IDomainEvent;

            public sealed class Boxing : EventSourcedAggregate<InvoiceId>
            {
                private Boxing()
                {
                }

                private void Apply(InvoiceBoxed boxed) => Id = boxed.Invoice;
            }
            """);

        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Box`1[System.Collections.Generic.List`1[Shop.Modules.Billing.Domain.Invoices.InvoiceNumber]].Value carries a value object outside its constructor");
        output.ShouldNotContain("Box`1[Shop.Modules.Billing.Domain.Invoices.Marker].Value carries");
        output.ShouldNotContain("InvoiceBoxed.Box carries");
        exitCode.ShouldNotBe(0, output);
    }

    [Fact]
    public void A_member_a_recurring_generic_type_holds_through_a_collections_elements_is_checked()
    {
        // Growing<Marker> holds a Branch<Marker>, whose Inner holds a Branch<Envelope<Marker>>: its Value carries an
        // InvoiceNumber. A collection's own elements cannot grow it (the runtime refuses a recursive generic definition),
        // so the growth passes through a member.
        var (exitCode, output) = ArchitectureTestsWith("""
            using System.Collections.ObjectModel;
            using Shop.BuildingBlocks.EventSourcing;

            namespace Shop.Modules.Billing.Domain.Invoices;

            public sealed class Growing<T> : Collection<Branch<T>>;

            public sealed record Branch<T>(Growing<Envelope<T>> Inner)
            {
                public T Value { get; init; } = default!;
            }

            public sealed record Envelope<T>(InvoiceNumber Number);

            public sealed record Marker;

            public sealed record InvoiceGrown(InvoiceId Invoice, Growing<Marker> Grown) : IDomainEvent;

            public sealed class Growth : EventSourcedAggregate<InvoiceId>
            {
                private Growth()
                {
                }

                private void Apply(InvoiceGrown grown) => Id = grown.Invoice;
            }
            """);

        output.ShouldContain("Shop.Modules.Billing.Domain.Invoices.Branch`1[Shop.Modules.Billing.Domain.Invoices.Envelope`1[Shop.Modules.Billing.Domain.Invoices.Marker]].Value carries a value object outside its constructor");
        output.ShouldNotContain("Branch`1[Shop.Modules.Billing.Domain.Invoices.Marker].Value carries");
        output.ShouldNotContain("InvoiceGrown.Grown carries");
        exitCode.ShouldNotBe(0, output);
    }

    private static (int ExitCode, string Output) ArchitectureTestsWith(string types)
    {
        using var project = new GeneratedProject("industrial-event-driven-mfe", Choices);
        project.AddModule("billing");
        project.WriteTypes("src/Modules/Billing/Billing.Domain/Invoices", Invoices);
        project.WriteTypes("src/Modules/Billing/Billing.Domain/Invoices", types);
        project.Build();

        return project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");
    }

    private static Dictionary<string, string> Database(Container postgres) => new()
    {
        ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
    };
}
