#!/usr/bin/env bash
# ADR-0003: a supported option whose nightly run fails becomes a bug issue on the board; it is not silently demoted.
# A failing pairwise combination cannot say which of its options broke it, so the issue is per combination
# (roadmap M3-04: a broken combination opens one issue), listing the supported options it holds. One whose options are
# all experimental opens none: experimental means not yet passing. One that did not generate opens one, saying the
# run could not tell its tiers.
# The issue carries the combination's fingerprint (build/matrix-combination.sh) in a hidden marker. While an open
# bug issue has that marker, a rerun comments on it instead of opening a second; once it is closed, a new failure
# opens a new issue.
# Reads the results directory build/matrix-combination.sh wrote; needs gh (authenticated), jq and GITHUB_REPOSITORY.
# Usage: build/matrix-issues.sh <results-directory> <run-url>
set -euo pipefail

[[ $# -eq 2 ]] || { echo "Usage: build/matrix-issues.sh <results-directory> <run-url>" >&2; exit 2; }
results="$1"
run_url="$2"
repository="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY names the repository whose issues to open}"

shopt -s nullglob
files=("$results"/*.json)
[[ ${#files[@]} -gt 0 ]] || { echo "No results in $results; nothing to report."; exit 0; }

# Assigned first: a failing command substitution inside the loop's input would not stop the script.
open_issues="$(gh api --paginate "repos/$repository/issues?state=open&labels=bug&per_page=100" | jq --slurp 'add // [] | map(select(.pull_request | not))')"
failed="$(jq --slurp --compact-output 'sort_by(.name)[] | select(.result == "failed")' "${files[@]}")"

while IFS= read -r result; do
  [[ -n $result ]] || continue
  name="$(jq --raw-output .name <<<"$result")"
  step="$(jq --raw-output .failedStep <<<"$result")"
  fingerprint="$(jq --raw-output .fingerprint <<<"$result")"
  marker="<!-- matrix-combination: $fingerprint -->"

  if jq --exit-status '.supported == []' <<<"$result" >/dev/null; then
    echo "$name: failed, but holds no supported option; no issue (ADR-0003)."
    continue
  fi

  existing="$(jq --arg marker "$marker" '[.[] | select(.body // "" | contains($marker)) | .number] | first // empty' <<<"$open_issues")"
  if [[ -n $existing ]]; then
    gh api -X POST "repos/$repository/issues/$existing/comments" -f body="Still failing in $run_url, at $step." >/dev/null
    echo "$name: still failing; commented on #$existing."
    continue
  fi

  if jq --exit-status '.supported == null' <<<"$result" >/dev/null; then
    options="It did not generate, so the run could not tell which of its options are supported."
  else
    options="The supported options it holds; one or more of them fails here together with another option of the combination:"$'\n\n'"$(jq --raw-output '.supported[] | "- `\(.)`"' <<<"$result")"
  fi
  body="The pairwise matrix's combination \`$name\` failed at **$step** in the nightly run $run_url (ADR-0003: a \`supported\` option whose nightly run fails becomes a \`bug\` issue).

$options

Its choices are the combination \`$name\` in \`build/matrix.json\`. Reproduce it with \`build/profile-ci.sh --combination $name --steps generate,build,test,web\`.

$marker"
  number="$(gh api -X POST "repos/$repository/issues" -f title="Nightly matrix: $name fails at $step" -f body="$body" -f 'labels[]=bug' | jq --raw-output .number)"
  echo "$name: opened #$number."
done <<<"$failed"
