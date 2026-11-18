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
# that file, one line per class: <assembly><tab><Namespace.Class><tab><parallel|serial><tab><seconds>; serial for a
# class in a collection it names itself (Fixed ports, Docker ports), which runs alone, after the parallel classes. And
# one line per test method in the same shape, named <Namespace.Class>#<Method>, the sum of its test cases' times.
# build/test-history.sh adds them to the times build/test-split.sh balances the next run's shards by.
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
# directory under the reports directory><tab><Namespace.Class>#<Method>. Each report starts with a line naming its shard,
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
    print assembly "\t" type "." method "\t" type "\t" attribute($0, "time") "\t" mode "\t" shard "\t" type "#" method
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
  awk -F '\t' '{ seconds[$1 "\t" $3 "\t" $5] += $4; seconds[$1 "\t" $7 "\t" $5] += $4 } END { for (key in seconds) printf "%s\t%.1f\n", key, seconds[key] }' "$work/ran" | sort > "$times"
fi

exit "$failed"
