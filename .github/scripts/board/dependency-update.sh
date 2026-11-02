#!/usr/bin/env bash
# A dependency update by Dependabot is not reviewed by a model (board protocol, docs/adr): Dependabot's runs get a
# read-only token and no secrets, so the cross-model review cannot run on them, and a version bump quotes no failing
# test. Only a narrow case is exempt: the pull request's author is dependabot[bot], and every file it changes is a
# dependency manifest or lockfile, a workflow counting only when nothing but its uses: lines change.
# Usage: dependency-update.sh <pull request author> <base sha> <head sha>
# Exits 0 and prints the note when the pull request is exempt; exits 1 and says why when it is not.
set -euo pipefail

author="$1"
base="$2"
head="$3"

if [ "$author" != "dependabot[bot]" ]; then
  echo "Not a dependency update: the pull request's author is $author, not dependabot[bot]."
  exit 1
fi

changed="$(git diff --name-only --no-renames "$base...$head")"
if [ -z "$changed" ]; then
  echo "Not a dependency update: the pull request changes no file."
  exit 1
fi

uses_line='^[+-][[:space:]]*(-[[:space:]]+)?uses:[[:space:]]'
while IFS= read -r file; do
  case "$file" in
    Directory.Packages.props | .config/dotnet-tools.json | package.json | */package.json | pnpm-lock.yaml | */pnpm-lock.yaml)
      continue
      ;;
  esac

  if [ "$(dirname "$file")" = .github/workflows ] && [[ "$file" == *.yml ]]; then
    other="$(git diff --no-color --no-ext-diff --no-renames --unified=0 "$base...$head" -- "$file" \
      | sed -n '/^@@/,$p' | grep -E '^[+-]' | grep -vE "$uses_line" || true)"
    if [ -z "$other" ]; then
      continue
    fi

    echo "Not a dependency update: $file changes more than its uses: lines."
    exit 1
  fi

  echo "Not a dependency update: $file is not a dependency manifest or lockfile."
  exit 1
done <<< "$changed"

echo "Dependency update by Dependabot: not reviewed by a model; build, tests and the other gates apply."

