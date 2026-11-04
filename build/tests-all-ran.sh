#!/usr/bin/env bash
# The tests run in shards (ci.yml, build/test-shard.sh); this is what makes that safe. It fails unless every test
# method of every assembly given is in a shard's report, so a shard left out of the matrix, a filter that matches
# nothing or a class in no shard fails the build job instead of quietly running fewer tests.
#
# Reads the xUnit XML reports (--report-xunit-xml) anywhere under the reports directory, one subdirectory per shard's
# artifact, and lists each assembly's methods with its own runner. Prints, per assembly, how many methods and test
# cases ran, which is the count to compare before and after a change to the shards, and then each class's time, the
# sum of its tests' times, which is what build/test-shards.txt is balanced by.
# Usage: build/tests-all-ran.sh <reports directory> <test assembly .dll>...
set -euo pipefail
export LC_ALL=C

reports="$1"
shift
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# One line per test case run: <assembly name><tab><Namespace.Class.Method><tab><Namespace.Class><tab><seconds>.
find "$reports" -type f -name '*.xml' -print0 | xargs -0 -r cat | tr '<' '\n' | awk '
  function attribute(record, key) {
    if (match(record, " " key "=\"[^\"]*\"")) { return substr(record, RSTART + length(key) + 3, RLENGTH - length(key) - 4) }
    return ""
  }
  /^assembly / { assembly = attribute($0, "name"); sub(/.*\//, "", assembly); sub(/\.dll$/, "", assembly) }
  /^test / { type = attribute($0, "type"); print assembly "\t" type "." attribute($0, "method") "\t" type "\t" attribute($0, "time") }
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
  if [ ! -s "$work/listed" ]; then
    echo "$name: the runner lists no test methods:"
    echo "$listing"
    failed=1
  elif [ -n "$missing" ]; then
    echo "$name: these test methods ran in no shard:"
    sed 's/^/  /' <<<"$missing"
    failed=1
  else
    echo "$name: $(wc -l < "$work/listed") test methods, every one ran ($(wc -l < "$work/cases") test cases)"
  fi
done

echo "::group::Time per test class"
echo "seconds  tests  class (from the reports, slowest first)"
awk -F '\t' '{ seconds[$3] += $4; tests[$3]++ } END { for (type in seconds) printf "%9.1f  %5d  %s\n", seconds[type], tests[type], type }' "$work/ran" | sort -k1,1nr -k3,3
echo "::endgroup::"

exit "$failed"
