#!/usr/bin/env bash
# Profile CI (ADR-0003, roadmap M1-09): generates a profile with the CLI from the real packs, no stand-ins, adds a
# module, and runs what the generated project's own CI (.github/workflows/ci.yml) runs: backend format check, build and
# tests, the web app's install, build, lint, tests and format check, and the end-to-end tests against the running
# application.
# Needs a Release build of the solution, Docker, Node 22 with pnpm, the Aspire CLI bundle and Playwright's Chromium.
# Usage: build/profile-ci.sh <profile>
set -euo pipefail

profile="$1"
repository="$(cd "$(dirname "$0")/.." && pwd)"
cli="$repository/src/Fabric.Cli/bin/Release/net10.0/fabric.dll"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fabric() { dotnet "$cli" "$@"; }

project="$work/shop"
fabric new "$project" --profile "$profile"
fabric add-module billing --project "$project"
cd "$project"

step() { echo "::group::$profile: $1"; }

step "backend format check"
bash .github/scripts/format.sh --verify-no-changes
echo "::endgroup::"

step "backend build"
dotnet build build.proj -c Release
echo "::endgroup::"

for tests in $(find tests -name '*.Tests.csproj' | sort); do
  step "$tests"
  dotnet test --project "$tests" -c Release --no-build
  echo "::endgroup::"
done

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

echo "profile $profile: generated, built, tested, end to end"
