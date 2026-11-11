using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// ASP.NET Core 11 serves OpenAPI 3.2 by default, which the RTK Query codegen's parser refuses. Owner's decision on #550
/// (option A): the host pins its document to OpenAPI 3.1, which Microsoft.OpenApi writes as 3.1.2, and every API client
/// starts from a copy of the document at that version, so a fresh project's OpenApiDocumentTests pass.
/// </summary>
[Collection(FixedPorts.Name)]
public sealed class OpenApiDocumentVersionTests
{
    private const string Version = "3.1.2";
    private const string DatabasePassword = "fabric-tests";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Each client, with the host's /me (and, with tenancy, /me/tenant) and without any path.</summary>
    public static TheoryData<string, string[]> Clients => new()
    {
        { "lean-crud", ["frontend.api-client=orval"] },
        { "lean-crud", ["frontend.api-client=orval", "backend.auth=none"] },
        { "lean-crud", ["frontend.api-client=openapi-fetch"] },
        { "lean-crud", ["frontend.api-client=openapi-fetch", "backend.auth=none"] },
        { "saas-standard-react", ["frontend.api-client=rtk-query-codegen"] },
        { "saas-standard-react", ["frontend.api-client=rtk-query-codegen", "backend.auth=none", "backend.tenancy=none"] },
    };

    [Theory]
    [MemberData(nameof(Clients))]
    public void Every_api_clients_copy_of_the_document_is_OpenAPI_3_1_2(string profile, string[] choices)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, choices);
        generated.Errors.ShouldBeEmpty();

        var document = generated.Files.Single(file => file.Path == "web/app/openapi.json").Content;

        ((string?)JsonNode.Parse(document)!["openapi"]).ShouldBe(Version);
    }

    [Fact]
    public async Task A_fresh_hosts_document_is_the_frontends_copy()
    {
        using var project = new GeneratedProject("lean-crud", "backend.auth=none");
        project.Build();

        using var postgres = Container.Postgres(DatabasePassword);
        using var host = project.StartHost(new Dictionary<string, string>
        {
            ["ConnectionStrings__database"] = $"Host=127.0.0.1;Port={postgres.Port};Database=postgres;Username=postgres;Password={DatabasePassword}",
        });

        using var response = await host.GetAsync("/openapi/v1.json");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var served = JsonNode.Parse(body)!.AsObject();
        served.Remove("servers");

        (served.ToJsonString(Indented) + "\n").ShouldBe(File.ReadAllText(Path.Combine(project.ProjectDirectory, "web", "app", "openapi.json")).ReplaceLineEndings("\n"));
    }
}
