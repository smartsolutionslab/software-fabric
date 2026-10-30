using System.Net;

namespace Fabric.Packs.Tests;

/// <summary>Generates saas-standard-react from the real packs (stand-ins for the rest) and checks the result.</summary>
public sealed class SaasStandardReactTests
{
    private const string Profile = "saas-standard-react";

    /// <summary>What a module's endpoint looks like with minimal-apis and result; the module packs (M1-04) will add real ones.</summary>
    private const string ProbeEndpoint = """
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Routing;
        using Shop.BuildingBlocks.Api;
        using Shop.BuildingBlocks.Results;

        namespace Shop.Modules.Billing.Api;

        public sealed class Probe : IEndpoint
        {
            public void Map(IEndpointRouteBuilder endpoints) =>
                endpoints.MapGet("/probe", () => Result<string, ApiError>.Success("pong").ToHttpResult());
        }
        """;

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

        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = "Host=127.0.0.1;Port=1;Database=shop;Username=none;Password=none",
            ["Authentication__Authority"] = "http://127.0.0.1:1/realms/shop",
        });

        (await host.GetAsync("/alive")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.GetAsync("/openapi/v1.json")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.GetAsync("/billing/probe")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
