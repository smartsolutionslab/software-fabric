#!/usr/bin/env bash
# Profile CI's matrix (ADR-0003): the profiles the CLI generates from the real packs alone, so a profile is run as
# soon as its last pack is written, without editing the workflow. Needs a Release build of the CLI.
#
# Writes one line for $GITHUB_OUTPUT, profiles=["...", ...]. Each profile left out because packs are missing gets a
# ::notice:: naming them (the slots only those packs declare fail too, and are not repeated). A profile that fails to
# generate with every pack present fails this script.
# Usage: build/complete-profiles.sh >> "$GITHUB_OUTPUT"
set -euo pipefail

repository="$(cd "$(dirname "$0")/.." && pwd)"
cli="$repository/src/Fabric.Cli/bin/Release/net10.0/fabric.dll"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fabric() { dotnet "$cli" "$@"; }

missing_prefix="error: no pack for "
# Assigned first: a failing command substitution inside the for list would not stop the script.
profiles="$(fabric profiles)"
complete=()
for profile in $profiles; do
  if errors="$(fabric new "$work/$profile" --name shop --profile "$profile" 2>&1 >/dev/null)"; then
    complete+=("\"$profile\"")
  elif grep --quiet "^$missing_prefix" <<<"$errors"; then
    missing="$(grep "^$missing_prefix" <<<"$errors" | sed -e "s/^$missing_prefix//" -e 's/\.$//' | paste -s -d , - | sed 's/,/, /g')"
    echo "::notice title=profile CI::$profile is not run, its packs are not all written: no pack for $missing." >&2
  else
    echo "::error title=profile CI::$profile does not generate:" >&2
    echo "$errors" >&2
    exit 1
  fi
done

echo "profiles=[$(IFS=,; echo "${complete[*]}")]"
