#!/usr/bin/env bash
# The tests run in shards (ci.yml, build/test-shard.sh); this is what makes that safe. It fails unless every test
# method of every assembly given is in exactly one shard's report, so a shard left out of the matrix, a filter that
# matches nothing or a class in no shard fails the build job instead of quietly running fewer tests, and a class split
# across shards by method (#738) whose parts overlap fails it instead of running a test twice.
#
# Reads the xUnit XML reports (--report-xunit-xml) anywhere under the reports directory, one subdirectory per shard's
# artifact, and lists each assembly's methods with its own runner. Prints, per assembly, how many methods and test
# cases ran, which is the count to compare before and after a change to the shards, and then each class's time, the
# sum of its tests' times, which is what the shards are balanced by. With --times, it also writes each class's time to
# that file, one line per class: <assembly><tab><Namespace.Class><tab><parallel|serial><tab><seconds><tab><load><tab>
# <parts>; serial for a class in a collection it names itself (Fixed ports, Docker ports), which runs alone, after the
# parallel classes. And one line per test method in the same shape, named <Namespace.Class>#<Method>, the sum of its
# test cases' times. The load is how many other tests ran beside the class's on average, in its shard, from the start and
# finish times in the reports (#757): a class is slower beside others, and build/test-split.sh learns by how much. The
# parts are how many shards ran the class: a class run in parts pays its setup in each. build/test-history.sh adds the
# lines to the times build/test-split.sh balances the next run's shards by.
# Usage: build/tests-all-ran.sh [--times <file>] <reports directory> <test assembly .dll>...
set -euo pipefail
export LC_ALL=C

times=""
if [ "${1:-}" = --times ]; then
  times="$2"
  shift 2
fi
reports="$1"
shift
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# One line per test case run: <assembly name><tab><Namespace.Class.Method><tab><Namespace.Class><tab><seconds><tab>
# parallel, or serial when the case's collection is not xUnit's own collection for its class<tab><shard: the report's
# directory under the reports directory><tab><Namespace.Class>#<Method><tab><start><tab><finish>, the last two as the
# report has them (start-rtf, finish-rtf), empty where it has none. Each report starts with a line naming its shard,
# after a line break, since a report need not end with one.
find "$reports" -type f -name '*.xml' -print0 | sort -z | while IFS= read -r -d '' report; do
  shard="$(dirname "${report#"$reports"/}")"
  printf '\n\001shard %s\n' "$shard"
  tr '<' '\n' < "$report"
done | awk '
  function attribute(record, key) {
    if (match(record, " " key "=\"[^\"]*\"")) { return substr(record, RSTART + length(key) + 3, RLENGTH - length(key) - 4) }
    return ""
  }
  /^\001shard / { shard = substr($0, 8); next }
  /^assembly / { assembly = attribute($0, "name"); sub(/.*\//, "", assembly); sub(/\.dll$/, "", assembly) }
  /^collection / { mode = attribute($0, "name") ~ /^Test collection for / ? "parallel" : "serial" }
  /^test / {
    type = attribute($0, "type"); method = attribute($0, "method")
    print assembly "\t" type "." method "\t" type "\t" attribute($0, "time") "\t" mode "\t" shard "\t" type "#" method "\t" attribute($0, "start-rtf") "\t" attribute($0, "finish-rtf")
  }
' > "$work/ran"

failed=0
for assembly in "$@"; do
  name="$(basename "$assembly" .dll)"
  # The runner prints a header line before the methods; a method's full name is a dotted identifier.
  listing="$(dotnet "$assembly" -list methods -noColor)"
  grep -E '^[A-Za-z_][A-Za-z0-9_.+`]*$' <<<"$listing" | sort -u > "$work/listed" || true
  awk -F '\t' -v name="$name" '$1 == name { print $2 }' "$work/ran" > "$work/cases"
  sort -u "$work/cases" > "$work/methods"
  missing="$(comm -23 "$work/listed" "$work/methods")"
  twice="$(awk -F '\t' -v name="$name" '$1 == name { print $2 "\t" $6 }' "$work/ran" | sort -u | cut -f1 | uniq -d)"
  if [ ! -s "$work/listed" ]; then
    echo "$name: the runner lists no test methods:"
    echo "$listing"
    failed=1
    continue
  fi
  if [ -n "$missing" ]; then
    echo "$name: these test methods ran in no shard:"
    sed 's/^/  /' <<<"$missing"
    failed=1
  fi
  if [ -n "$twice" ]; then
    echo "$name: these test methods ran in more than one shard:"
    sed 's/^/  /' <<<"$twice"
    failed=1
  fi
  if [ -z "$missing" ] && [ -z "$twice" ]; then
    echo "$name: $(wc -l < "$work/listed") test methods, every one ran in one shard ($(wc -l < "$work/cases") test cases)"
  fi
done

echo "::group::Time per test class"
echo "seconds  tests  class (from the reports, slowest first)"
awk -F '\t' '{ seconds[$3] += $4; tests[$3]++ } END { for (type in seconds) printf "%9.1f  %5d  %s\n", seconds[type], tests[type], type }' "$work/ran" | sort -k1,1nr -k3,3
echo "::endgroup::"

if [ -n "$times" ]; then
  awk -F '\t' '
    # Seconds since 1970 of a time such as 2026-11-18T23:59:00.0000000+00:00 (days from the civil date, as awk has no
    # date functions everywhere).
    function instant(stamp,    year, month, day, rest, offset, era, years, days) {
      year = substr(stamp, 1, 4) + 0; month = substr(stamp, 6, 2) + 0; day = substr(stamp, 9, 2) + 0
      rest = substr(stamp, 18); offset = 0
      if (match(rest, /[+-][0-9][0-9]:[0-9][0-9]$/)) {
        offset = (substr(rest, RSTART + 1, 2) * 3600 + substr(rest, RSTART + 4, 2) * 60) * (substr(rest, RSTART, 1) == "-" ? -1 : 1)
        rest = substr(rest, 1, RSTART - 1)
      }
      sub(/Z$/, "", rest)
      if (month <= 2) { year-- }
      era = int(year / 400); years = year - era * 400
      days = era * 146097 + years * 365 + int(years / 4) - int(years / 100) + int((153 * (month + (month > 2 ? -3 : 9)) + 2) / 5) + day - 1 - 719468
      return days * 86400 + substr(stamp, 12, 2) * 3600 + substr(stamp, 15, 2) * 60 + rest - offset
    }
    {
      cases++
      assembly[cases] = $1; class[cases] = $3; seconds[cases] = $4; mode[cases] = $5; shard[cases] = $6; method[cases] = $7
      if ($8 != "" && $9 != "") { timed[cases] = 1; start[cases] = instant($8); finish[cases] = instant($9) }
    }
    END {
      # The seconds other tests of its shard ran beside each test case.
      for (i = 1; i <= cases; i++) {
        if (!timed[i]) { continue }
        for (j = 1; j <= cases; j++) {
          if (j == i || !timed[j] || shard[j] != shard[i]) { continue }
          from = start[i] > start[j] ? start[i] : start[j]
          until = finish[i] < finish[j] ? finish[i] : finish[j]
          if (until > from) { beside[i] += until - from }
        }
      }
      for (i = 1; i <= cases; i++) {
        for (level = 1; level <= 2; level++) {
          key = assembly[i] "\t" (level == 1 ? class[i] : method[i]) "\t" mode[i]
          total[key] += seconds[i]; overlap[key] += beside[i]
          if (!((key, shard[i]) in ran)) { ran[key, shard[i]] = 1; parts[key]++ }
        }
      }
      for (key in total) { printf "%s\t%.1f\t%.2f\t%d\n", key, total[key], (total[key] > 0 ? overlap[key] / total[key] : 0), parts[key] }
    }
  ' "$work/ran" | sort > "$times"
fi

exit "$failed"
