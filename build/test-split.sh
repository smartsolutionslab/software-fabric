#!/usr/bin/env bash
# Prints the test shards for this run, in build/test-shards.txt's format (#711): as many lines as that file has, each
# naming the Fabric.Packs.Tests classes one shard runs; the shard after them runs everything they do not name. The
# classes are balanced by the median of the times build/test-history.sh kept for them, so one slow runner does not
# move them, and new heavy classes are spread from the run after they first ran without anyone editing a file.
#
# The model is the one the file was balanced by by hand (#710): a shard takes as long as its parallel classes, which
# run four at a time on the runner's four cores but never take less than the longest of them, plus its serial classes
# (a collection that runs alone, such as Fixed ports), which run one after another. Every class outside
# Fabric.Packs.Tests, and every class with no time yet, stays in the last shard. The classes are assigned slowest first
# to the shard they lengthen least, then classes are moved or swapped off the slowest shard while that shortens it.
#
# Without a history, or when the times would leave a shard empty, the split is build/test-shards.txt as it stands. A
# class whose source file is gone is not named: a shard naming only such classes would run no test and fail.
# Prints each shard's estimate to stderr.
# Usage: build/test-split.sh <history> <build/test-shards.txt>
set -euo pipefail
export LC_ALL=C

history="$1"
fallback="$2"
repository="$(cd "$(dirname "$0")/.." && pwd)"
named="$(grep -cvE '^[[:space:]]*(#|$)' "$fallback" || true)"

if [ ! -s "$history" ]; then
  echo "No test times are cached; the split is $fallback." >&2
  cat "$fallback"
  exit 0
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
find "$repository/tests/Fabric.Packs.Tests" -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -printf '%f\n' | sed 's/\.cs$//' > "$work/sources"

status=0
awk -F '\t' -v named="$named" -v estimates="$work/estimates" -v shards="$work/split" '
  function median(times,    count, values, i, j, value) {
    count = split(times, values, " ")
    for (i = 2; i <= count; i++) {
      value = values[i] + 0
      for (j = i - 1; j >= 1 && values[j] + 0 > value; j--) { values[j + 1] = values[j] }
      values[j + 1] = value
    }
    return count % 2 ? values[(count + 1) / 2] + 0 : (values[count / 2] + values[count / 2 + 1]) / 2
  }
  # The seconds bin b takes with item add (0 for none) in it and item drop (0 for none) out of it.
  function cost(b, add, drop,    k, item, parallel, longest, serial) {
    parallel = 0; longest = 0; serial = 0
    for (k = 1; k <= members[b]; k++) {
      item = member[b, k]
      if (item != drop) { if (alone[item]) { serial += seconds[item] } else { parallel += seconds[item]; if (seconds[item] > longest) { longest = seconds[item] } } }
    }
    if (add) { if (alone[add]) { serial += seconds[add] } else { parallel += seconds[add]; if (seconds[add] > longest) { longest = seconds[add] } } }
    parallelTime = parallel / 4 > longest ? parallel / 4 : longest
    serialTime = serial
    return parallelTime + serialTime
  }
  function put(b, item) { member[b, ++members[b]] = item; bin[item] = b }
  function take(b, item,    k, kept) {
    kept = 0
    for (k = 1; k <= members[b]; k++) { if (member[b, k] != item) { member[b, ++kept] = member[b, k] } }
    members[b] = kept
  }
  function larger(a, b) { return a > b ? a : b }
  FILENAME == ARGV[1] { source[$1] = 1; next }
  {
    class = $2; sub(/.*\./, "", class)
    if ($1 == "Fabric.Packs.Tests" && !(class in source)) { next }
    count++
    name[count] = $2; alone[count] = $3 == "serial"; seconds[count] = median($4); movable[count] = $1 == "Fabric.Packs.Tests"
  }
  END {
    bins = named + 1
    # Slowest first, by name among equals; an item number is then its rank.
    for (i = 1; i <= count; i++) { order[i] = i }
    for (i = 2; i <= count; i++) {
      item = order[i]
      for (j = i - 1; j >= 1 && (seconds[order[j]] < seconds[item] || (seconds[order[j]] == seconds[item] && name[order[j]] > name[item])); j--) { order[j + 1] = order[j] }
      order[j + 1] = item
    }
    for (i = 1; i <= count; i++) { item = order[i]; n[i] = name[item]; a[i] = alone[item]; s[i] = seconds[item]; m[i] = movable[item] }
    for (i = 1; i <= count; i++) { name[i] = n[i]; alone[i] = a[i]; seconds[i] = s[i]; movable[i] = m[i] }

    for (i = 1; i <= count; i++) { if (!movable[i]) { put(bins, i) } }
    for (i = 1; i <= count; i++) {
      if (!movable[i]) { continue }
      best = 1; least = cost(1, i, 0)
      for (b = 2; b <= bins; b++) { value = cost(b, i, 0); if (value < least) { least = value; best = b } }
      put(best, i)
    }

    for (round = 1; round <= 10000; round++) {
      slowest = 1; current = cost(1, 0, 0)
      for (b = 2; b <= bins; b++) { value = cost(b, 0, 0); if (value > current) { current = value; slowest = b } }
      target = current - 0.05; found = 0
      for (k = 1; k <= members[slowest]; k++) {
        i = member[slowest, k]
        if (!movable[i]) { continue }
        for (b = 1; b <= bins; b++) {
          if (b == slowest) { continue }
          value = larger(cost(slowest, 0, i), cost(b, i, 0))
          if (value < target) { target = value; found = 1; moved = i; other = b; swapped = 0 }
          for (l = 1; l <= members[b]; l++) {
            j = member[b, l]
            if (!movable[j]) { continue }
            value = larger(cost(slowest, j, i), cost(b, i, j))
            if (value < target) { target = value; found = 1; moved = i; other = b; swapped = j }
          }
        }
      }
      if (!found) { break }
      take(slowest, moved); put(other, moved)
      if (swapped) { take(other, swapped); put(slowest, swapped) }
    }

    for (b = 1; b <= bins; b++) {
      total = cost(b, 0, 0)
      printf "shard %2d: est %5.1f min  (parallel %5.1f, serial %5.1f, classes %d%s)\n", b, total / 60, parallelTime / 60, serialTime / 60, members[b], b == bins ? ", the rest" : "" > estimates
      if (b < bins && members[b] == 0) { empty = 1 }
    }
    if (empty) { exit 3 }
    print "# The shards of this run, balanced by build/test-split.sh on the median of each class'"'"'s recent times." > shards
    for (b = 1; b < bins; b++) {
      line = ""
      for (i = 1; i <= count; i++) { if (bin[i] == b) { line = line (line == "" ? "" : " ") name[i] } }
      print line > shards
    }
  }
' "$work/sources" "$history" || status=$?
if [ "$status" -eq 3 ]; then
  echo "The times would leave a shard empty; the split is $fallback." >&2
  cat "$fallback"
  exit 0
elif [ "$status" -ne 0 ]; then
  exit "$status"
fi

cat "$work/estimates" >&2
cat "$work/split"
