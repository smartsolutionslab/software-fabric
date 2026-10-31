#!/usr/bin/env bash
# End-to-end tests against the running application (CI's e2e job; also runs locally, Docker needed):
# starts the AppHost as a developer does, waits for the web app, runs Playwright, stops the AppHost.
# Run it after `pnpm install` in web/. The AppHost builds what it starts.
set -euo pipefail
# Job control gives the AppHost its own process group, whose members can be found and signalled.
set -m

cd "$(dirname "$0")/../.."
root="$PWD"
app="http://localhost:5173/"
log="$(mktemp)"

# No input: a background process group that reads its terminal is stopped.
# Plain `dotnet run`: Aspire's CLI, which it hands the AppHost to, starts the Debug build whatever -c says.
dotnet run --project src/AppHost --launch-profile http < /dev/null > "$log" 2>&1 &
apphost=$!

stop() {
  # Ctrl+C to the AppHost itself, not through `dotnet run` and Aspire's CLI, which cut its shutdown short; it has
  # Aspire's orchestrator (DCP) remove the containers it started. DCP itself outlives the AppHost and may restart
  # the host and the Vite server, so once the containers are gone it is stopped along with anything still running.
  local host orchestrator kubeconfig controllers containers
  host="$(pgrep -g "$apphost" -f "$root/src/AppHost/bin/" || true)"
  orchestrator="$(pgrep -f -- "--monitor ${host:-none}( |\$)" | head -n1 || true)"
  kubeconfig="$(ps -o args= -p "${orchestrator:-0}" 2> /dev/null | sed -nE 's/.*--kubeconfig ([^ ]+).*/\1/p' || true)"
  controllers="$(pgrep -f -- "run-controllers --kubeconfig ${kubeconfig:-none}( |\$)" || true)"
  containers="$(for controller in $controllers; do pgrep -a -P "$controller" | sed -nE 's/.*--containerID ([0-9a-f]+).*/\1/p'; done)"

  if [ -n "$host" ]; then
    kill -INT "$host" 2> /dev/null || true
  else
    kill -INT -- "-$apphost" 2> /dev/null || true
  fi

  for _ in $(seq 90); do
    remaining=""
    pgrep -g "$apphost" > /dev/null && remaining=apphost
    for container in $containers; do
      docker container inspect "$container" > /dev/null 2>&1 && remaining=containers
    done
    [ -n "$remaining" ] || break
    sleep 1
  done

  kill -KILL -- "-$apphost" 2> /dev/null || true
  for controller in $controllers; do
    pkill -KILL -P "$controller" || true
  done
  [ -z "$kubeconfig" ] || pkill -KILL -f -- "--kubeconfig $kubeconfig" || true
  pkill -KILL -f "^[^ ]*$root/(src|web)/|^node $root/web/" || true
}

trap stop EXIT

fail() {
  echo "$1"
  echo "--- AppHost output"
  cat "$log"
  exit 1
}

for _ in $(seq 150); do
  curl -fs -o /dev/null --max-time 10 "$app" && break
  if ! kill -0 "$apphost" 2> /dev/null; then
    status=0
    wait "$apphost" || status=$?
    fail "The AppHost exited with $status before $app answered."
  fi
  sleep 2
done
curl -fs -o /dev/null --max-time 10 "$app" || fail "$app did not answer within five minutes."

(cd web/app && pnpm e2e) || fail "The end-to-end tests failed."
