namespace Fabric.Cli.Tests;

/// <summary>
/// Stands in for a released fabric, which a test cannot install: a script that reports <paramref name="version"/>, runs
/// this build's CLI with the given catalog and packs, and records <paramref name="version"/> in the <c>fabric.yaml</c> it
/// writes, as that release would. What it generates is what this build generates from those packs.
/// </summary>
internal static class OldFabric
{
    public static string Create(string directory, string version, string catalog, string packs)
    {
        var cli = Path.Combine(AppContext.BaseDirectory, "fabric.dll");
        var script = Path.Combine(directory, $"fabric-{version}");
        File.WriteAllText(script, $$"""
            #!/usr/bin/env bash
            set -euo pipefail
            if [[ ${1:-} == --version ]]; then echo '{{version}}'; exit 0; fi
            project=.
            arguments=("$@")
            if [[ $1 == new ]]; then project=$2; fi
            for ((i = 0; i < ${#arguments[@]}; i++)); do
              if [[ ${arguments[i]} == --project ]]; then project=${arguments[i + 1]}; fi
            done
            dotnet '{{cli}}' "$@" --catalog '{{catalog}}' --packs '{{packs}}'
            sed -i 's/^generator: .*/generator: {{version}}/' "$project/fabric.yaml"

            """.ReplaceLineEndings("\n"));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return script;
    }
}
