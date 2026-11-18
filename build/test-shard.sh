#!/usr/bin/env bash
# Runs one shard of the solution's tests (ci.yml's tests job). Shard n runs the Fabric.Packs.Tests classes on the nth
# line of the split, build/test-split.sh's in CI and build/test-shards.txt unless one is given; the shard after the last
# line runs the whole solution except the classes the split names, so a new class or test project runs without anyone
# assigning it. build/tests-all-ran.sh then checks, from the xUnit XML reports each shard writes, that every test method
# ran.
# Needs a Release build of the solution, and what the tests need: Docker, Node 22 with pnpm, the Aspire CLI bundle and
# Playwright's Chromium.
# Usage: build/test-shard.sh <shard> <results directory> [<split>]
set -euo pipefail

shard="$1"
results="${2:-}"
repository="$(cd "$(dirname "$0")/.." && pwd)"
split="${3:-$repository/build/test-shards.txt}"

shards=()
while read -r line; do
  shards+=("$line")
done < <(grep -vE '^[[:space:]]*(#|$)' "$split")
rest=$((${#shards[@]} + 1))

if ! [[ "$shard" =~ ^[1-9][0-9]*$ ]] || [ "$shard" -gt "$rest" ]; then
  echo "There is no shard $shard: the split names ${#shards[@]}, and shard $rest runs the rest." >&2
  exit 1
fi
if [ -z "$results" ]; then
  echo "Usage: build/test-shard.sh <shard> <results directory> [<split>]" >&2
  exit 1
fi

cd "$repository"
arguments=(-c Release --no-build --results-directory "$results" --report-xunit-xml)
if [ "$shard" -lt "$rest" ]; then
  for class in ${shards[$((shard - 1))]}; do
    arguments+=(--filter-class "$class")
  done
  dotnet test --project tests/Fabric.Packs.Tests/Fabric.Packs.Tests.csproj "${arguments[@]}"
else
  for line in "${shards[@]}"; do
    for class in $line; do
      arguments+=(--filter-not-class "$class")
    done
  done
  dotnet test --solution Fabric.slnx "${arguments[@]}"
fi
