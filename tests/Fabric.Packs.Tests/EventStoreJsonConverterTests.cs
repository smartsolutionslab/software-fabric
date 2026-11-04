namespace Fabric.Packs.Tests;

/// <summary>The event-store.json-converters slot of module.event-store.marten.</summary>
public sealed class EventStoreJsonConverterTests
{
    private const string Profile = "industrial-event-driven-mfe";

    private static readonly string[] Frontend = ["frontend.state=redux-toolkit", "frontend.api-client=rtk-query-codegen"];

    [Fact]
    public void A_converter_another_pack_contributes_serialises_every_modules_events_and_documents()
    {
        // The realtime option has no pack yet; the probe takes its place and fills the slot.
        using var probe = new ProbePack("frontend.realtime.signalr", new Dictionary<string, string>
        {
            ["event-store.json-converters"] = "            json.Converters.Add(new Shop.BuildingBlocks.EventStore.ProbeConverter());",
        });
        using var project = new GeneratedProject(Profile, [probe.Manifest], Frontend);
        project.AddModule("billing");
        project.Write("src/BuildingBlocks/EventStore/ProbeValue.cs", """
            namespace Shop.BuildingBlocks.EventStore;

            public sealed record ProbeValue(string Text);
            """);
        project.Write("src/BuildingBlocks/EventStore/ProbeConverter.cs", """
            using System.Text.Json;
            using System.Text.Json.Serialization;

            namespace Shop.BuildingBlocks.EventStore;

            public sealed class ProbeConverter : JsonConverter<ProbeValue>
            {
                public override ProbeValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                    new(reader.GetString()!["probe:".Length..]);

                public override void Write(Utf8JsonWriter writer, ProbeValue value, JsonSerializerOptions options)
                {
                    Ensure.That(writer).IsNotNull();
                    Ensure.That(value).IsNotNull();
                    writer.WriteStringValue("probe:" + value.Text);
                }
            }
            """);
        project.Write("tests/Modules/Billing.Tests/JsonConverterTests.cs", """
            using JasperFx;
            using Marten;
            using Shop.BuildingBlocks.EventStore;
            using Shop.Modules.Billing.Infrastructure.EventStore;

            namespace Shop.Modules.Billing.Tests;

            public class JsonConverterTests
            {
                [Fact]
                public void The_event_store_serialises_with_the_contributed_converter()
                {
                    var options = new StoreOptions();
                    ModuleEventStore.Of(typeof(BillingAggregateStore).Assembly).ShouldNotBeNull().Configure(options, "Host=localhost", AutoCreate.None);

                    options.Serializer().ToJson(new ProbeValue("ada")).ShouldBe("\"probe:ada\"");
                }
            }
            """);
        project.Build();

        var (exitCode, output) = project.Test("tests/Modules/Billing.Tests/Billing.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void Without_a_converter_the_event_store_keeps_its_plain_serializer_configuration()
    {
        using var project = new GeneratedProject(Profile, Frontend);

        File.ReadAllText(Path.Combine(project.ProjectDirectory, "src", "BuildingBlocks", "EventStore", "ModuleEventStore.cs"))
            .ShouldContain("        options.UseSystemTextJsonForSerialization();\n");
    }
}
