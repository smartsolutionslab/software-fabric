[assembly: AssemblyFixture(typeof(Fabric.Packs.Tests.DotNetTestEnvironment))]

namespace Fabric.Packs.Tests;

/// <summary>
/// <c>dotnet test</c> starts this assembly with <c>MSBuildSDKsPath</c> and <c>MSBuildExtensionsPath</c> pointing at the
/// SDK that runs the tests, and every process a test starts inherits them. A generated project's global.json can pin
/// another SDK, whose MSBuild would then load this SDK's targets and fail; so they are removed before any test runs, and
/// each <c>dotnet</c> a test starts finds its SDK from its own global.json.
/// </summary>
public sealed class DotNetTestEnvironment
{
    public DotNetTestEnvironment()
    {
        Environment.SetEnvironmentVariable("MSBuildSDKsPath", null);
        Environment.SetEnvironmentVariable("MSBuildExtensionsPath", null);
    }
}
