using Fabric.Core.Generation;

namespace Fabric.Packs.Tests;

/// <summary>Generates saas-standard-react from the real packs (stand-ins for the rest) and builds the result.</summary>
public sealed class SaasStandardReactTests : IDisposable
{
    private const string Profile = "saas-standard-react";

    private readonly string root = Directory.CreateTempSubdirectory("fabric-saas-").FullName;
    private readonly StandIns standIns = new();

    public void Dispose()
    {
        standIns.Dispose();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void The_repository_packs_load_without_errors()
    {
        Repository.Packs.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generates_builds_and_still_builds_after_adding_a_module()
    {
        var packs = standIns.Complete(Repository.Catalog, Repository.Packs.Packs, Profile);
        var project = Path.Combine(root, "shop");

        var generated = Generator.New(Repository.Catalog, packs, "shop", Profile, []);
        generated.Errors.ShouldBeEmpty();
        ProjectWriter.Write(project, generated.Files).ShouldBeNull();

        Build(project);

        var record = FabricFile.Read(new StringReader(File.ReadAllText(Path.Combine(project, FabricFile.FileName))), FabricFile.FileName);
        var addition = Generator.AddModule(Repository.Catalog, packs, record, "billing", []);
        addition.Errors.ShouldBeEmpty();
        ProjectWriter.Add(project, addition.Created, addition.FabricYaml!).ShouldBeNull();

        Build(project);
        File.Exists(Path.Combine(project, "src", "Modules", "Billing", "Billing.Api", "bin", "Release", "net10.0", "Shop.Modules.Billing.Api.dll")).ShouldBeTrue();
    }

    private static void Build(string project)
    {
        var (exitCode, output) = DotNet.Run(project, "build", "build.proj", "-c", "Release");
        exitCode.ShouldBe(0, output);
    }
}
