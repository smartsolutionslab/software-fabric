# Shared by the board scripts (board protocol, docs/adr). Source it; do not run it.
# Bash ignores errexit inside a function called as a condition (`if f`, `f || ...`), and callers do call these that
# way, so every step that can fail inside a function is checked explicitly.
# Talks to GitHub through `git` (branches) and `gh api` with REST (issues, labels, comments, pull requests): Claude
# Code's cloud sessions refuse GraphQL, which most other gh subcommands use, and branch writes through the API.
# Board fields are the exception (GitHub Projects have no REST API) and stay optional.

set -euo pipefail

board_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"

# Settings: .github/board.env, unless the environment already sets a value.
while IFS='=' read -r key value; do
  [[ "$key" =~ ^BOARD_[A-Z_]+$ ]] || continue
  if [ -z "${!key:-}" ]; then
    export "$key=$value"
  fi
done < "$board_root/.github/board.env"

repo="${GITHUB_REPOSITORY:-}"
if [ -z "$repo" ]; then
  repo="$(git -C "$board_root" remote get-url origin | sed -E 's#\.git$##; s#^.*github\.com[:/]##')"
fi

board_now() {
  echo "${BOARD_NOW:-$(date -u +%s)}"
}

board_git() {
  git -C "$board_root" "$@"
}

board_default_branch() {
  board_git ls-remote --symref origin HEAD | sed -nE 's#^ref: refs/heads/([^[:space:]]+)[[:space:]]+HEAD$#\1#p'
}

# The issue's branch on the remote, or nothing. It is agent/<issue> and nothing more: a name taken from anything
# that can change, such as the title, would let two agents that each saw no branch push two different locks.
board_branch_for() {
  board_git ls-remote --heads origin "refs/heads/agent/$1" | awk -v ref="refs/heads/agent/$1" '$2 == ref { print "agent/" substr(ref, 18); exit }'
}

# Fetches a remote branch and prints its last commit.
board_fetch() {
  board_git fetch --quiet origin "refs/heads/$1" && board_git rev-parse FETCH_HEAD
}

# A branch is parked when its last commit gives the issue back.
board_parked() {
  [[ "$(board_git log -1 --format=%s "$1")" == "chore(board): release #"* ]]
}

board_executor_of() {
  board_git log -1 --format='%(trailers:key=Agent-Executor,valueonly)' "$1" | sed '/^$/d' | head -n1
}

# The branch is the lock, and it moves only by `git push` (cloud sessions refuse branch writes through the API).
# Every board step is an empty commit on top of what the step saw, so a push that finds the branch moved is not a
# fast-forward and is rejected: of two agents taking the same step, one wins.
# Each commit carries a nonce: two agents with the same identity, parent and second would otherwise build the same
# commit, and the second push would report the branch "up to date" and succeed. An up-to-date push still counts as lost.
# Returns 0 when pushed, 2 when another agent moved the branch first, 1 on any other failure (printed).
board_mark() {
  local branch="$1" base="$2" message="$3" commit output nonce
  nonce="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')" || return 1
  message="$(git interpret-trailers --trailer "Board-Nonce: $nonce" <<< "$message")" || return 1
  commit="$(
    export GIT_AUTHOR_NAME="${GIT_AUTHOR_NAME:-$(board_git config user.name || echo board)}"
    export GIT_AUTHOR_EMAIL="${GIT_AUTHOR_EMAIL:-$(board_git config user.email || echo board@users.noreply.github.com)}"
    export GIT_COMMITTER_NAME="${GIT_COMMITTER_NAME:-$GIT_AUTHOR_NAME}" GIT_COMMITTER_EMAIL="${GIT_COMMITTER_EMAIL:-$GIT_AUTHOR_EMAIL}"
    if [ -n "${BOARD_NOW:-}" ]; then
      export GIT_AUTHOR_DATE="@$BOARD_NOW +0000" GIT_COMMITTER_DATE="@$BOARD_NOW +0000"
    fi
    board_git commit-tree "$base^{tree}" -p "$base" -m "$message"
  )" || return 1
  if output="$(board_git push --porcelain origin "$commit:refs/heads/$branch" 2>&1)"; then
    grep -q $'^=\t' <<< "$output" && return 2
    return 0
  fi
  if grep -q $'\t\[rejected\]' <<< "$output"; then
    return 2
  fi
  echo "$output" >&2
  return 1
}

# Parks the issue's branch for the next agent: a release commit on top, signed with the claimer's executor.
# Given the head a decision was made on, parks that head only: if the branch has moved since, nothing changes (2).
board_park() {
  local issue="$1" reason="$2" head="${3:-}" branch executor
  branch="$(board_branch_for "$issue")" || return 1
  [ -n "$branch" ] || return 0
  if [ -z "$head" ]; then
    head="$(board_fetch "$branch")" || return 1
  fi
  board_parked "$head" && return 0
  executor="$(board_executor_of "$head")" || return 1
  board_mark "$branch" "$head" "chore(board): release #$issue

$reason

Agent-Executor: ${executor:-board}"
}

board_comment() {
  gh api -X POST "repos/$repo/issues/$1/comments" -f "body=$2" > /dev/null
}

board_uri() {
  jq -rn --arg value "$1" '$value | @uri'
}

# Open issues matching a query string (labels=..., assignee=...), every page, pull requests left out.
board_issues() {
  gh api "repos/$repo/issues?state=open&per_page=100&$1" --paginate | jq -s 'add // [] | map(select(has("pull_request") | not))'
}

board_issue() {
  gh api "repos/$repo/issues/$1"
}

# Work an agent may take in lane $lane: open, agent:ready, no assignee, neither blocked nor escalated (needs:*), as jq
# over one issue. agent:claimed is the callers' to judge: the branch is the lock, and the label follows it.
board_eligible_jq='.state == "open" and (.assignees | length) == 0
  and ([.labels[].name] | index("agent:ready") != null and index("lane:" + $lane) != null
       and (any(. == "agent:blocked" or startswith("needs:")) | not))'

# Returns 0 when the issue is eligible in the lane now, 2 when it is not, 1 when it could not be read.
board_eligible() {
  local state
  state="$(board_issue "$1")" || return 1
  jq -e --arg lane "$2" "$board_eligible_jq" <<< "$state" > /dev/null || return 2
}

board_add_label() {
  gh api -X POST "repos/$repo/issues/$1/labels" -f "labels[]=$2" > /dev/null
}

board_remove_label() {
  gh api -X DELETE "repos/$repo/issues/$1/labels/$(board_uri "$2")" > /dev/null
}

# agent:claimed shows people (and agents reading labels) that an issue is taken; the branch stays the lock.
# An issue that cannot be read is a failure, not an issue without the label: a parked branch whose issue keeps
# agent:claimed is skipped by next.sh and the janitor alike, for good.
board_unclaim() {
  local state
  state="$(board_issue "$1")" || return 1
  if jq -e 'any(.labels[]; .name == "agent:claimed")' <<< "$state" > /dev/null; then
    board_remove_label "$1" agent:claimed
  fi
}

board_assign() {
  gh api -X POST "repos/$repo/issues/$1/assignees" -f "assignees[]=$2" > /dev/null
}

# Board fields (Status, Executor) live on the GitHub Project named in .github/board.env.
# Without one, the labels and comments still carry the protocol.
board_project_configured() {
  [ -n "${BOARD_PROJECT_NUMBER:-}" ] && [ -n "${BOARD_PROJECT_OWNER:-}" ]
}

board_item() {
  local issue="$1" url item
  url="https://github.com/$repo/issues/$issue"
  item="$(gh project item-list "$BOARD_PROJECT_NUMBER" --owner "$BOARD_PROJECT_OWNER" --format json --limit 1000 \
    | jq -r --arg url "$url" '.items[] | select(.content.url == $url) | .id' | head -n1)" || return 1
  if [ -z "$item" ]; then
    item="$(gh project item-add "$BOARD_PROJECT_NUMBER" --owner "$BOARD_PROJECT_OWNER" --url "$url" --format json | jq -r .id)" || return 1
  fi
  echo "$item"
}

board_field() {
  local issue="$1" field="$2" value="$3" project fields field_id item
  board_project_configured || return 0
  project="$(gh project view "$BOARD_PROJECT_NUMBER" --owner "$BOARD_PROJECT_OWNER" --format json | jq -r .id)" || return 1
  fields="$(gh project field-list "$BOARD_PROJECT_NUMBER" --owner "$BOARD_PROJECT_OWNER" --format json)" || return 1
  field_id="$(jq -r --arg name "$field" '.fields[] | select(.name == $name) | .id' <<< "$fields")" || return 1
  if [ -z "$field_id" ]; then
    echo "The board has no field '$field'; run .github/scripts/board/setup.sh." >&2
    return 1
  fi
  item="$(board_item "$issue")" || return 1
  if jq -e --arg name "$field" '.fields[] | select(.name == $name) | has("options")' <<< "$fields" > /dev/null; then
    local option
    option="$(jq -r --arg name "$field" --arg value "$value" '.fields[] | select(.name == $name) | .options[] | select(.name == $value) | .id' <<< "$fields")" || return 1
    if [ -z "$option" ]; then
      echo "The board's '$field' field has no option '$value'; run .github/scripts/board/setup.sh." >&2
      return 1
    fi
    gh project item-edit --id "$item" --project-id "$project" --field-id "$field_id" --single-select-option-id "$option" > /dev/null
  elif [ -z "$value" ]; then
    gh project item-edit --id "$item" --project-id "$project" --field-id "$field_id" --clear > /dev/null
  else
    gh project item-edit --id "$item" --project-id "$project" --field-id "$field_id" --text "$value" > /dev/null
  fi
}

board_status() {
  board_field "$1" Status "$2"
}

board_executor() {
  board_field "$1" Executor "$2"
}
