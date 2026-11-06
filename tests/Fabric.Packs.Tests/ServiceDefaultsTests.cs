using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>
/// The base's ServiceDefaults: every HttpClient retries only idempotent methods unless a client opts back in, and the
/// standard resilience handler is registered there once, for every client. The tracer listens to the application's own
/// source and to the sources other packs name through <c>service-defaults.activity-sources</c>.
/// </summary>
public sealed partial class ServiceDefaultsTests
{
    private const string Profile = "saas-standard-react";

    private const string ArchitectureTests = "tests/Architecture.Tests/Architecture.Tests.csproj";

    private const string TracedSourceTests = """
        using System.Diagnostics;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using OpenTelemetry;
        using OpenTelemetry.Trace;

        namespace Shop.Architecture.Tests.Probes;

        public sealed class TracedSourceTests
        {
            [Fact]
            public void The_service_defaults_trace_a_source_another_pack_names_and_not_one_nobody_names()
            {
                var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(settings: null);
                builder.AddServiceDefaults();
                List<string> ended = [];
                builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(new EndedSpans(ended)));
                using var provider = builder.Services.BuildServiceProvider();
                provider.GetRequiredService<TracerProvider>();

                foreach (var name in new[] { "Shop.Probe", "Shop.Unnamed" })
                {
                    using var source = new ActivitySource(name);
                    using var span = source.StartActivity("probe");
                }

                ended.ShouldBe(["Shop.Probe"]);
            }
        }

        internal sealed class EndedSpans(List<string> sources) : BaseProcessor<Activity>
        {
            public override void OnEnd(Activity data) => sources.Add(data.Source.Name);
        }
        """;

    [Fact]
    public void A_default_http_client_retries_a_get_but_not_a_post_and_a_client_can_opt_back_in()
    {
        using var project = new GeneratedProject(Profile);
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        File.Exists(Path.Combine(project.ProjectDirectory, "tests", "Architecture.Tests", "Rules", "StandardResilienceHandlerOnlyInServiceDefaults.cs")).ShouldBeTrue();
    }

    [Fact]
    public void A_second_resilience_handler_in_a_module_fails_the_architecture_tests_naming_it()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Infrastructure/Billing.Infrastructure.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
              </ItemGroup>
            </Project>
            """);
        project.Write("src/Modules/Billing/Billing.Infrastructure/BillingHttpClients.cs", """"
            using Microsoft.Extensions.DependencyInjection;

            namespace Shop.Modules.Billing.Infrastructure;

            public static class BillingHttpClients
            {
                /* A block comment is prose, not a registration:
                services.AddHttpClient("prose").AddStandardResilienceHandler();
                */
                public const string Example = """
                    services.AddHttpClient("quoted").AddStandardResilienceHandler();
                    """;

                // AddStandardResilienceHandler() in a line comment is prose too.
                public static IServiceCollection AddBillingHttpClients(this IServiceCollection services)
                {
                    services.AddHttpClient("payments").AddStandardResilienceHandler /* nested */ ();
                    services.AddHttpClient("https://ledger.test").AddStandardResilienceHandler();
            #if NET10_0_OR_GREATER
                    services.AddHttpClient("compiled-branch").AddStandardResilienceHandler();
            #endif
            #if DEBUG
                    services.AddHttpClient("debug-branch").AddStandardResilienceHandler();
            #endif
                    return services;
                }
            }
            """");
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("src/Modules/Billing/Billing.Infrastructure/BillingHttpClients.cs:17: calls AddStandardResilienceHandler outside src/ServiceDefaults");
        output.ShouldContain("src/Modules/Billing/Billing.Infrastructure/BillingHttpClients.cs:18: calls AddStandardResilienceHandler outside src/ServiceDefaults");
        output.ShouldContain("src/Modules/Billing/Billing.Infrastructure/BillingHttpClients.cs:20: calls AddStandardResilienceHandler outside src/ServiceDefaults");
        output.ShouldContain("src/Modules/Billing/Billing.Infrastructure/BillingHttpClients.cs:23: calls AddStandardResilienceHandler outside src/ServiceDefaults");
        output.ShouldNotContain("BillingHttpClients.cs:8:");
        output.ShouldNotContain("BillingHttpClients.cs:11:");
        output.ShouldNotContain("BillingHttpClients.cs:14:");
    }

    [Fact]
    public void A_source_another_pack_names_through_the_slot_is_traced_by_the_service_defaults()
    {
        // contract-tests has no pack yet; the probe takes its place and fills the slot.
        using var probe = new ProbePack("backend.test.extras.contract-tests", new Dictionary<string, string>
        {
            ["service-defaults.activity-sources"] = "\"Shop.Probe\"",
        });
        using var project = new GeneratedProject(Profile, [probe.Manifest], "backend.test.extras=contract-tests");
        project.WriteTypes("tests/Architecture.Tests/Probes", TracedSourceTests);
        project.Build();

        var (exitCode, output) = project.DotNetCommand(
            new Dictionary<string, string>(),
            "test", "--project", ArchitectureTests, "-c", "Release", "--no-build", "--no-ansi",
            "--", "--filter-class", "Shop.Architecture.Tests.Probes.TracedSourceTests");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("succeeded: 1", customMessage: output);
        output.ShouldContain("failed: 0", customMessage: output);
    }

    [Fact]
    public void The_retry_rule_fails_a_project_whose_defaults_retry_every_method()
    {
        using var project = new GeneratedProject(Profile);
        var extensions = Path.Combine(project.ProjectDirectory, "src", "ServiceDefaults", "Wiring", "Extensions.cs");
        var retryingEveryMethod = StandardResilienceHandler().Replace(File.ReadAllText(extensions), "AddStandardResilienceHandler()");
        project.Write("src/ServiceDefaults/Wiring/Extensions.cs", retryingEveryMethod);
        project.Build();

        var (exitCode, output) = project.Test(ArchitectureTests);

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("A POST that failed with ServiceUnavailable was attempted 4 times by a default HttpClient; expected 1.");
    }

    [GeneratedRegex(@"AddStandardResilienceHandler\([^)]*\)")]
    private static partial Regex StandardResilienceHandler();
}
