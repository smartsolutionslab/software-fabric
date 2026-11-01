#!/usr/bin/env bash
# Runs the test projects with code coverage, then the coverage gate: each module's Domain and Application, and the
# Kernel, keep the line coverage tests/coverage.props sets (docs/adr). Needs a Release build (dotnet build build.proj
# -c Release). The backend CI runs it with no arguments.
# Usage: bash .github/scripts/test.sh [test project ...]   (default: every *.Tests.csproj under tests/)
set -euo pipefail

cd "$(dirname "$0")/../.."
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

if [ "$#" -eq 0 ]; then
  # Assigned first: a failing command substitution inside set -- would not stop the script.
  projects="$(find tests -name '*.Tests.csproj' -not -path '*/bin/*' -not -path '*/obj/*' | sort)"
  # shellcheck disable=SC2086 # one project per word; project paths have no spaces
  set -- $projects
fi

for project in "$@"; do
  dotnet test --project "$project" -c Release --no-build \
    --coverage --coverage-output-format cobertura --coverage-output "$work/$(basename "$project" .csproj).cobertura.xml"
done

dotnet msbuild .github/scripts/coverage-gate.proj -nologo -p:CoverageDirectory="$work"
