#!/usr/bin/env bash
# Profile CI (ADR-0003, roadmap M1-09): generates a profile with the CLI from the real packs, no stand-ins, adds a
# module, and runs what the generated project's own CI (.github/workflows/ci.yml) runs: backend format check, build,
# tests and coverage gate, the web app's install, build, lint, tests and format check, and the end-to-end tests against the running
# application.
# Needs a Release build of the solution, Docker, Node 22 with pnpm, the Aspire CLI bundle and Playwright's Chromium.
#
# Overrides go to `fabric new` as they are, so a pack can go through profile CI before the profile that needs it is
# complete: build/profile-ci.sh saas-standard-react --set frontend.design-system=tokens-and-components
# Usage: build/profile-ci.sh <profile> [--set dimension=option ...]
set -euo pipefail

usage() {
  echo "Usage: build/profile-ci.sh <profile> [--set dimension=option ...]" >&2
  exit 2
}

[[ $# -ge 1 && $1 != -* ]] || usage
profile="$1"
shift
sets=()
while [[ $# -gt 0 ]]; do
  [[ $1 == --set && $# -ge 2 ]] || usage
  sets+=(--set "$2")
  shift 2
done
# What the step names and the last line call this run: the profile, and the overrides when there are any.
run="$profile${sets[*]:+ ${sets[*]}}"
repository="$(cd "$(dirname "$0")/.." && pwd)"
cli="$repository/src/Fabric.Cli/bin/Release/net10.0/fabric.dll"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fabric() { dotnet "$cli" "$@"; }

project="$work/shop"
fabric new "$project" --profile "$profile" "${sets[@]}"
fabric add-module billing --project "$project"
cd "$project"

step() { echo "::group::$run: $1"; }

step "backend format check"
bash .github/scripts/format.sh --verify-no-changes
echo "::endgroup::"

step "backend build"
dotnet build build.proj -c Release
echo "::endgroup::"

step "backend tests with coverage, and the coverage gate"
bash .github/scripts/test.sh
echo "::endgroup::"

step "first step: the lockfile"
(cd web && pnpm install)
echo "::endgroup::"

for command in "install --frozen-lockfile" build lint test; do
  step "pnpm $command"
  # shellcheck disable=SC2086 # the command and its flags are separate words
  (cd web && pnpm $command)
  echo "::endgroup::"
done

step "pnpm format:check"
(cd web/app && pnpm format:check)
echo "::endgroup::"

step "end-to-end"
(cd web/app && pnpm exec playwright install chromium)
bash .github/scripts/e2e.sh
echo "::endgroup::"

echo "profile $run: generated, built, tested, end to end"
