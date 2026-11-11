using System.Text.Json.Nodes;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// The host's document is OpenAPI 3.1.2 (#550). @rtk-query/codegen-openapi 2.2.0, its newest release, asks for
/// @apidevtools/swagger-parser ^10.1.1, which reads 3.1.0 and 3.1.1 only; 12.1.0 reads 3.1.2. Owner's decision on #550
/// (option A): rtk-query-codegen overrides it to 12.1.0 at the workspace root, through web.pnpm-overrides (#569).
/// </summary>
public sealed class RtkQueryCodegenOpenApiTests
{
    private const string SwaggerParser = "@apidevtools/swagger-parser";

    public static TheoryData<string> RtkQueryProfiles => [.. Repository.ProfilesChoosing("frontend.api-client", "rtk-query-codegen")];

    public static TheoryData<string, string> OtherClients => new()
    {
        { "lean-crud", "frontend.api-client=orval" },
        { "lean-crud", "frontend.api-client=openapi-fetch" },
    };

    [Theory]
    [MemberData(nameof(RtkQueryProfiles))]
    public void The_workspace_root_overrides_swagger_parser_to_the_release_that_reads_OpenAPI_3_1_2(string profile)
    {
        var root = RootPackage(profile);

        ((string?)root["pnpm"]?["overrides"]?[SwaggerParser]).ShouldBe("12.1.0");
    }

    [Theory]
    [MemberData(nameof(OtherClients))]
    public void Another_api_client_overrides_nothing(string profile, string client)
    {
        RootPackage(profile, client)["pnpm"].ShouldBeNull();
    }

    [Fact]
    public void The_codegen_reads_the_document_the_host_serves()
    {
        using var project = new GeneratedProject("saas-standard-react");
        var web = Path.Combine(project.ProjectDirectory, "web");

        var (installExitCode, install) = Shell.Run(web, "pnpm", "install");
        installExitCode.ShouldBe(0, $"pnpm install:\n{install}");

        var (exitCode, output) = Shell.Run(Path.Combine(web, "app"), "pnpm", "codegen");
        exitCode.ShouldBe(0, $"pnpm codegen:\n{output}");
    }

    private static JsonNode RootPackage(string profile, params string[] overrides)
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", profile, overrides);
        generated.Errors.ShouldBeEmpty();
        return JsonNode.Parse(generated.Files.Single(file => file.Path == "web/package.json").Content)!;
    }
}
