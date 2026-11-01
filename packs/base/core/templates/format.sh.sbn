#!/usr/bin/env bash
# Formats every C# project under src/ and tests/: bash .github/scripts/format.sh
# CI checks instead of formatting: bash .github/scripts/format.sh --verify-no-changes
#
# dotnet format reads only C# projects and solutions: given build.proj (a traversal project) it formats nothing and
# succeeds, and Solution.slnx lists only the projects present at generation. So the projects are found the way
# build.proj finds them and listed in a throwaway solution.
set -euo pipefail

cd "$(dirname "$0")/../.."
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

dotnet new sln --name format --format slnx --output "$work" >/dev/null
find "$PWD/src" "$PWD/tests" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | sort -z \
  | xargs -0 dotnet sln "$work/format.slnx" add >/dev/null
dotnet format "$work/format.slnx" "$@"
