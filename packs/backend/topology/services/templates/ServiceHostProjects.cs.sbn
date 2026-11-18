using System.Xml.Linq;

namespace Aspire.Hosting;

/// <summary>
/// Every module's service host, found by convention (src/Services/*/Host/Host.csproj) so that adding a module only
/// creates files. Each is named after its module, as its project file records it.
/// </summary>
public static class ServiceHostProjects
{
    public static IEnumerable<(string Module, string ProjectPath)> ServiceHosts(this IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "Services"));
        if (!Directory.Exists(services))
        {
            return [];
        }

        return Directory.EnumerateDirectories(services)
            .Select(service => Path.Combine(service, "Host", "Host.csproj"))
            .Where(File.Exists)
            .Order(StringComparer.Ordinal)
            .Select(path => (ModuleName(path), path));
    }

    private static string ModuleName(string projectPath) =>
        XDocument.Load(projectPath).Descendants("ModuleName").SingleOrDefault()?.Value
            ?? throw new InvalidOperationException($"{projectPath} does not name its module (<ModuleName>).");
}
