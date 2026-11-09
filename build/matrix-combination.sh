#!/usr/bin/env bash
# One job of the pairwise matrix's nightly run (ADR-0003 amendment, roadmap M3-04): puts one combination of
# build/matrix.json (or the file --matrix names) through build/profile-ci.sh without e2e, the definition of passing
# ADR-0003 gives, and writes <results-directory>/<combination>.json for build/matrix-report.sh and
# build/matrix-issues.sh:
#   name, result (passed or failed), failedStep (profile-ci.sh's step that failed, or null), seconds,
#   fingerprint (sha256 of the combination's choices, key order aside: the same choices under another name are the
#   same combination), and supported (the chosen options the generator did not warn are experimental, as
#   dimension=option; null when generation failed, since the generator names them only once it has generated).
# Exits with profile-ci.sh's status, so the job is as red as the combination.
# Needs a Release build of the CLI, jq and sha256sum, and what profile-ci.sh's steps need.
# MATRIX_PROFILE_CI replaces build/profile-ci.sh, for this script's tests.
# Usage: build/matrix-combination.sh <combination> <results-directory> [--matrix <file>] [--steps <steps>]
set -euo pipefail

usage() {
  echo "Usage: build/matrix-combination.sh <combination> <results-directory> [--matrix <file>] [--steps <steps>]" >&2
  exit 2
}

repository="$(cd "$(dirname "$0")/.." && pwd)"
[[ $# -ge 2 ]] || usage
combination="$1"
results="$2"
shift 2
matrix="$repository/build/matrix.json"
steps=generate,build,test,web
while [[ $# -gt 0 ]]; do
  [[ $# -ge 2 ]] || usage
  case "$1" in
    --matrix) matrix="$2" ;;
    --steps) steps="$2" ;;
    *) usage ;;
  esac
  shift 2
done
profile_ci="${MATRIX_PROFILE_CI:-$repository/build/profile-ci.sh}"

choices="$(jq --arg name "$combination" --exit-status '.combinations[] | select(.name == $name) | .choices' "$matrix")" ||
  { echo "error: no combination $combination in $matrix." >&2; exit 2; }
fingerprint="$(jq --sort-keys --compact-output . <<<"$choices" | sha256sum | cut -d' ' -f1)"

mkdir -p "$results"
log="$(mktemp)"
trap 'rm -f "$log"' EXIT
started=$SECONDS
set +e
bash "$profile_ci" --combination "$combination" --matrix "$matrix" --steps "$steps" 2>&1 | tee "$log"
status=${PIPESTATUS[0]}
set -e
seconds=$((SECONDS - started))

# profile-ci.sh opens a group per step after generate, "::group::combination <name>: <step>", and closes it only
# when the step passed, so the last one opened is the step that failed; none means generate did.
failed_step=null
if [[ $status -ne 0 ]]; then
  last_step="$(awk -v prefix="::group::combination $combination: " 'index($0, prefix) == 1 { step = substr($0, length(prefix) + 1) } END { print step }' "$log")"
  failed_step="$(jq --null-input --arg step "${last_step:-generate}" '$step')"
fi

supported=null
if [[ $failed_step != '"generate"' ]]; then
  experimental="$(sed -n 's/^warning: \([^ ]*\) is experimental (ADR-0003).*/\1/p' "$log" | jq --raw-input --slurp 'split("\n") | map(select(. != ""))')"
  supported="$(jq --argjson experimental "$experimental" \
    '[to_entries[] | .key as $dimension | (.value | if type == "array" then .[] else . end) | "\($dimension)=\(.)"] - $experimental' <<<"$choices")"
fi

jq --null-input \
  --arg name "$combination" \
  --arg result "$([[ $status -eq 0 ]] && echo passed || echo failed)" \
  --argjson failedStep "$failed_step" \
  --argjson seconds "$seconds" \
  --arg fingerprint "$fingerprint" \
  --argjson supported "$supported" \
  '{name: $name, result: $result, failedStep: $failedStep, seconds: $seconds, fingerprint: $fingerprint, supported: $supported}' \
  >"$results/$combination.json"
exit "$status"
