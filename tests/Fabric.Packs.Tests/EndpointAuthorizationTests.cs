namespace Fabric.Packs.Tests;

/// <summary>
/// With Keycloak, every endpoint of the host declares its authorization: it requires it, or it is anonymous through an
/// explicit <c>.AllowAnonymous()</c> where it is mapped. Module routes inherit it from their group; anything mapped
/// beside the group has to say so itself.
/// </summary>
public sealed class EndpointAuthorizationTests
{
    private const string Profile = "saas-standard-react";

    [Fact]
    public void An_endpoint_without_authorization_fails_the_architecture_tests_naming_it()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/BillingModule.cs", BillingModule("endpoints.MapGet(\"/billing-open\", () => \"open\");"));
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldNotBe(0, output);
        output.ShouldContain("GET /billing-open is mapped with neither RequireAuthorization() nor AllowAnonymous().");
    }

    [Fact]
    public void An_endpoint_marked_anonymous_where_it_is_mapped_passes()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Write("src/Modules/Billing/Billing.Api/BillingModule.cs", BillingModule("endpoints.MapGet(\"/billing-open\", () => \"open\").AllowAnonymous();"));
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
    }

    [Fact]
    public void The_untouched_project_declares_the_authorization_of_every_endpoint()
    {
        using var project = new GeneratedProject(Profile);
        project.AddModule("billing");
        project.Build();

        var (exitCode, output) = project.Test("tests/Architecture.Tests/Architecture.Tests.csproj");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("failed: 0");
        File.Exists(Path.Combine(project.ProjectDirectory, "tests", "Architecture.Tests", "Rules", "EveryEndpointDeclaresItsAuthorization.cs")).ShouldBeTrue();
    }

    private static string BillingModule(string extraMapping) => $$"""
        using Shop.BuildingBlocks.Modules;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;

        namespace Shop.Modules.Billing.Api;

        public sealed class BillingModule : IModule
        {
            public void AddServices(IServiceCollection services, IConfiguration configuration) =>
                services.AddModuleInfrastructure<BillingModule>(configuration);

            public void MapEndpoints(IEndpointRouteBuilder endpoints)
            {
                endpoints.MapGroup("/billing").MapModuleEndpoints<BillingModule>();
                {{extraMapping}}
            }
        }
        """;
}
