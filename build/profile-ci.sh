#!/usr/bin/env bash
# Profile CI (ADR-0003, roadmap M1-09): generates a profile with the CLI from the real packs, no stand-ins, adds a
# module, and runs what the generated project's own CI (.github/workflows/ci.yml) runs: backend format check, build,
# tests and coverage gate, the web app's install, build, lint, tests and format check, and the end-to-end tests against the running
# application.
# Needs a Release build of the solution, Docker, Node 22 with pnpm, the Aspire CLI bundle and Playwright's Chromium;
# jq for --combination.
#
# Overrides go to `fabric new` as they are, so a pack can go through profile CI before the profile that needs it is
# complete: build/profile-ci.sh saas-standard-react --set frontend.design-system=tokens-and-components
#
# --combination runs one combination of the pairwise matrix (ADR-0003, build/matrix.json, or the file --matrix names)
# in place of a profile: its choices go to `fabric new --choices`, and what they leave out takes the catalog's defaults.
#
# --steps picks the steps, from generate (fabric new, then a module added), build (format check and build), test
# (tests and coverage gate), web (the web app's install, build, lint, tests and format check) and e2e; all of them by
# default. The matrix's nightly run leaves out e2e (ADR-0003): --steps generate,build,test,web
set -euo pipefail

usage() {
  echo "Usage: build/profile-ci.sh <profile> [--set dimension=option ...] [--steps generate,build,test,web,e2e]" >&2
  echo "Usage: build/profile-ci.sh --combination <name> [--matrix build/matrix.json] [--set dimension=option ...] [--steps ...]" >&2
  [[ $# -eq 0 ]] || echo "$1" >&2
  exit 2
}

all_steps=generate,build,test,web,e2e
repository="$(cd "$(dirname "$0")/.." && pwd)"
profile=""
combination=""
matrix="$repository/build/matrix.json"
steps="$all_steps"
sets=()
if [[ $# -ge 1 && $1 != -* ]]; then
  profile="$1"
  shift
fi
while [[ $# -gt 0 ]]; do
  [[ $# -ge 2 ]] || usage
  case "$1" in
    --set) sets+=(--set "$2") ;;
    --combination) combination="$2" ;;
    --matrix) matrix="$2" ;;
    --steps) steps="$2" ;;
    *) usage ;;
  esac
  shift 2
done
[[ -n $profile && -z $combination || -z $profile && -n $combination ]] || usage

IFS=, read -r -a chosen <<<"$steps"
for name in "${chosen[@]}"; do
  [[ ,$all_steps, == *,$name,* ]] || usage "unknown step $name; the steps are $all_steps."
done
runs() { [[ ,$steps, == *,$1,* ]]; }
runs generate || usage "every run starts with generate; there is nothing to build or test before it."
! runs test || runs build || usage "test needs build: the tests run on the Release build."
! runs e2e || runs web || usage "e2e needs web: the end-to-end tests run from the web app's installed packages."

# What the step names and the last line call this run: the profile or the combination, and the overrides when there are any.
if [[ -n $combination ]]; then
  run="combination $combination${sets[*]:+ ${sets[*]}}"
  summary="$run"
else
  run="$profile${sets[*]:+ ${sets[*]}}"
  summary="profile $run"
fi
cli="$repository/src/Fabric.Cli/bin/Release/net11.0/fabric.dll"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fabric() { dotnet "$cli" "$@"; }

project="$work/shop"
if [[ -n $combination ]]; then
  jq --arg name "$combination" --exit-status '.combinations[] | select(.name == $name) | .choices' "$matrix" >"$work/choices.json" ||
    { echo "error: no combination $combination in $matrix." >&2; exit 1; }
  fabric new "$project" --name shop --choices "$work/choices.json" "${sets[@]}"
else
  fabric new "$project" --profile "$profile" "${sets[@]}"
fi
fabric add-module billing --project "$project"
cd "$project"
passed=(generated)

step() { echo "::group::$run: $1"; }

if runs build; then
  step "backend format check"
  bash .github/scripts/format.sh --verify-no-changes
  echo "::endgroup::"

  step "backend build"
  dotnet build build.proj -c Release
  echo "::endgroup::"
  passed+=(built)
fi

if runs test; then
  step "backend tests with coverage, and the coverage gate"
  bash .github/scripts/test.sh
  echo "::endgroup::"
  passed+=(tested)
fi

if runs web; then
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
  passed+=("web app checked")
fi

if runs e2e; then
  step "end-to-end"
  (cd web/app && pnpm exec playwright install chromium)
  bash .github/scripts/e2e.sh
  echo "::endgroup::"
  passed+=("end to end")
fi

summary_steps="$(printf '%s, ' "${passed[@]}")"
echo "$summary: ${summary_steps%, }"
