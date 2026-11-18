#!/usr/bin/env bash
# Runs one shard of the solution's tests (ci.yml's tests job). Shard n runs the Fabric.Packs.Tests classes on the nth
# line of the split, build/test-split.sh's in CI and build/test-shards.txt unless one is given; the shard after the last
# line runs the whole solution except the classes the split names, so a new class or test project runs without anyone
# assigning it. A name <Namespace.Class>#<Method> is one method of a class the split runs in parts (#738); xUnit ANDs a
# class filter with a method filter, so a shard that runs such a part names its whole classes by method too
# (<Namespace.Class>.*). build/tests-all-ran.sh then checks, from the xUnit XML reports each shard writes, that every
# test method ran in exactly one shard.
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
  line="${shards[$((shard - 1))]}"
  for name in $line; do
    if [[ "$line" == *"#"* ]]; then
      [[ "$name" == *"#"* ]] && arguments+=(--filter-method "${name/\#/.}") || arguments+=(--filter-method "$name.*")
    else
      arguments+=(--filter-class "$name")
    fi
  done
  dotnet test --project tests/Fabric.Packs.Tests/Fabric.Packs.Tests.csproj "${arguments[@]}"
else
  # Excluding filters of both kinds leave out what either names.
  for line in "${shards[@]}"; do
    for name in $line; do
      [[ "$name" == *"#"* ]] && arguments+=(--filter-not-method "${name/\#/.}") || arguments+=(--filter-not-class "$name")
    done
  done
  dotnet test --solution Fabric.slnx "${arguments[@]}"
fi
