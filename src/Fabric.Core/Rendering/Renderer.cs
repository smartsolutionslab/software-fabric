using System.Text.RegularExpressions;
using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Generation;
using Fabric.Core.Packs;
using Scriban;
using Scriban.Functions;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Fabric.Core.Rendering;

/// <summary>
/// Renders a composition with Scriban. Templates see <c>name</c>, <c>namespace</c>, <c>project</c>,
/// <c>module_defaults</c>, <c>modules</c>, <c>module</c> (per-module files only) and <c>slots</c> (files only). Nothing from the clock or the machine is
/// available, unknown variables are errors, and output uses LF, so the same input always renders the same bytes.
/// </summary>
public static partial class Renderer
{
    public static RenderedProject Render(CatalogModel catalog, Composition composition, ProjectChoices choices, string name)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(name);

        List<string> errors = [];
        var model = Model(catalog, choices, name);
        var functions = Functions(composition);

        var slots = new ScriptObject();
        foreach (var (slot, entries) in composition.Slots.Where(slot => slot.Key != Composer.TemplateFunctionsSlot))
        {
            var parts = entries.Select(entry => RenderTemplate(entry.Pack, entry.Contribution.Template, functions, errors, model));
            var separator = composition.SlotSeparators?.GetValueOrDefault(slot) ?? "\n";
            slots.SetValue(slot, string.Join(separator, parts.OfType<string>()), readOnly: true);
        }

        List<RenderedFile> files = [];
        foreach (var file in composition.Files)
        {
            var scope = new ScriptObject { ["slots"] = slots };
            if (file.Module is { } module)
            {
                scope["module"] = Module(catalog, module, choices.Modules[module]);
            }

            if (RenderTemplate(file.Pack, file.File.Template, functions, errors, model, scope) is { } content)
            {
                files.Add(new RenderedFile(file.Target, content));
            }
        }

        return errors.Count > 0
            ? new RenderedProject([], errors)
            : new RenderedProject([.. files.OrderBy(file => file.Path, StringComparer.Ordinal)], []);
    }

    /// <summary>The Scriban code packs contribute to <see cref="Composer.TemplateFunctionsSlot"/>, as a prefix that outputs nothing.</summary>
    private static string Functions(Composition composition) =>
        composition.Slots.TryGetValue(Composer.TemplateFunctionsSlot, out var entries) && entries.Count > 0
            ? "{{~ " + string.Join('\n', entries.Select(entry => File.ReadAllText(Path.Combine(entry.Pack.Directory, entry.Contribution.Template)))) + " }}"
            : string.Empty;

    private static string? RenderTemplate(PackManifest pack, string templatePath, string functions, List<string> errors, params ScriptObject[] globals)
    {
        var label = $"{pack.Option}: {templatePath}";
        var template = Template.Parse(functions + File.ReadAllText(Path.Combine(pack.Directory, templatePath)), templatePath);
        if (template.HasErrors)
        {
            errors.Add($"{label}: {string.Join("; ", template.Messages)}");
            return null;
        }

        var context = new TemplateContext(Builtins()) { StrictVariables = true };
        foreach (var global in globals)
        {
            context.PushGlobal(global);
        }

        try
        {
            return Tidy(template.Render(context).Replace("\r\n", "\n", StringComparison.Ordinal));
        }
        catch (ScriptRuntimeException exception)
        {
            errors.Add($"{label}: {exception.Message}");
            return null;
        }
    }

    /// <summary>An empty slot leaves its line behind; whitespace-only lines become empty and blank-line runs collapse to one.</summary>
    private static string Tidy(string content) =>
        BlankLineRun().Replace(WhitespaceOnlyLine().Replace(content, string.Empty), "\n\n");

    [GeneratedRegex(@"(?m)^[ \t]+$")]
    private static partial Regex WhitespaceOnlyLine();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLineRun();

    /// <summary>Scriban's built-ins minus <c>date</c>, <c>timespan</c> and <c>math</c>, which can read the clock or a random source.</summary>
    private static ScriptObject Builtins()
    {
        var builtins = new ScriptObject();
        builtins.SetValue("array", new ArrayFunctions(), readOnly: true);
        builtins.SetValue("html", new HtmlFunctions(), readOnly: true);
        builtins.SetValue("object", new ObjectFunctions(), readOnly: true);
        builtins.SetValue("regex", new RegexFunctions(), readOnly: true);
        builtins.SetValue("string", new StringFunctions(), readOnly: true);
        return builtins;
    }

    private static ScriptObject Model(CatalogModel catalog, ProjectChoices choices, string name)
    {
        var modules = new ScriptArray();
        foreach (var (moduleName, values) in choices.Modules)
        {
            modules.Add(Module(catalog, moduleName, values));
        }

        return new ScriptObject
        {
            ["name"] = name,
            ["namespace"] = Names.ToPascalCase(name),
            ["project"] = Values(catalog, choices.Project),
            ["module_defaults"] = Values(catalog, choices.ModuleDefaults),
            ["modules"] = modules,
        };
    }

    private static ScriptObject Module(CatalogModel catalog, string name, IReadOnlyDictionary<string, IReadOnlyList<string>> values) =>
        new() { ["name"] = name, ["namespace"] = Names.ToPascalCase(name), ["values"] = Values(catalog, values) };

    /// <summary>Single-valued dimensions become strings, multi-valued ones lists.</summary>
    private static ScriptObject Values(CatalogModel catalog, IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        var result = new ScriptObject();
        foreach (var (dimension, chosen) in values)
        {
            var multi = catalog.Dimensions.TryGetValue(dimension, out var definition) && definition.Multi;
            result[dimension] = multi ? new ScriptArray(chosen) : chosen.Single();
        }

        return result;
    }
}
