using System.CommandLine;
using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Generation;
using Fabric.Core.Packs;

namespace Fabric.Cli;

/// <summary>The <c>fabric</c> command line. Returns 0 on success, 1 when anything was rejected.</summary>
public static class FabricCli
{
    private static readonly string ShippedCatalog = Path.Combine(AppContext.BaseDirectory, "catalog", "catalog.yaml");
    private static readonly string ShippedPacks = Path.Combine(AppContext.BaseDirectory, "packs");

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var root = new RootCommand("Generate .NET + React/Vue projects from a catalog of options and packs.")
        {
            ValidateCommand(output, error),
            NewCommand(output, error),
            AddModuleCommand(output, error),
        };

        return root.Parse(args).Invoke(new InvocationConfiguration { Output = output, Error = error });
    }

    private static Command ValidateCommand(TextWriter output, TextWriter error)
    {
        var (profile, set, catalog) = (ProfileOption(), SetOption(), CatalogOption());
        var command = new Command("validate", "Check a profile plus overrides against every catalog rule, without writing anything.")
        {
            profile,
            set,
            catalog,
        };

        command.SetAction(result => Guarded(error, () =>
        {
            var catalogModel = CatalogReader.ReadFile(result.GetValue(catalog)!);
            var profileName = result.GetValue(profile);
            var resolution = ChoiceResolver.Resolve(catalogModel, profileName, result.GetValue(set) ?? []);
            if (resolution.Choices is not { } choices)
            {
                return Fail(error, resolution.Errors);
            }

            var violations = RuleEvaluator.Check(catalogModel, choices);
            if (violations.Count > 0)
            {
                return Fail(error, violations);
            }

            var subject = profileName is null ? "catalog defaults" : $"profile {profileName}";
            output.WriteLine($"valid: {subject} resolves to {choices.Project.Count} project choices and {choices.ModuleDefaults.Count} module defaults.");
            return 0;
        }));

        return command;
    }

    private static Command NewCommand(TextWriter output, TextWriter error)
    {
        var (profile, set, catalog, packs) = (ProfileOption(), SetOption(), CatalogOption(), PacksOption());
        var directory = new Argument<string>("directory") { Description = "Where to create the project; must be new or empty." };
        var command = new Command("new", "Generate a project from a profile plus overrides.")
        {
            directory,
            profile,
            set,
            catalog,
            packs,
        };

        command.SetAction(result => Guarded(error, () =>
        {
            var catalogModel = CatalogReader.ReadFile(result.GetValue(catalog)!);
            var loaded = PackLoader.Load(result.GetValue(packs)!, catalogModel);
            if (loaded.Errors.Count > 0)
            {
                return Fail(error, loaded.Errors);
            }

            var generated = Generator.New(catalogModel, loaded.Packs, result.GetValue(profile), result.GetValue(set) ?? []);
            if (generated.Errors.Count > 0)
            {
                return Fail(error, generated.Errors);
            }

            var target = result.GetValue(directory)!;
            if (ProjectWriter.Write(target, generated.Files) is { } writeError)
            {
                return Fail(error, [writeError]);
            }

            output.WriteLine($"created {target} with {Files(generated.Files.Count)}.");
            return 0;
        }));

        return command;
    }

    private static Command AddModuleCommand(TextWriter output, TextWriter error)
    {
        var (set, catalog, packs) = (SetOption(), CatalogOption(), PacksOption());
        var name = new Argument<string>("name") { Description = "The module's name, in lower kebab-case." };
        var project = new Option<string>("--project")
        {
            Description = "The generated project to add the module to. Defaults to the current directory.",
            DefaultValueFactory = _ => ".",
        };
        var command = new Command("add-module", "Add a module (bounded context) to a generated project. Only creates files.")
        {
            name,
            set,
            project,
            catalog,
            packs,
        };

        command.SetAction(result => Guarded(error, () =>
        {
            var projectDirectory = result.GetValue(project)!;
            var fabricYamlPath = Path.Combine(projectDirectory, FabricFile.FileName);
            if (!File.Exists(fabricYamlPath))
            {
                return Fail(error, [$"no {FabricFile.FileName} in {projectDirectory}; run add-module in a generated project or pass --project."]);
            }

            FabricRecord record;
            using (var reader = File.OpenText(fabricYamlPath))
            {
                record = FabricFile.Read(reader, fabricYamlPath);
            }

            var catalogModel = CatalogReader.ReadFile(result.GetValue(catalog)!);
            var loaded = PackLoader.Load(result.GetValue(packs)!, catalogModel);
            if (loaded.Errors.Count > 0)
            {
                return Fail(error, loaded.Errors);
            }

            var moduleName = result.GetValue(name)!;
            var addition = Generator.AddModule(catalogModel, loaded.Packs, record, moduleName, result.GetValue(set) ?? []);
            if (addition.FabricYaml is not { } fabricYaml)
            {
                return Fail(error, addition.Errors);
            }

            if (ProjectWriter.Add(projectDirectory, addition.Created, fabricYaml) is { } writeError)
            {
                return Fail(error, [writeError]);
            }

            output.WriteLine($"added module {moduleName} with {Files(addition.Created.Count)}.");
            return 0;
        }));

        return command;
    }

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    private static Option<string?> ProfileOption() => new("--profile") { Description = "A profile from the catalog to start from." };

    private static Option<string[]> SetOption() => new("--set")
    {
        Description = "Override one dimension: --set dimension=option (comma-separated for multi-valued dimensions). Repeatable.",
        Arity = ArgumentArity.ZeroOrMore,
    };

    private static Option<string> CatalogOption() => new("--catalog")
    {
        Description = "Path to catalog.yaml. Defaults to the catalog shipped with the tool.",
        DefaultValueFactory = _ => ShippedCatalog,
    };

    private static Option<string> PacksOption() => new("--packs")
    {
        Description = "Path to the packs directory. Defaults to the packs shipped with the tool.",
        DefaultValueFactory = _ => ShippedPacks,
    };

    /// <summary>A catalog the tool cannot read is the user's input, not a crash: report it like any other error.</summary>
    private static int Guarded(TextWriter error, Func<int> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception) when (exception is CatalogException or FabricFileException or IOException or UnauthorizedAccessException)
        {
            return Fail(error, [exception.Message]);
        }
    }

    private static int Fail(TextWriter error, IEnumerable<string> errors)
    {
        foreach (var message in errors)
        {
            error.WriteLine($"error: {message}");
        }

        return 1;
    }
}
