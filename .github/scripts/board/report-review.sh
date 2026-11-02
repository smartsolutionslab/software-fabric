#!/usr/bin/env bash
# Posts a reviewer's findings on the pull request and fails when one of them blocks (board protocol, docs/adr).
# Usage: report-review.sh <pull request> <findings file> <reviewer>
# The findings file is the reviewer's final answer. Its findings are the last top-level JSON object in it shaped by
# .github/review-findings.schema.json: the reviewer's CLI cannot hold a model to the schema, and models write prose,
# or a code fence, around their answer. An answer without such an object fails the review.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

pr="$1"
findings_file="$2"
reviewer="$3"

# Every top-level object in the answer: from each "{" that a key or "}" follows, the shortest span to a "}" that parses
# as a JSON object, unless it starts inside an object already found. Then the schema, checked here too: a severity the
# summary does not know would be counted as optional.
if ! findings="$(jq -Rsce '
    explode as $text
    | [range($text | length) | select($text[.] == 125)] as $closes
    | [range($text | length) | select($text[.] == 123)
        | select(. as $at | first(range($at + 1; $text | length) | $text[.] | select(. != 32 and . != 9 and . != 10 and . != 13))
            | . == 34 or . == 125) as $start
        | first($closes[] | select(. > $start) as $end
            | $text[$start:$end + 1] | implode | try fromjson catch empty | objects
            | {start: $start, end: $end, value: .})]
    | reduce .[] as $object ([]; if length > 0 and $object.start < last.end then . else . + [$object] end)
    | [.[].value.findings | arrays | select(all(.[];
        type == "object"
        and (.severity == "blocking" or .severity == "optional")
        and (.file | type) == "string"
        and ((.line | type) == "number" or .line == null)
        and (.message | type) == "string"))]
    | last // empty' "$findings_file" 2> /dev/null)" || [ -z "$findings" ]; then
  echo "The $reviewer review did not report its findings in the shape .github/review-findings.schema.json asks for. It answered:" >&2
  head -c 4000 "$findings_file" >&2
  echo >&2
  exit 1
fi

blocking="$(jq '[.[] | select(.severity == "blocking")] | length' <<< "$findings")"
optional="$(jq '[.[] | select(.severity != "blocking")] | length' <<< "$findings")"
summary="**Cross-model review by $reviewer:** $blocking blocking, $optional optional."
if [ $((blocking + optional)) -eq 0 ]; then
  summary="$summary

No findings."
fi

# Findings with a line go on that line; the rest, or all of them when GitHub refuses a line, go in the body.
listed() {
  jq -r "$1"' | "- **\(.severity)** `\(.file)\(if .line then ":\(.line)" else "" end)`: \(.message)"' <<< "$findings"
}
review() {
  gh api -X POST "repos/$repo/pulls/$pr/reviews" --input "$1" > /dev/null
}
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
payload="$work/review.json"

# The summary, then the findings the selector picks as a list, if any.
body() {
  local list
  list="$(listed "$1")" || return 1
  if [ -n "$list" ]; then
    printf '%s\n\n%s' "$summary" "$list"
  else
    printf '%s' "$summary"
  fi
}

general="$(body '.[] | select(.line == null)')"
jq -n --arg body "$general" --argjson findings "$findings" '{
  event: "COMMENT",
  body: $body,
  comments: [$findings[] | select(.line != null) | {path: .file, line: .line, side: "RIGHT", body: "**\(.severity)**: \(.message)"}]
}' > "$payload"

if ! review "$payload" 2> "$work/refused"; then
  echo "GitHub refused the line comments ($(head -c 300 "$work/refused")); posting every finding in the review body." >&2
  everything="$(body '.[]')"
  jq -n --arg body "$everything" '{event: "COMMENT", body: $body}' > "$payload"
  review "$payload"
fi

echo "$summary"
if [ "$blocking" -gt 0 ]; then
  echo "$blocking blocking finding(s): the review fails until they are resolved." >&2
  exit 1
fi

