#!/usr/bin/env bash
# dotnet test --project exists only on Microsoft.Testing.Platform, which global.json's "test": { "runner": ... } selects.
# Without it the SDK falls back to VSTest and stops at its own arguments (MSB1001: Unknown switch) before any test runs,
# saying nothing about why; this check runs first and names the file. The architecture rule
# GlobalJsonSelectsTheTestingPlatform says the same where the test platform runs the tests itself (an IDE).
# Usage: bash .github/scripts/check-test-runner.sh   (from anywhere in the project)
set -euo pipefail

cd "$(dirname "$0")/../.."
expected="Microsoft.Testing.Platform"

if [ ! -f global.json ]; then
  echo "global.json is missing; it pins the SDK and sets test.runner to \"$expected\"." >&2
  exit 1
fi

# global.json has one "runner" key, inside "test"; a value on the key's own line is how every tool writes it.
runner="$(sed -nE 's/^[^/]*"runner"[[:space:]]*:[[:space:]]*"([^"]*)".*$/\1/p' global.json | head -n 1)"
if [ -z "$runner" ]; then
  echo "global.json: test.runner is missing; it must be \"$expected\", or dotnet test --project fails with MSB1001: Unknown switch." >&2
  exit 1
fi

if [ "$runner" != "$expected" ]; then
  echo "global.json: test.runner is \"$runner\", not \"$expected\"; dotnet test --project fails with MSB1001: Unknown switch." >&2
  exit 1
fi
