#!/usr/bin/env bash
# Prints the test-time history with this run's times added: one line per class that ran this time,
# <assembly><tab><Namespace.Class><tab><parallel|serial><tab><seconds>...<tab><load>...<tab><parts>..., this run's
# first and at most five runs' in all, so build/test-split.sh can balance the shards by a median rather than one run's
# times. Each run's load and parts (build/tests-all-ran.sh) stay beside its seconds, in the same order: they are what
# build/test-split.sh learns a class's slowdown beside others and its setup per part from (#757). A class that did not
# run this time is dropped with its history. The build job keeps the history in the Actions cache for the next run.
#
# A class that took longer than the target, 15 minutes, in any run kept may not fit in a shard of that length, so
# build/test-split.sh may run it in parts, by test method (#738, #757): for such a class, the history also keeps each of
# its methods' times, in lines of the same shape named <Namespace.Class>#<Method>. Only for such a class, so the history
# stays a few tens of KB.
# Usage: build/test-history.sh <history, which need not exist> <this run's times, from build/tests-all-ran.sh --times>
set -euo pipefail
export LC_ALL=C

history="$1"
times="$2"
[ -f "$history" ] || history=/dev/null

awk -F '\t' '
  BEGIN { runs = 5; target = 15 * 60 }
  function longest(times,    count, values, i, most) {
    count = split(times, values, " ")
    most = 0
    for (i = 1; i <= count; i++) { if (values[i] + 0 > most) { most = values[i] + 0 } }
    return most
  }
  FILENAME == ARGV[1] { previous[$1 "\t" $2] = $4 "\t" $5 "\t" $6; next }
  {
    key = $1 "\t" $2
    split(previous[key], before, "\t")
    line = $3
    for (column = 1; column <= 3; column++) {
      count = split(before[column], values, " ")
      entries[column] = $(column + 3)
      for (i = 1; i <= count && i < runs; i++) { entries[column] = entries[column] " " values[i] }
      line = line "\t" entries[column]
    }
    kept[key] = line
    if (index($2, "#") == 0) { over[key] = longest(entries[1]) > target }
  }
  END {
    for (key in kept) {
      class = key; sub(/#.*/, "", class)
      if (key == class || over[class]) { print key "\t" kept[key] }
    }
  }
' "$history" "$times" | sort
