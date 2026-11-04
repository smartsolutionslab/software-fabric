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
