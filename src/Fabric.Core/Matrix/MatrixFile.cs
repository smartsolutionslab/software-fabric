using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Fabric.Core.Matrix;

/// <summary>Writes the matrix as <c>build/matrix.json</c>: indented, <c>\n</c> line ends, the same bytes for the same matrix.</summary>
public static class MatrixFile
{
    private const string About =
        "Written by fabric matrix: a pairwise covering set over every option with a pack (ADR-0003). " +
        "Each combination is a full choice set without a profile, as --set arguments. Do not edit; regenerate it.";

    public static string Write(MatrixResult matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("about", About);
            writer.WriteNumber("feasiblePairs", matrix.FeasiblePairs);
            writer.WriteStartArray("combinations");
            foreach (var combination in matrix.Combinations)
            {
                writer.WriteStartObject();
                writer.WriteString("name", combination.Name);
                if (combination.Reason is { } reason)
                {
                    writer.WriteString("reason", reason);
                }

                writer.WriteStartArray("set");
                foreach (var entry in combination.Set)
                {
                    writer.WriteStringValue(entry);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("infeasiblePairs");
            foreach (var pair in matrix.InfeasiblePairs)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(pair.First);
                writer.WriteStringValue(pair.Second);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }
}
