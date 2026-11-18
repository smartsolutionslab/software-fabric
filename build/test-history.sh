#!/usr/bin/env bash
# Prints the test-time history with this run's times added: one line per class that ran this time,
# <assembly><tab><Namespace.Class><tab><parallel|serial><tab><seconds>..., this run's seconds first and at most five
# runs' in all, so build/test-split.sh can balance the shards by a median rather than one run's times. A class that did
# not run this time is dropped with its history. The build job keeps the history in the Actions cache for the next run.
# Usage: build/test-history.sh <history, which need not exist> <this run's times, from build/tests-all-ran.sh --times>
set -euo pipefail
export LC_ALL=C

history="$1"
times="$2"
[ -f "$history" ] || history=/dev/null

awk -F '\t' '
  BEGIN { runs = 5 }
  FILENAME == ARGV[1] { previous[$1 "\t" $2] = $4; next }
  {
    key = $1 "\t" $2
    count = split(previous[key], seconds, " ")
    line = $4
    for (i = 1; i <= count && i < runs; i++) { line = line " " seconds[i] }
    print key "\t" $3 "\t" line
  }
' "$history" "$times" | sort
