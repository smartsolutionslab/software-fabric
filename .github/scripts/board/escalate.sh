#!/usr/bin/env bash
# Hands an issue to a developer (board protocol, docs/adr).
# Usage: escalate.sh <issue> <needs:human|needs:decision|agent:blocked> <question or verbatim failure>
# The developer is the lane's owner (docs/owners.yml) with the fewest open assignments, below the WIP limit.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

issue="$1"
label="$2"
message="$3"

case "$label" in
  needs:human | needs:decision | agent:blocked) ;;
  *) echo "Unknown escalation label '$label'." >&2; exit 2 ;;
esac

lane="$(board_issue "$issue" | jq -r '[.labels[].name | select(startswith("lane:"))][0] // "" | ltrimstr("lane:")')"
owners_file="$board_root/docs/owners.yml"
limit="$(yq -r '.["wip-limit"] // 3' "$owners_file")"

if [[ ! "$lane" =~ ^[a-z0-9-]*$ ]]; then
  echo "Lane label 'lane:$lane' is not a lane name." >&2
  exit 2
fi

owner=""
fewest=""
if [ -n "$lane" ]; then
  while read -r login; do
    [ -n "$login" ] || continue
    open="$(board_issues "assignee=$(board_uri "$login")" | jq length)"
    if [ "$open" -lt "$limit" ] && { [ -z "$fewest" ] || [ "$open" -lt "$fewest" ]; }; then
      owner="$login"
      fewest="$open"
    fi
  done < <(yq -r "(.lanes[\"$lane\"].owners // [])[]" "$owners_file")
fi

# The escalation label holds the issue before the branch is parked and before agent:claimed stops holding it: a
# next.sh already past its list rechecks the issue, not agent:claimed, so either other order lets it take the issue.
board_add_label "$issue" "$label"
if ! board_park "$issue" "Escalated ($label)."; then
  board_remove_label "$issue" "$label"
  echo "Parking #$issue's branch failed; not escalated." >&2
  exit 1
fi
board_unclaim "$issue"
board_status "$issue" Blocked
if [ -n "$owner" ]; then
  board_assign "$issue" "$owner"
  board_comment "$issue" "@$owner, this needs you ($label):

$message

Unassign yourself once answered; the issue then returns to Todo and the next agent resumes on its branch."
else
  board_comment "$issue" "This needs a developer ($label), but no owner of lane '${lane:-none}' in docs/owners.yml has capacity:

$message"
fi

