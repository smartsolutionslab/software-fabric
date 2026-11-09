#!/usr/bin/env bash
# The pairwise matrix's nightly table (ADR-0003: the matrix runner reports duration per combination so its cost can be
# watched; roadmap M3-04): one row per combination of build/matrix.json (or the file --matrix names), in its order,
# with the result, the step that failed and the duration that build/matrix-combination.sh wrote to the results
# directory. A combination with no result file, such as one whose job was cancelled or never started, is "no result".
# Markdown on standard output, for the run's summary.
# Needs jq.
# Usage: build/matrix-report.sh <results-directory> [--matrix <file>]
set -euo pipefail

usage() {
  echo "Usage: build/matrix-report.sh <results-directory> [--matrix <file>]" >&2
  exit 2
}

repository="$(cd "$(dirname "$0")/.." && pwd)"
[[ $# -eq 1 || $# -eq 3 && $2 == --matrix ]] || usage
results="$1"
matrix="${3:-$repository/build/matrix.json}"

# Every result as one array, empty when the directory holds none.
shopt -s nullglob
files=("$results"/*.json)
if [[ ${#files[@]} -gt 0 ]]; then
  all="$(jq --slurp . "${files[@]}")"
else
  all='[]'
fi

jq --raw-output --argjson results "$all" '
  def duration: "\(. / 60 | floor)m \(. % 60 | tostring | if length < 2 then "0" + . else . end)s";
  [.combinations[].name] as $names
  | ($results | map({key: .name, value: .}) | from_entries) as $by_name
  | [$names[] | $by_name[.]] as $rows
  | ([$rows[] | select(. != null and .result == "passed")] | length) as $passed
  | ([$rows[] | select(. != null)]) as $ran
  | "## Pairwise matrix: \($passed) of \($names | length) combinations passed",
    "",
    "| Combination | Result | Failed at | Duration |",
    "|---|---|---|---|",
    ($names[] as $name | $by_name[$name] as $row
      | if $row == null then "| \($name) | no result |  |  |"
        else "| \($name) | \($row.result) | \($row.failedStep // "") | \($row.seconds | duration) |" end),
    "",
    "Total time: \([$ran[].seconds] | add // 0 | duration) in \($ran | length) jobs."' "$matrix"
