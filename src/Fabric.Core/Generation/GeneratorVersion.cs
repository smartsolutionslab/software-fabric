using System.Reflection;

namespace Fabric.Core.Generation;

/// <summary>
/// The generator's SemVer, from its assembly's informational version (<c>Version</c> in Directory.Build.props). It
/// carries no git SHA, so every build of the same version writes the same <c>fabric.yaml</c>.
/// </summary>
internal static class GeneratorVersion
{
    public static string Current { get; } =
        typeof(GeneratorVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
}
