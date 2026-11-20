using System.Text.RegularExpressions;

namespace Fabric.Core.Generation;

/// <summary>
/// Project and module names become directory names, namespaces and queue names, so they are kept to lower
/// kebab-case; <see cref="ToPascalCase"/> gives the C# namespace form.
/// </summary>
public static partial class Names
{
    /// <summary>PostgreSQL's identifier limit (NAMEDATALEN - 1): a module's name is its schema name.</summary>
    public const int MaxModuleLength = 63;

    public static bool IsValid(string name) => Pattern().IsMatch(name);

    /// <summary>Why <paramref name="name"/> cannot name a module because of its length, or <c>null</c> when it can.</summary>
    public static string? ModuleLengthProblem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.Length <= MaxModuleLength
            ? null
            : $"is {name.Length} characters; a module's name is its PostgreSQL schema name, and PostgreSQL identifiers are at most {MaxModuleLength} characters.";
    }

    /// <summary><c>smart-shop-2</c> becomes <c>SmartShop2</c>.</summary>
    public static string ToPascalCase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return string.Concat(name.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex Pattern();
}
