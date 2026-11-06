using Fabric.Core.Catalog;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Fabric.Core.Matrix;

/// <summary>
/// Reads <c>build/matrix-extra.yaml</c>: <c>combinations:</c>, a list of entries with a <c>reason</c> and the
/// <c>choices</c> they pin, written like a profile's (<c>dimension: option</c> or <c>dimension: [option, ...]</c>).
/// Structure only: whether the choices exist and fit together is checked when the matrix is built.
/// </summary>
public static class MatrixExtraReader
{
    private const string Owner = "the extra combinations";

    public static IReadOnlyList<MatrixExtra> ReadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var reader = File.OpenText(path);
        return Read(reader, path);
    }

    public static IReadOnlyList<MatrixExtra> Read(TextReader reader, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(sourceName);

        var stream = new YamlStream();
        try
        {
            stream.Load(reader);
        }
        catch (YamlException exception)
        {
            throw new CatalogException($"{sourceName}:{exception.Start.Line}: {exception.Message}", exception);
        }

        if (stream.Documents.Count != 1)
        {
            throw new CatalogException($"{sourceName}: expected exactly one YAML document.");
        }

        var source = new YamlSource(sourceName);
        var root = source.Mapping(stream.Documents[0].RootNode, Owner);
        var combinations = source.Fields(root, Owner, "combinations").Required("combinations");
        if (combinations is not YamlSequenceNode sequence)
        {
            throw source.Error(combinations, "'combinations' must be a list.");
        }

        List<MatrixExtra> extras = [];
        foreach (var node in sequence.Children)
        {
            var owner = $"extra combination {extras.Count + 1}";
            var fields = source.Fields(source.Mapping(node, owner), owner, "reason", "choices");
            var reason = source.Scalar(fields.Required("reason"), owner);

            OrderedDictionary<string, IReadOnlyList<string>> choices = [];
            foreach (var (key, value) in source.Mapping(fields.Required("choices"), $"'choices' of {owner}").Children)
            {
                choices[source.Scalar(key, owner)] = source.ScalarOrList(value, owner);
            }

            extras.Add(new MatrixExtra(reason, choices));
        }

        return extras;
    }
}
