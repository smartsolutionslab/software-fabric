using System.Text.Json;

namespace Fabric.Core.Choices;

/// <summary>
/// A choice set in a file of its own, as a matrix combination spells one out: a JSON object from dimension to an option,
/// or to a list of options for a multi-valued dimension. It is read as the overrides <c>dimension=option[,option]</c>,
/// so the resolver checks it, and fills what it leaves out, exactly as it does for <c>--set</c>.
/// </summary>
public static class ChoiceSetFile
{
    /// <returns>The overrides in the file's order, or the errors, each naming <paramref name="path"/>.</returns>
    public static (IReadOnlyList<string> Overrides, IReadOnlyList<string> Errors) Read(string json, string path)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(path);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return ([], [$"{path}: is not valid JSON: {exception.Message}"]);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ([], [$"{path}: must be a JSON object of dimension to option, or to a list of options."]);
            }

            List<string> overrides = [];
            List<string> errors = [];
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (Options(property.Value) is { } options)
                {
                    overrides.Add($"{property.Name}={string.Join(',', options)}");
                }
                else
                {
                    errors.Add($"{path}: {property.Name} must be an option or a list of options, not {property.Value.GetRawText()}.");
                }
            }

            return errors.Count > 0 ? ([], errors) : (overrides, []);
        }
    }

    private static IReadOnlyList<string>? Options(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => [value.GetString()!],
        JsonValueKind.Array when value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) =>
            [.. value.EnumerateArray().Select(item => item.GetString()!)],
        _ => null,
    };
}
