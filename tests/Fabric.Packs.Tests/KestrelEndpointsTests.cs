using System.Text.Json;
using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>
/// The modular monolith's host settings declare <c>host.kestrel-endpoints</c>: a pack that needs another endpoint of
/// the host (an HTTP/2-only one for gRPC) contributes it under <c>Kestrel:Endpoints</c>, beside the host's own
/// <c>http</c> endpoint, and Aspire turns each into an endpoint of the host resource. Without a contribution the
/// settings have no Kestrel section, so the host keeps listening where launchSettings and Aspire say.
/// </summary>
public sealed class KestrelEndpointsTests
{
    private const string Profile = "lean-crud";

    private const string Settings = "src/Host/appsettings.json";

    [Fact]
    public void An_endpoint_another_pack_contributes_is_a_kestrel_endpoint_beside_the_hosts_own_http_endpoint()
    {
        using var probe = new ProbePack("frontend.ui-library.mantine", new Dictionary<string, string>
        {
            ["host.kestrel-endpoints"] = """
                      "probe": {
                        "Url": "http://localhost:5099",
                        "Protocols": "Http2"
                      }
                """,
        });
        var generated = Generator.New(Repository.Catalog, Packs(probe), "shop", Profile, ["frontend.ui-library=mantine"]);
        generated.Errors.ShouldBeEmpty();

        using var settings = JsonDocument.Parse(generated.Files.Single(file => file.Path == Settings).Content);
        var endpoints = settings.RootElement.GetProperty("Kestrel").GetProperty("Endpoints");

        endpoints.EnumerateObject().Select(endpoint => endpoint.Name).ShouldBe(["http", "probe"]);
        endpoints.GetProperty("http").GetProperty("Url").GetString().ShouldBe("http://localhost:5080");
        endpoints.GetProperty("probe").GetProperty("Protocols").GetString().ShouldBe("Http2");
        settings.RootElement.GetProperty("Logging").GetProperty("LogLevel").GetProperty("Default").GetString().ShouldBe("Information");
    }

    [Fact]
    public void Without_a_contribution_the_host_settings_have_no_kestrel_section()
    {
        var generated = Generator.New(Repository.Catalog, Repository.Packs.Packs, "shop", Profile, []);
        generated.Errors.ShouldBeEmpty();

        using var settings = JsonDocument.Parse(generated.Files.Single(file => file.Path == Settings).Content);

        settings.RootElement.TryGetProperty("Kestrel", out _).ShouldBeFalse();
    }

    private static Dictionary<string, Core.Packs.PackManifest> Packs(ProbePack probe)
    {
        var packs = Repository.Packs.Packs.ToDictionary();
        packs[probe.Manifest.Option] = probe.Manifest;
        return packs;
    }
}
