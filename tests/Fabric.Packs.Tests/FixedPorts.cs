namespace Fabric.Packs.Tests;

/// <summary>Tests that start a generated AppHost; they share fixed ports (Keycloak 8080, the web app 5173), so they never run at once.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FixedPorts
{
    public const string Name = "Fixed ports";
}
