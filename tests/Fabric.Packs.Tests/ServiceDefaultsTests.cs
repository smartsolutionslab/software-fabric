using System.Text.RegularExpressions;

namespace Fabric.Packs.Tests;

/// <summary>
/// The base's ServiceDefaults: every HttpClient retries only idempotent methods unless a client opts back in, and the
/// standard resilience handler is registered there once, for every client.
/// </summary>
public sealed partial class ServiceDefaultsTests
{
    private const string Profile = "saas-standard-react";

    private const string ArchitectureTests = "tests/Architecture.Tests/Architecture.Tests.csproj";

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
