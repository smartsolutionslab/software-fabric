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

    /// <summary>C#'s reserved keywords, which cannot name a namespace without an <c>@</c>.</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "volatile", "while",
    };

    public static bool IsValid(string name) => Pattern().IsMatch(name);

    /// <summary>Why <paramref name="name"/> cannot name a module because of its length, or <c>null</c> when it can.</summary>
    public static string? ModuleLengthProblem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.Length <= MaxModuleLength
            ? null
            : $"is {name.Length} characters; a module's name is its PostgreSQL schema name, and PostgreSQL identifiers are at most {MaxModuleLength} characters.";
    }

    /// <summary>
    /// Why <paramref name="rootNamespace"/> cannot be a project's root namespace, or <c>null</c> when it can: dotted C#
    /// names, ASCII only, none a keyword, and not under <c>System</c> or <c>Microsoft</c>, which a generated project keeps
    /// for .NET's own types and for its wiring code.
    /// </summary>
    public static string? NamespaceProblem(string rootNamespace)
    {
        ArgumentNullException.ThrowIfNull(rootNamespace);

        var parts = rootNamespace.Split('.');
        if (!parts.All(part => NamespacePart().IsMatch(part) && !Keywords.Contains(part)))
        {
            return $"namespace '{rootNamespace}' must be a C# namespace: names of ASCII letters, digits and underscores, each starting with a " +
                "letter and none a C# keyword, separated by dots.";
        }

        return parts[0] is "System" or "Microsoft"
            ? $"namespace '{rootNamespace}' is under System or Microsoft, which a generated project keeps for .NET's own types and its wiring code."
            : null;
    }

    /// <summary><c>smart-shop-2</c> becomes <c>SmartShop2</c>.</summary>
    public static string ToPascalCase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return string.Concat(name.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex Pattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex NamespacePart();
}
