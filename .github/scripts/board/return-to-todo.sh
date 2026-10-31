#!/usr/bin/env bash
# Returns an escalated issue to the agents once its developer has unassigned themselves (board protocol, docs/adr).
# Usage: return-to-todo.sh <issue>
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

issue="$1"

state="$(board_issue "$issue")"
if [ "$(jq '.assignees | length' <<< "$state")" -gt 0 ]; then
  echo "#$issue still has an assignee; it stays with them."
  exit 0
fi

escalations="$(jq -r '[.labels[].name | select(. == "needs:human" or . == "needs:decision" or . == "agent:blocked")] | join(",")' <<< "$state")"
if [ -z "$escalations" ]; then
  echo "#$issue was not escalated; nothing to do."
  exit 0
fi

for label in ${escalations//,/ }; do
  board_remove_label "$issue" "$label"
done
board_add_label "$issue" agent:ready
board_status "$issue" Todo
board_comment "$issue" "Answered and unassigned: back to Todo for the agents (was $escalations)."

