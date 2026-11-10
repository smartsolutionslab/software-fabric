#!/usr/bin/env bash
# Returns lapsed claims to Todo (board protocol, docs/adr). Run on a schedule.
# A claim lapses when its agent/ branch has no open pull request and no commit (the claim is one) within the lease.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

now="$(board_now)"
lease=$((BOARD_LEASE_HOURS * 3600))

# The board's items, read once per run and only when a parked branch needs them: a Status read early only decides
# whether to look again; the branch is fetched afresh before anything is written.
items=""
items_read=false
read_items() {
  [ "$items_read" = false ] || return 0
  if board_project_configured; then
    items="$(gh project item-list "$BOARD_PROJECT_NUMBER" --owner "$BOARD_PROJECT_OWNER" --format json --limit 1000)" || return 1
  fi
  items_read=true
}

# The issue's Status on the board, or nothing without a board.
status_of() {
  [ -n "$items" ] || return 0
  jq -r --arg url "https://github.com/$repo/issues/$1" '[.items[] | select(.content.url == $url) | .status // empty][0] // empty' <<< "$items"
}

board_git ls-remote --heads origin | awk 'index($2, "refs/heads/agent/") == 1 { sub("refs/heads/", "", $2); print $2 }' | while read -r branch; do
  issue="${branch#agent/}"
  # A lock is agent/<issue>, or agent/<issue>-<slug> from before; anything else under agent/ is not a claim.
  [[ "$issue" =~ ^[0-9]+(-.*)?$ ]] || continue
  issue="${issue%%-*}"
  # A stray agent/ branch without an open issue is not a claim: a merged pull request leaves its branch behind.
  [ "$(board_issue "$issue" 2>/dev/null | jq -r .state)" = open ] || continue

  # Unknown is not "none": a failed lookup leaves the branch alone, as an open pull request would.
  pulls="$(gh api "repos/$repo/pulls?state=open&head=$(board_uri "${repo%%/*}:$branch")" | jq length)" || continue
  [ "$pulls" -eq 0 ] || continue

  head="$(board_fetch "$branch")" || continue
  if board_parked "$head"; then
    # Parked, but still labelled agent:claimed: a release, escalation or lapse stopped half-way. Neither next.sh nor
    # this loop would ever take the issue again, so finish it. Parked, but shown as Claimed: the same, or a stale write
    # marked an ended claim again; labels and fields have no compare-and-swap, so only the branch is a lock and the
    # rest is repaired here. An escalation keeps its executor for the developer and shows Blocked; anything else is
    # back in Todo. A claim pushed meanwhile has moved the branch: its label goes back on, and the fields are its own.
    issue_state="$(board_issue "$issue")" || continue
    labelled=false
    if jq -e 'any(.labels[]; .name == "agent:claimed")' <<< "$issue_state" > /dev/null; then
      labelled=true
    fi
    escalated=false
    if jq -e 'any(.labels[]; .name == "agent:blocked" or (.name | startswith("needs:")))' <<< "$issue_state" > /dev/null; then
      escalated=true
    fi
    read_items || continue
    status="$(status_of "$issue")" || continue
    if [ "$labelled" = true ] || [ "$status" = Claimed ]; then
      if [ "$labelled" = true ]; then
        board_unclaim "$issue" || continue
      fi
      head="$(board_fetch "$branch")" || continue
      if ! board_parked "$head"; then
        if [ "$labelled" = true ]; then
          board_add_label "$issue" agent:claimed
        fi
        continue
      fi
      if [ "$escalated" = false ]; then
        board_status "$issue" Todo
        board_executor "$issue" ""
      elif [ "$status" = Claimed ]; then
        board_status "$issue" Blocked
      fi
      echo "Finished giving back #$issue."
    fi
    continue
  fi

  # The claim is a commit too, so the branch's last commit is the last sign of life.
  if [ $((now - $(board_git log -1 --format=%ct "$head"))) -gt "$lease" ]; then
    # Parks the head whose age was checked; a push since then means the claim is alive after all.
    board_park "$issue" "The claim lapsed: no push for ${BOARD_LEASE_HOURS} h and no open pull request." "$head" || continue
    board_unclaim "$issue"
    # A claim that took the parked branch meanwhile keeps its label and its board fields.
    head="$(board_fetch "$branch")" || continue
    if ! board_parked "$head"; then
      board_add_label "$issue" agent:claimed
      continue
    fi
    board_status "$issue" Todo
    board_executor "$issue" ""
    board_comment "$issue" "The claim on \`$branch\` lapsed: no push for ${BOARD_LEASE_HOURS} h and no open pull request. Back to Todo; the work so far stays on the branch."
    echo "Returned #$issue to Todo."
  fi
done

