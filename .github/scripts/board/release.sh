#!/usr/bin/env bash
# Gives a claimed issue back (board protocol, docs/adr): its branch is parked for the next agent, its status returns to Todo.
# Usage: release.sh <issue> <reason>
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

issue="$1"
reason="$2"

board_park "$issue" "Released: $reason" || { echo "Parking #$issue's branch failed; it is still claimed." >&2; exit 1; }
board_unclaim "$issue"
board_status "$issue" Todo
board_executor "$issue" ""
board_comment "$issue" "Released: $reason
The work so far stays on \`$(board_branch_for "$issue")\`; the next agent resumes it."

