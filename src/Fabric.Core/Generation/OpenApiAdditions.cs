using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fabric.Core.Generation;

/// <summary>
/// What a new module adds to the frontend's copy of the OpenAPI document (ADR-0005, amended for #609): the paths and
/// schemas the generator writes for the project with the module and not without it. add-module inserts them into the
/// copy as it is, so nothing a person or the integration tests wrote there changes.
/// </summary>
/// <param name="Paths">The new paths, in the order the host maps them.</param>
/// <param name="Schemas">The new schemas.</param>
/// <param name="LaterModules">
/// The route prefixes (<c>/orders</c>) of the project's modules the host maps after the new one: it maps modules in the
/// order of their namespaces, so the new paths go before the first path of any of these.
/// </param>
public sealed record OpenApiAdditions(
    IReadOnlyList<KeyValuePair<string, JsonNode>> Paths,
    IReadOnlyList<KeyValuePair<string, JsonNode>> Schemas,
    IReadOnlyList<string> LaterModules)
{
    /// <summary>The committed copy every frontend api-client pack writes.</summary>
    public const string DocumentPath = "web/app/openapi.json";

    // As the generated OpenApiDocumentTests writes the copy, so the copy stays byte for byte what the host serves.
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n" };

    /// <summary>Inserts the paths and schemas into <paramref name="document"/>.</summary>
    /// <returns>An error naming a path or schema the document already has, or <c>null</c> with <paramref name="extended"/> set.</returns>
    public string? InsertInto(string document, out string extended)
    {
        ArgumentNullException.ThrowIfNull(document);

        extended = document;
        JsonObject root;
        try
        {
            if (JsonNode.Parse(document) is not JsonObject parsed)
            {
                return $"{DocumentPath} is not a JSON object; add-module adds the new module's paths to it.";
            }

            root = parsed;
        }
        catch (JsonException exception)
        {
            return $"{DocumentPath} is not valid JSON ({exception.Message}); add-module adds the new module's paths to it.";
        }

        if (!TryObject(root, "paths", out var paths))
        {
            return NotAnObject("paths");
        }

        if (!TryObject(root, "components", out var components))
        {
            return NotAnObject("components");
        }

        JsonObject? schemas = null;
        if (components is not null && !TryObject(components, "schemas", out schemas))
        {
            return NotAnObject("components.schemas");
        }

        if (Paths.FirstOrDefault(path => paths?.ContainsKey(path.Key) == true) is { Key: { } existingPath })
        {
            return Refusal("path", existingPath);
        }

        if (Schemas.FirstOrDefault(schema => schemas?.ContainsKey(schema.Key) == true) is { Key: { } existingSchema })
        {
            return Refusal("schema", existingSchema);
        }

        if (Paths.Count > 0)
        {
            paths ??= Child(root, "paths");
            var at = paths.Select(path => path.Key).ToList().FindIndex(MappedLater);
            at = at < 0 ? paths.Count : at;
            foreach (var (key, value) in Paths)
            {
                paths.Insert(at++, key, value.DeepClone());
            }
        }

        if (Schemas.Count > 0)
        {
            schemas ??= Child(components ?? Child(root, "components"), "schemas");
            foreach (var (key, value) in Schemas)
            {
                var at = schemas.Select(schema => schema.Key).ToList().FindIndex(name => string.CompareOrdinal(name, key) > 0);
                schemas.Insert(at < 0 ? schemas.Count : at, key, value.DeepClone());
            }
        }

        extended = root.ToJsonString(Indented) + "\n";
        return null;
    }

    private static string Refusal(string kind, string name) =>
        $"{DocumentPath} already has the {kind} {name}; add-module only adds a new module's paths and schemas.";

    private static string NotAnObject(string member) =>
        $"{DocumentPath} has a {member} that is not a JSON object; add-module adds the new module's paths to it.";

    // A member that is missing is fine, add-module creates it; one of another kind is a copy it cannot extend.
    private static bool TryObject(JsonObject parent, string name, out JsonObject? child)
    {
        child = parent[name] as JsonObject;
        return child is not null || parent[name] is null;
    }

    private static JsonObject Child(JsonObject parent, string name)
    {
        var child = new JsonObject();
        parent[name] = child;
        return child;
    }

    private bool MappedLater(string path) =>
        LaterModules.Any(prefix => path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal));
}
