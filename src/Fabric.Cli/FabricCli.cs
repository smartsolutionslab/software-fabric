using System.CommandLine;
using Fabric.Core.Catalog;
using Fabric.Core.Choices;
using Fabric.Core.Generation;
using Fabric.Core.Matrix;
using Fabric.Core.Packs;
using Fabric.Core.Reference;
using Fabric.Core.Upgrade;

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
            UpgradeCommand(output, error),
            ProfilesCommand(output, error),
            MatrixCommand(output, error),
            ReferenceCommand(output, error),
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
            var projectChoices = new ProjectChoices(choices.Project, choices.ModuleDefaults, new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>());
            WarnExperimental(error, ExperimentalOptions.Chosen(catalogModel, projectChoices));
            return 0;
        }));

        return command;
    }

    private static Command NewCommand(TextWriter output, TextWriter error)
    {
        var (profile, set, catalog, packs) = (ProfileOption(), SetOption(), CatalogOption(), PacksOption());
        var directory = new Argument<string>("directory") { Description = "Where to create the project; must be new or empty." };
        var name = new Option<string?>("--name") { Description = "The project's name in lower kebab-case. Defaults to the directory's name." };
        var choices = new Option<string?>("--choices")
        {
            Description = "A JSON file of dimension to option (or to a list of options), such as a combination of build/matrix.json, " +
                "to generate from instead of a profile. Overrides apply on top of it.",
        };
        var command = new Command("new", "Generate a project from a profile, or a choice set file, plus overrides.")
        {
            directory,
            name,
            profile,
            choices,
            set,
            catalog,
            packs,
        };

        command.SetAction(result => Guarded(error, () =>
        {
            var target = result.GetValue(directory)!;
            var projectName = result.GetValue(name) ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)));
            if (result.GetValue(name) is null && !Names.IsValid(projectName))
            {
                return Fail(error, [$"project name '{projectName}' must be lower-case letters, digits and hyphens, starting with a letter. Pass --name."]);
            }

            IReadOnlyList<string> overrides = result.GetValue(set) ?? [];
            if (result.GetValue(choices) is { } choiceSetPath)
            {
                if (result.GetValue(profile) is not null)
                {
                    return Fail(error, ["--choices is a whole choice set and takes no --profile; pass one or the other."]);
                }

                var choiceSet = ChoiceSetFile.Read(File.ReadAllText(choiceSetPath), choiceSetPath);
                if (choiceSet.Errors.Count > 0)
                {
                    return Fail(error, choiceSet.Errors);
                }

                overrides = [.. choiceSet.Overrides, .. overrides];
            }

            var catalogModel = CatalogReader.ReadFile(result.GetValue(catalog)!);
            var loaded = PackLoader.Load(result.GetValue(packs)!, catalogModel);
            if (loaded.Errors.Count > 0)
            {
                return Fail(error, loaded.Errors);
            }

            var generated = Generator.New(catalogModel, loaded.Packs, projectName, result.GetValue(profile), overrides);
            if (generated.Errors.Count > 0)
            {
                return Fail(error, generated.Errors);
            }

            if (ProjectWriter.Write(target, generated.Files) is { } writeError)
            {
                return Fail(error, [writeError]);
            }

            output.WriteLine($"created {target} with {Files(generated.Files.Count)}.");
            WarnExperimental(error, generated.Experimental);
            if (FirstSteps(generated.Files) is { } firstSteps)
            {
                output.WriteLine();
                output.WriteLine("First steps (README.md):");
                output.WriteLine(firstSteps);
            }

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

            if (ProjectWriter.Add(projectDirectory, addition.Created, fabricYaml, addition.OpenApi) is { } writeError)
            {
                return Fail(error, [writeError]);
            }

            output.WriteLine($"added module {moduleName} with {Files(addition.Created.Count)}.");
            if (addition.OpenApi is { } openApi)
            {
                output.WriteLine($"{OpenApiAdditions.DocumentPath}: added {Describe(openApi)}.");
            }
            WarnExperimental(error, addition.Experimental);
            return 0;
        }));

        return command;
    }

    private static Command UpgradeCommand(TextWriter output, TextWriter error)
    {
        var (catalog, packs) = (CatalogOption(), PacksOption());
        var project = new Option<string>("--project")
        {
            Description = "The generated project to upgrade: the top of a git repository with a clean working tree. Defaults to the current directory.",
            DefaultValueFactory = _ => ".",
        };
        var from = new Option<string?>("--from")
        {
            Description = "The fabric that wrote the project (a tool or its fabric.dll), for a version that cannot be installed. " +
                "Without it, the version fabric.yaml records is installed from the organisation's feed.",
        };
        var command = new Command(
            "upgrade",
            "Merge what this fabric writes for the project's fabric.yaml into the project, three-way against what the recorded " +
            "version writes (ADR-0016). Conflicts are left as git conflicts; nothing is committed.")
        {
            project,
            from,
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

            var upgrade = Upgrader.Upgrade(result.GetValue(project)!, result.GetValue(from), catalogModel, loaded.Packs);
            if (upgrade.Summary is not { } summary)
            {
                return Fail(error, upgrade.Errors);
            }

            output.Write(summary);
            WarnExperimental(error, upgrade.Experimental);
            return 0;
        }));

        return command;
    }

    private static Command ProfilesCommand(TextWriter output, TextWriter error)
    {
        var catalog = CatalogOption();
        var command = new Command("profiles", "List the catalog's profiles, one per line.") { catalog };

        command.SetAction(result => Guarded(error, () =>
        {
            foreach (var profile in CatalogReader.ReadFile(result.GetValue(catalog)!).Profiles.Keys)
            {
                output.WriteLine(profile);
            }

            return 0;
        }));

        return command;
    }

    private static Command MatrixCommand(TextWriter output, TextWriter error)
    {
        var (catalog, packs) = (CatalogOption(), PacksOption());
        var extra = new Option<string?>("--extra") { Description = "A matrix-extra.yaml of combinations to add to the covering set." };
        var file = new Option<string?>("--output") { Description = "Where to write matrix.json. Without it, the matrix is printed." };
        var command = new Command(
            "matrix",
            "Build the pairwise matrix (ADR-0003): valid choice sets that together contain every pair of options with a pack " +
            "that any valid choice set contains, plus the pairs none does.")
        {
            extra,
            file,
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

            // Without packs the domain is empty and so is the matrix; written over build/matrix.json, that would drop every row.
            if (loaded.Packs.Count == 0)
            {
                return Fail(error, [$"no packs under {result.GetValue(packs)}; the matrix covers the options that have one."]);
            }

            var extras = result.GetValue(extra) is { } extraPath ? MatrixExtraReader.ReadFile(extraPath) : [];
            var matrix = PairwiseMatrix.Build(catalogModel, loaded.Packs.Keys, extras);
            if (matrix.Errors.Count > 0)
            {
                return Fail(error, matrix.Errors);
            }

            var json = MatrixFile.Write(matrix);
            if (result.GetValue(file) is not { } target)
            {
                output.Write(json);
                return 0;
            }

            File.WriteAllText(target, json);
            var infeasible = matrix.InfeasiblePairs.Count;
            output.WriteLine(
                $"wrote {target}: {Count(matrix.Combinations.Count, "combination")} cover {matrix.FeasiblePairs} feasible pairs; " +
                $"{Count(infeasible, "pair")} {(infeasible == 1 ? "is" : "are")} infeasible.");
            return 0;
        }));

        return command;
    }

    private static Command ReferenceCommand(TextWriter output, TextWriter error)
    {
        var catalog = CatalogOption();
        var file = new Option<string?>("--output") { Description = "Where to write the reference (docs/catalog.md). Without it, the reference is printed." };
        var command = new Command(
            "reference",
            "Write the catalog reference: every dimension and option with its tier, rules, note and the profiles that use it.")
        {
            file,
            catalog,
        };

        command.SetAction(result => Guarded(error, () =>
        {
            var catalogModel = CatalogReader.ReadFile(result.GetValue(catalog)!);
            var markdown = CatalogReference.Write(catalogModel);
            if (result.GetValue(file) is not { } target)
            {
                output.Write(markdown);
                return 0;
            }

            File.WriteAllText(target, markdown);
            var options = catalogModel.Dimensions.Values.Sum(dimension => dimension.Options.Count);
            output.WriteLine(
                $"wrote {target}: {Count(catalogModel.Dimensions.Count, "dimension")}, {Count(options, "option")}, " +
                $"{Count(catalogModel.Profiles.Count, "profile")}.");
            return 0;
        }));

        return command;
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    /// <summary>"/billing/permissions/manage and the schema PermissionDto": what add-module put into the OpenAPI copy.</summary>
    private static string Describe(OpenApiAdditions openApi)
    {
        var parts = openApi.Paths.Select(path => path.Key).ToList();
        if (openApi.Schemas.Count > 0)
        {
            parts.Add((openApi.Schemas.Count == 1 ? "the schema " : "the schemas ") + string.Join(", ", openApi.Schemas.Select(schema => schema.Key)));
        }

        return parts.Count == 1 ? parts[0] : string.Join(", ", parts[..^1]) + " and " + parts[^1];
    }

    /// <summary>The generated README's "## First steps" section, which packs fill through readme.first-steps.</summary>
    private static string? FirstSteps(IReadOnlyList<Core.Rendering.RenderedFile> files)
    {
        var readme = files.FirstOrDefault(file => file.Path == "README.md")?.Content;
        const string heading = "## First steps\n";
        var start = readme?.IndexOf(heading, StringComparison.Ordinal) ?? -1;
        if (start < 0)
        {
            return null;
        }

        var section = readme![(start + heading.Length)..];
        var end = section.IndexOf("\n## ", StringComparison.Ordinal);
        return (end < 0 ? section : section[..end]).Trim();
    }

    private static Option<string?> ProfileOption() => new("--profile") { Description = "A profile from the catalog to start from." };

    private static Option<string[]> SetOption() => new("--set")
    {
        Description = "Override one dimension: --set dimension=option (comma-separated for multi-valued dimensions), " +
            "or set a chosen option's number: --set dimension.option.setting=number. Repeatable.",
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

    /// <summary>ADR-0003: an experimental option is generated with a warning. Standard error, so scripts reading the output are unaffected.</summary>
    private static void WarnExperimental(TextWriter error, IReadOnlyList<string> options)
    {
        foreach (var option in options)
        {
            error.WriteLine($"warning: {option} is experimental (ADR-0003): it is generated, but not yet passing the test matrix.");
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
