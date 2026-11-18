#!/usr/bin/env bash
# Prints the test shards for this run, in build/test-shards.txt's format (#711): as many lines as that file has, each
# naming the Fabric.Packs.Tests classes one shard runs; the shard after them runs everything they do not name. The
# classes are balanced by the times build/test-history.sh kept for them, so one slow runner does not move them, and new
# heavy classes are spread from the run after they first ran without anyone editing a file.
#
# The model (#757) is learned from those times. A class's recorded time depends on its shard: each other test running
# beside it slows it, and a class run in parts pays its setup again in every part. So each recorded run keeps its load
# (how many other tests ran beside the class, on average) and its parts (how many shards ran it), and the model is
#   time = (work + setup * (parts - 1)) * (1 + slowdown * load)
# with one slowdown and one setup for every class, the pair that fits the recorded runs best (least squares of the
# logarithms, the setup by regression on the parts for a given slowdown), and each class's work the median its runs
# give. A class whose runs never differ in load or parts cannot tell either apart; with none that do, both are 0, which
# is #710's model. A shard's estimate then plays its parallel classes out four at a time on the runner's four cores,
# slowest first, each running at 1 / (1 + slowdown * (running - 1)) of its speed alone, and adds its serial classes (a
# collection that runs alone, such as Fixed ports), which run one after another, after them. Every class outside
# Fabric.Packs.Tests, and every class with no time yet, stays in the last shard. The classes are assigned slowest first
# to the shard they lengthen least, then classes are moved or swapped off the slowest shard while that shortens it.
#
# A class whose work alone is over the target, 15 minutes, runs in parts (#738): where the history keeps its methods'
# times (build/test-history.sh does for a class whose median is over the target), each method is assigned like a class
# and named <Namespace.Class>#<Method>, and a method with no time yet runs in the last shard. A method's share of the
# class's work is its share of the methods' times, and each part pays the setup once more. A class's methods run one
# after another, so the parts of one class in one shard take as long as their sum, with one setup; the model counts
# them so.
#
# Without a history, or when the times would leave a shard empty, the split is build/test-shards.txt as it stands. A
# class whose source file is gone is not named, nor a method its class's source no longer declares: a shard naming
# only such classes would run no test and fail.
# Prints the slowdown and setup it learned, and each shard's estimate, to stderr.
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
# The methods of Fabric.Packs.Tests the history has times for that their class's source still declares.
: > "$work/methods"
awk -F '\t' '$1 == "Fabric.Packs.Tests" && index($2, "#") { print $2 }' "$history" | while IFS= read -r part; do
  class="${part%%#*}"
  source="$(find "$repository/tests/Fabric.Packs.Tests" -name "${class##*.}.cs" -not -path '*/bin/*' -not -path '*/obj/*' | head -n 1)"
  if [ -n "$source" ] && grep -qE "[^A-Za-z0-9_]${part#*#}[[:space:]]*\(" "$source"; then
    echo "$part" >> "$work/methods"
  fi
done

status=0
awk -F '\t' -v named="$named" -v estimates="$work/estimates" -v shards="$work/split" '
  BEGIN { limit = 15 * 60; cores = 4 }
  function median(list, count,    values, i, j, value) {
    for (i = 1; i <= count; i++) { values[i] = list[i] }
    for (i = 2; i <= count; i++) {
      value = values[i]
      for (j = i - 1; j >= 1 && values[j] > value; j--) { values[j + 1] = values[j] }
      values[j + 1] = value
    }
    return count % 2 ? values[(count + 1) / 2] : (values[count / 2] + values[count / 2 + 1]) / 2
  }
  # Each class line'"'"'s work, with slowdown s and setup u: the median of what its runs give.
  function works(s, u,    l, r, given) {
    for (l = 1; l <= lines; l++) {
      for (r = 1; r <= runs[l]; r++) { given[r] = took[l, r] / (1 + s * load[l, r]) - u * (parts[l, r] - 1) }
      work[l] = median(given, runs[l])
      if (work[l] < 0) { work[l] = 0 }
    }
  }
  # The setup per part that fits slowdown s best: the regression of each class line'"'"'s work on its parts.
  function setup(s,    l, r, meanParts, meanWork, alone, numerator, denominator) {
    numerator = 0; denominator = 0
    for (l = 1; l <= lines; l++) {
      meanParts = 0; meanWork = 0
      for (r = 1; r <= runs[l]; r++) { alone[r] = took[l, r] / (1 + s * load[l, r]); meanParts += parts[l, r]; meanWork += alone[r] }
      meanParts /= runs[l]; meanWork /= runs[l]
      for (r = 1; r <= runs[l]; r++) {
        numerator += (parts[l, r] - meanParts) * (alone[r] - meanWork)
        denominator += (parts[l, r] - meanParts) ^ 2
      }
    }
    return denominator > 0 && numerator > 0 ? numerator / denominator : 0
  }
  function misfit(s, u,    l, r, error, predicted) {
    error = 0
    for (l = 1; l <= lines; l++) {
      for (r = 1; r <= runs[l]; r++) {
        predicted = (work[l] + u * (parts[l, r] - 1)) * (1 + s * load[l, r])
        error += (log(took[l, r] + 1) - log(predicted + 1)) ^ 2
      }
    }
    return error
  }
  # The seconds the parallel work queued[1..count], slowest first, takes run four at a time, each slowed by the others.
  function play(queued, count,    running, active, waiting, elapsed, least, kept, i) {
    active = 0; waiting = 1; elapsed = 0
    while (waiting <= count || active > 0) {
      while (active < cores && waiting <= count) { running[++active] = queued[waiting++] }
      least = running[1]
      for (i = 2; i <= active; i++) { if (running[i] < least) { least = running[i] } }
      elapsed += least * (1 + slowdown * (active - 1))
      kept = 0
      for (i = 1; i <= active; i++) { if (running[i] - least > 1e-9) { running[++kept] = running[i] - least } }
      active = kept
    }
    return elapsed
  }
  # The seconds bin b takes with item add (0 for none) in it and item drop (0 for none) out of it. Items of one class
  # (the parts of a class split by method) run one after another, and pay the setup once in the bin.
  function cost(b, add, drop,    k, item, serial, sums, groups, listed, count, queued, i, j, value) {
    serial = 0; groups = 0
    for (k = 1; k <= members[b] + 1; k++) {
      item = k <= members[b] ? member[b, k] : add
      if (!item || item == drop) { continue }
      if (alone[item]) { serial += seconds[item]; continue }
      if (!(group[item] in sums)) { listed[++groups] = group[item]; sums[group[item]] = divided[group[item]] ? perPart : 0 }
      sums[group[item]] += seconds[item]
    }
    count = 0
    for (i = 1; i <= groups; i++) {
      value = sums[listed[i]]
      for (j = count; j >= 1 && queued[j] < value; j--) { queued[j + 1] = queued[j] }
      queued[j + 1] = value; count++
    }
    parallelTime = play(queued, count)
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
  FILENAME == ARGV[2] { part[$1] = 1; next }
  {
    class = $2; sub(/#.*/, "", class)
    type = class; sub(/.*\./, "", type)
    if ($1 == "Fabric.Packs.Tests" && !(type in source)) { next }
    count = split($4, values, " "); split($5, loads, " "); split($6, shares, " ")
    if (index($2, "#")) {
      # A method: its share of its class, by the median of its times as they were.
      for (r = 1; r <= count; r++) { values[r] += 0 }
      methodSeconds[$2] = median(values, count); methodTotal[class] += methodSeconds[$2]
      if ($2 in part) { methods[class] = methods[class] " " $2 }
      next
    }
    lines++
    lineName[lines] = $2; mode[lines] = $3; packs[lines] = $1 == "Fabric.Packs.Tests"; runs[lines] = count
    for (r = 1; r <= count; r++) { took[lines, r] = values[r] + 0; load[lines, r] = loads[r] + 0; parts[lines, r] = (r in shares) ? shares[r] + 0 : 1 }
  }
  END {
    # The slowdown, in hundredths, and the setup that fit the recorded runs best; the smaller among equals.
    best = -1
    for (hundredths = 0; hundredths <= 200; hundredths++) {
      s = hundredths / 100; u = setup(s); works(s, u); error = misfit(s, u)
      if (best < 0 || error < best - 1e-9) { best = error; slowdown = s; perPart = u }
    }
    works(slowdown, perPart)
    printf "Learned from the recorded times: each other test running beside a class slows it by %.2f of its time alone, and a class run in parts pays %.0f s again for every part.\n", slowdown, perPart > estimates

    count = 0
    for (l = 1; l <= lines; l++) {
      class = lineName[l]
      if (packs[l] && methods[class] != "" && work[l] > limit) {
        divided[class] = 1
        parted = split(substr(methods[class], 2), list, " ")
        for (p = 1; p <= parted; p++) {
          count++
          itemName[count] = list[p]; itemGroup[count] = class; itemAlone[count] = mode[l] == "serial"; itemMovable[count] = 1
          itemSeconds[count] = methodTotal[class] > 0 ? (work[l] > perPart ? work[l] - perPart : 0) * methodSeconds[list[p]] / methodTotal[class] : 0
        }
        continue
      }
      count++
      itemName[count] = class; itemGroup[count] = class; itemAlone[count] = mode[l] == "serial"; itemSeconds[count] = work[l]; itemMovable[count] = packs[l]
    }

    bins = named + 1
    # Slowest first, by name among equals; an item number is then its rank.
    for (i = 1; i <= count; i++) { order[i] = i }
    for (i = 2; i <= count; i++) {
      it = order[i]
      for (j = i - 1; j >= 1 && (itemSeconds[order[j]] < itemSeconds[it] || (itemSeconds[order[j]] == itemSeconds[it] && itemName[order[j]] > itemName[it])); j--) { order[j + 1] = order[j] }
      order[j + 1] = it
    }
    for (i = 1; i <= count; i++) { it = order[i]; name[i] = itemName[it]; group[i] = itemGroup[it]; alone[i] = itemAlone[it]; seconds[i] = itemSeconds[it]; movable[i] = itemMovable[it] }

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
          if (cost(b, i, 0) < target) {
            value = larger(cost(slowest, 0, i), cost(b, i, 0))
            if (value < target) { target = value; found = 1; moved = i; other = b; swapped = 0 }
          }
          for (l = 1; l <= members[b]; l++) {
            j = member[b, l]
            if (!movable[j] || cost(b, i, j) >= target) { continue }
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
      classes = 0
      for (k = 1; k <= members[b]; k++) { if (!((b, group[member[b, k]]) in counted)) { counted[b, group[member[b, k]]] = 1; classes++ } }
      printf "shard %2d: est %5.1f min  (parallel %5.1f, serial %5.1f, classes %d%s)\n", b, total / 60, parallelTime / 60, serialTime / 60, classes, b == bins ? ", the rest" : "" > estimates
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
' "$work/sources" "$work/methods" "$history" || status=$?
if [ "$status" -eq 3 ]; then
  echo "The times would leave a shard empty; the split is $fallback." >&2
  cat "$fallback"
  exit 0
elif [ "$status" -ne 0 ]; then
  exit "$status"
fi

cat "$work/estimates" >&2
cat "$work/split"
