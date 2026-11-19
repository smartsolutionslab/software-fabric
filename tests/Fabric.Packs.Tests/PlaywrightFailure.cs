using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Fabric.Packs.Tests;

/// <summary>
/// What a failed Playwright run leaves in test-results, as text for a test's failure message (#791): CI keeps no
/// Playwright artifact a reader of the log can open. Per failed test, its error-context.md (the page snapshot) whole,
/// then from its trace.zip the pages the browser went to, the requests that failed or answered 400 or more, and the
/// console's errors and the page's uncaught errors. A request still open when the test ended is not in the trace.
/// </summary>
internal static class PlaywrightFailure
{
    public static string Describe(string testResults)
    {
        if (!Directory.Exists(testResults))
        {
            return $"No Playwright test results in {testResults}.";
        }

        var description = new StringBuilder();
        foreach (var test in Directory.EnumerateDirectories(testResults).Order(StringComparer.Ordinal))
        {
            var errorContext = Path.Combine(test, "error-context.md");
            var trace = Path.Combine(test, "trace.zip");
            if (!File.Exists(errorContext) && !File.Exists(trace))
            {
                continue;
            }

            description.AppendLine(CultureInfo.InvariantCulture, $"--- {Path.GetFileName(test)}");
            if (File.Exists(errorContext))
            {
                description.AppendLine(File.ReadAllText(errorContext));
            }

            if (File.Exists(trace))
            {
                DescribeTrace(trace, description);
            }
        }

        return description.Length == 0 ? $"No Playwright test results in {testResults}." : description.ToString();
    }

    private static void DescribeTrace(string trace, StringBuilder description)
    {
        List<string> pages = [];
        List<string> refused = [];
        List<string> errors = [];
        var requests = 0;
        using var archive = ZipFile.OpenRead(trace);
        foreach (var entry in archive.Entries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            // The trace's events (*.trace) and requests (*.network) are JSON lines; resources, sources and stacks are not read.
            if (entry.FullName.Contains('/', StringComparison.Ordinal)
                || !(entry.FullName.EndsWith(".trace", StringComparison.Ordinal) || entry.FullName.EndsWith(".network", StringComparison.Ordinal)))
            {
                continue;
            }

            foreach (var line in Lines(entry))
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                switch (Text(root, "type"))
                {
                    case "resource-snapshot":
                        requests++;
                        Request(root.GetProperty("snapshot"), pages, refused);
                        break;
                    case "console" when Text(root, "messageType") == "error":
                        errors.Add($"error: {Text(root, "text")} ({Location(root)})");
                        break;
                    case "event" when Text(root, "method") == "pageError":
                        var error = root.GetProperty("params");
                        errors.Add($"uncaught: {Text(error.GetProperty("error").GetProperty("error"), "message")} ({Location(error)})");
                        break;
                }
            }
        }

        Section(description, "Pages, in order:", pages);
        Section(description, "Requests that failed or answered 400 or more:", refused);
        description.AppendLine(CultureInfo.InvariantCulture, $"{requests} requests recorded; one still open when the test ended is not.");
        Section(description, "Console errors and uncaught page errors:", errors);
    }

    private static void Request(JsonElement snapshot, List<string> pages, List<string> refused)
    {
        var status = snapshot.GetProperty("response").GetProperty("status").GetInt32();
        var request = snapshot.GetProperty("request");
        var summary = $"{(status < 0 ? "failed" : status.ToString(CultureInfo.InvariantCulture))} {Text(request, "method")} {Text(request, "url")} ({Text(snapshot, "_resourceType")})";
        var line = $"{Text(snapshot, "startedDateTime")} {summary}";
        if (Text(snapshot, "_resourceType") == "document")
        {
            pages.Add(line);
        }
        else if (status < 0 || status >= 400)
        {
            refused.Add(line);
        }
    }

    private static IEnumerable<string> Lines(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }

    private static string Location(JsonElement element) =>
        element.TryGetProperty("location", out var location) ? Text(location, "url") : string.Empty;

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    private static void Section(StringBuilder description, string title, List<string> lines)
    {
        description.AppendLine(lines.Count == 0 ? $"{title} none" : title);
        foreach (var line in lines)
        {
            description.AppendLine(CultureInfo.InvariantCulture, $"  {line}");
        }
    }
}
