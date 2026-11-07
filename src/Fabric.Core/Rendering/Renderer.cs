using System.Text.RegularExpressions;
using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Generation;
using Fabric.Core.Packs;
using Scriban;
using Scriban.Functions;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Fabric.Core.Rendering;

/// <summary>
/// Renders a composition with Scriban. Templates see <c>name</c>, <c>namespace</c>, <c>project</c>,
/// <c>module_defaults</c>, <c>modules</c>, <c>settings</c> (chosen options' numbers, by <c>dimension.option.setting</c>), <c>module</c> (per-module files only) and <c>slots</c> (files only). Nothing from the clock or the machine is
/// available, unknown variables are errors, and output uses LF, so the same input always renders the same bytes.
/// A file's <c>when</c> condition sees the same names as its template; the file is written only when it is <c>true</c>.
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
        var functions = Functions(composition, errors);

        var slots = new ScriptObject();
        foreach (var (slot, entries) in composition.Slots.Where(slot => slot.Key != Composer.TemplateFunctionsSlot))
        {
            // A contribution that renders empty or whitespace-only under its pack's conditions leaves no trace, not even a separator.
            var parts = entries
                .Select(entry => RenderTemplate(entry.Pack, entry.Contribution.Template, functions, errors, model))
                .OfType<string>()
                .Where(part => !string.IsNullOrWhiteSpace(part));
            var separator = composition.SlotSeparators?.GetValueOrDefault(slot) ?? "\n";
            slots.SetValue(slot, string.Join(separator, parts), readOnly: true);
        }

        List<RenderedFile> files = [];
        foreach (var file in composition.Files)
        {
            var scope = new ScriptObject { ["slots"] = slots };
            if (file.Module is { } module)
            {
                scope["module"] = Module(catalog, module, choices.Modules[module]);
            }

            if (file.File.When is { } condition && !Holds(file, condition, errors, model, scope))
            {
                continue;
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

    /// <summary>
    /// The Scriban code packs contribute to <see cref="Composer.TemplateFunctionsSlot"/>, each parsed on its own so that an error names its own
    /// file and line, and so that a template's errors are not offset by the functions evaluated in front of it.
    /// </summary>
    private static List<Template> Functions(Composition composition, List<string> errors)
    {
        List<Template> functions = [];
        foreach (var entry in composition.Slots.GetValueOrDefault(Composer.TemplateFunctionsSlot) ?? [])
        {
            var path = entry.Contribution.Template;
            var template = Template.Parse(
                File.ReadAllText(Path.Combine(entry.Pack.Directory, path)),
                path,
                lexerOptions: new LexerOptions { Mode = ScriptMode.ScriptOnly });
            if (template.HasErrors)
            {
                errors.Add($"{entry.Pack.Option}: {path}: {string.Join("; ", template.Messages)}");
                continue;
            }

            functions.Add(template);
        }

        return functions;
    }

    private static string? RenderTemplate(PackManifest pack, string templatePath, IReadOnlyList<Template> functions, List<string> errors, params ScriptObject[] globals)
    {
        var label = $"{pack.Option}: {templatePath}";
        var template = Template.Parse(File.ReadAllText(Path.Combine(pack.Directory, templatePath)), templatePath);
        if (template.HasErrors)
        {
            errors.Add($"{label}: {string.Join("; ", template.Messages)}");
            return null;
        }

        try
        {
            var context = Context(globals);
            foreach (var function in functions)
            {
                function.Evaluate(context);
            }

            return Tidy(template.Render(context).Replace("\r\n", "\n", StringComparison.Ordinal));
        }
        catch (ScriptRuntimeException exception)
        {
            errors.Add($"{label}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Evaluates a file's <c>when</c>. Only <c>true</c> or <c>false</c> is an answer: a condition that does not parse, fails or yields anything else is
    /// an error naming the pack and the file, never a silent <c>false</c> (ADR-0006 rule 2).
    /// </summary>
    private static bool Holds(ComposedFile file, string condition, List<string> errors, params ScriptObject[] globals)
    {
        var label = $"{file.Pack.Option}: {file.File.Template} ({file.Target}): when '{condition}'";
        var template = Template.Parse(condition, "pack.yaml", lexerOptions: new LexerOptions { Mode = ScriptMode.ScriptOnly });
        if (template.HasErrors)
        {
            errors.Add($"{label}: {string.Join("; ", template.Messages)}");
            return false;
        }

        try
        {
            // Templates keep Scriban's relaxed member access; a condition does not, so a misspelled key fails instead of reading as false.
            var context = Context(globals);
            context.EnableRelaxedMemberAccess = false;
            var value = template.Evaluate(context);
            if (value is bool holds)
            {
                return holds;
            }

            errors.Add($"{label} must be true or false, got '{value ?? "null"}'.");
            return false;
        }
        catch (ScriptRuntimeException exception)
        {
            errors.Add($"{label}: {exception.Message}");
            return false;
        }
    }

    private static TemplateContext Context(ScriptObject[] globals)
    {
        var context = new TemplateContext(Builtins()) { StrictVariables = true };
        foreach (var global in globals)
        {
            context.PushGlobal(global);
        }

        return context;
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
            ["settings"] = Settings(choices.Settings),
        };
    }

    private static ScriptObject Module(CatalogModel catalog, string name, IReadOnlyDictionary<string, IReadOnlyList<string>> values) =>
        new() { ["name"] = name, ["namespace"] = Names.ToPascalCase(name), ["values"] = Values(catalog, values) };

    private static ScriptObject Settings(IReadOnlyDictionary<string, decimal> settings)
    {
        var result = new ScriptObject();
        foreach (var (key, value) in settings)
        {
            result[key] = value;
        }

        return result;
    }

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
