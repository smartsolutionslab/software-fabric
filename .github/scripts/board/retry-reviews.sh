#!/usr/bin/env bash
# Re-runs the cross-model reviews that Copilot's usage limit postponed (board protocol, docs/adr).
# Usage: retry-reviews.sh
# A review is postponed at a pull request's current head when that commit's status cross-model-review/verdict is
# pending and says "Postponed"; its link is the review's run, which is re-run whole once it has finished. A head that
# moved has a run of its own. Nothing is reviewed here, and a run that cannot be re-run is reported after the others.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

pulls="$(gh api "repos/$repo/pulls?state=open&per_page=100" --paginate | jq -rs 'add // [] | .[] | "\(.number) \(.head.sha)"')"
failed=0
while read -r pr head; do
  [ -n "$pr" ] || continue
  status="$(gh api "repos/$repo/commits/$head/status")" || { failed=1; continue; }
  url="$(jq -r '.statuses[] | select(.context == "cross-model-review/verdict" and .state == "pending"
    and (.description // "" | startswith("Postponed"))) | .target_url' <<< "$status")"
  [ -n "$url" ] || continue
  run="${url##*/actions/runs/}"
  run="${run%%/*}"
  if [[ ! "$run" =~ ^[0-9]+$ ]]; then
    echo "#$pr: the postponed review links to no run ($url)." >&2
    failed=1
    continue
  fi
  progress="$(gh api "repos/$repo/actions/runs/$run" | jq -r .status)" || { failed=1; continue; }
  if [ "$progress" != completed ]; then
    echo "#$pr: the review's run $run is $progress; it is left to finish."
    continue
  fi
  if gh api -X POST "repos/$repo/actions/runs/$run/rerun" > /dev/null; then
    echo "#$pr: re-running the postponed review (run $run)."
  else
    failed=1
  fi
done <<< "$pulls"
exit "$failed"

