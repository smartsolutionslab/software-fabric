#!/usr/bin/env bash
# Profile run (ADR-0003, roadmap M0-12). Needs a Release build of the solution.
#
# 1. Every profile of the fixture catalog is generated twice from the stub packs and the two trees must be
#    byte-identical; then a module is added to one of them.
# 2. Every profile of the real catalog must validate. Generating and building those projects starts in M1, when the
#    first real packs exist; until then `fabric new` would rightly stop at "no pack for ...".
set -euo pipefail

repository="$(cd "$(dirname "$0")/.." && pwd)"
cli="$repository/src/Fabric.Cli/bin/Release/net10.0/fabric.dll"
fixture="$repository/tests/fixtures/minimal"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fabric() { dotnet "$cli" "$@"; }

for profile in $(fabric profiles --catalog "$fixture/catalog.yaml"); do
  for run in first second; do
    fabric new "$work/$profile-$run" --name "$profile" --profile "$profile" --catalog "$fixture/catalog.yaml" --packs "$fixture/packs" >/dev/null
  done
  diff -r "$work/$profile-first" "$work/$profile-second"
  fabric add-module billing --project "$work/$profile-first" --catalog "$fixture/catalog.yaml" --packs "$fixture/packs" >/dev/null
  echo "fixture profile $profile: generated twice, identical; module added"
done

for profile in $(fabric profiles); do
  fabric validate --profile "$profile"
done
