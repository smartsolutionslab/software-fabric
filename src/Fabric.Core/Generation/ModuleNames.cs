using System.Text.RegularExpressions;

namespace Fabric.Core.Generation;

/// <summary>Module names become directory names, namespaces and queue names, so they are kept to lower kebab-case.</summary>
public static partial class ModuleNames
{
    public static bool IsValid(string name) => Pattern().IsMatch(name);

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex Pattern();
}
