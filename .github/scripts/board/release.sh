#!/usr/bin/env bash
# Gives a claimed issue back (board protocol, docs/adr): its branch is parked for the next agent, its status returns to Todo.
# Usage: release.sh <issue> <reason>
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

issue="$1"
reason="$2"

# Every write to the issue happens while no claim can be under way, so a claim that takes the branch afterwards is the
# last to write. A claim needs a parked branch and, from a fresh list, an issue without agent:claimed: the board fields
# go first, then the label, and the branch is parked last, at the head read here and never at one read later, which
# could be the next agent's claim. A head that is parked already, from a release or lapse that stopped half-way, is
# left as it is.
branch="$(board_branch_for "$issue")"
head=""
if [ -n "$branch" ]; then
  head="$(board_fetch "$branch")"
fi
labelled="$(board_issue "$issue" | jq 'any(.labels[]; .name == "agent:claimed")')"
board_status "$issue" Todo
board_executor "$issue" ""
if [ "$labelled" = true ]; then
  board_unclaim "$issue"
fi
outcome=0
board_park "$issue" "Released: $reason" "$head" || outcome=$?
# The branch moved since it was read: another agent parked it and may have claimed it, possibly before the writes above.
# Nothing here is this agent's to restore. A claim that holds the branch now gets its label and fields written again,
# with the values it writes itself, so whichever write lands last, they stand.
if [ "$outcome" -eq 2 ]; then
  head="$(board_fetch "$branch")"
  if ! board_parked "$head"; then
    board_add_label "$issue" agent:claimed
    board_status "$issue" Claimed
    board_executor "$issue" "$(board_executor_of "$head")"
  fi
  echo "#$issue's branch moved since it was read; another agent parked or claimed it, so this release changes nothing more." >&2
  exit 1
fi
if [ "$outcome" -ne 0 ]; then
  if [ "$labelled" = true ]; then
    board_add_label "$issue" agent:claimed
  fi
  board_status "$issue" Claimed
  board_executor "$issue" "$(board_executor_of "$head")"
  echo "Parking #$issue's branch failed; it is still claimed." >&2
  exit 1
fi
board_comment "$issue" "Released: $reason
The work so far stays on \`$branch\`; the next agent resumes it."

