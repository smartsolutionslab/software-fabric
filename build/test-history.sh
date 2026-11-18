#!/usr/bin/env bash
# Prints the test-time history with this run's times added: one line per class that ran this time,
# <assembly><tab><Namespace.Class><tab><parallel|serial><tab><seconds>...<tab><load>...<tab><parts>..., this run's
# first and at most five runs' in all, so build/test-split.sh can balance the shards by a median rather than one run's
# times. Each run's load and parts (build/tests-all-ran.sh) stay beside its seconds, in the same order: they are what
# build/test-split.sh learns a class's slowdown beside others and its setup per part from (#757). A class that did not
# run this time is dropped with its history. The build job keeps the history in the Actions cache for the next run.
#
# A class whose median is over the target, 15 minutes, cannot fit in a shard of that length, so build/test-split.sh
# runs it in parts, by test method (#738): for such a class, the history also keeps each of its methods' times, in
# lines of the same shape named <Namespace.Class>#<Method>. Only for such a class, so the history stays a few KB.
# Usage: build/test-history.sh <history, which need not exist> <this run's times, from build/tests-all-ran.sh --times>
set -euo pipefail
export LC_ALL=C

history="$1"
times="$2"
[ -f "$history" ] || history=/dev/null

awk -F '\t' '
  BEGIN { runs = 5; target = 15 * 60 }
  function median(times,    count, values, i, j, value) {
    count = split(times, values, " ")
    for (i = 2; i <= count; i++) {
      value = values[i] + 0
      for (j = i - 1; j >= 1 && values[j] + 0 > value; j--) { values[j + 1] = values[j] }
      values[j + 1] = value
    }
    return count % 2 ? values[(count + 1) / 2] + 0 : (values[count / 2] + values[count / 2 + 1]) / 2
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
    if (index($2, "#") == 0) { over[key] = median(entries[1]) > target }
  }
  END {
    for (key in kept) {
      class = key; sub(/#.*/, "", class)
      if (key == class || over[class]) { print key "\t" kept[key] }
    }
  }
' "$history" "$times" | sort
