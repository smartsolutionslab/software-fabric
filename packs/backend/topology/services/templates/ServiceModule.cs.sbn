using System.Reflection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// The module a service host runs, by its route name (ADR-0017): the name its endpoints are grouped under and the edge
/// routes /&lt;module&gt;/... by. The service host's project writes it into the assembly from its &lt;ModuleName&gt;, so
/// it needs no configuration and no line per module. Only a service host has one; the edge, which compiles src/Host
/// too, runs no module.
/// </summary>
public static class ServiceModule
{
    private const string Key = "ModuleName";

    public static string Name =>
        typeof(ServiceModule).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(metadata => metadata.Key == Key)?.Value
            ?? throw new InvalidOperationException($"{typeof(ServiceModule).Assembly.GetName().Name} carries no {Key} metadata: only a service host runs a module.");
}
