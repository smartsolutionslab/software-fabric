#!/usr/bin/env bash
# The CLI as its users get it (owner decision E5): packs src/Fabric.Cli as a .NET tool into <feed>, installs it from
# there into an empty directory with no other package source and an empty package cache, checks that `fabric
# --version` reports the package's version, generates <profile> with the installed tool, compares it with what the
# repository's own CLI generates, and builds it.
# Needs a Release build of the solution, and what building a generated project needs: its pinned SDK and the Aspire
# CLI bundle.
#
# --release <tag> packs the version without its suffix (owner decision on #381: a release clears VersionSuffix), and
# first fails unless <tag> is v<that version>. The Release build must have been made with -p:VersionSuffix= as well,
# or the installed tool reports another version than its package and the check fails.
# The release workflow (.github/workflows/release.yml) pushes the package this leaves in <feed>; this script publishes
# nothing.
# Usage: build/tool-check.sh <profile> <feed> [--release <tag>]
set -euo pipefail

usage() {
  echo "Usage: build/tool-check.sh <profile> <feed> [--release <tag>]" >&2
  exit 2
}

[[ $# -eq 2 || $# -eq 4 && $3 == --release ]] || usage
profile="$1"
feed="$2"
tag="${4:-}"
repository="$(cd "$(dirname "$0")/.." && pwd)"
project="$repository/src/Fabric.Cli/Fabric.Cli.csproj"
cli="$repository/src/Fabric.Cli/bin/Release/net11.0/fabric.dll"

versioning=()
if [[ $# -eq 4 ]]; then
  versioning=(-p:VersionSuffix=)
  release="$(dotnet msbuild "$project" -getProperty:Version "${versioning[@]}")"
  if [[ $tag != "v$release" ]]; then
    echo "::error title=release::the tag $tag does not name the version this commit releases, $release; tag it v$release." >&2
    exit 1
  fi
fi

mkdir -p "$feed"
dotnet pack "$project" -c Release --no-build -o "$feed" "${versioning[@]}"
packages=("$feed"/fabric.*.nupkg)
if [[ ${#packages[@]} -ne 1 || ! -f ${packages[0]} ]]; then
  echo "::error title=tool::expected one fabric package in $feed, found: ${packages[*]}" >&2
  exit 1
fi
version="$(basename "${packages[0]}" .nupkg)"
version="${version#fabric.}"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
# The install's nuget.config sits beside the tool, not above the generated projects, which restore from their own sources.
mkdir "$work/install"
cat >"$work/install/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="feed" value="$(cd "$feed" && pwd)" />
  </packageSources>
</configuration>
EOF
cd "$work/install"
NUGET_PACKAGES="$work/install/packages" dotnet tool install fabric --version "$version" --tool-path "$work/install/tools" --configfile "$work/install/nuget.config"
installed="$work/install/tools/fabric"

reported="$("$installed" --version)"
if [[ $reported != "$version" ]]; then
  echo "::error title=tool::the package is version $version, but the installed tool reports $reported." >&2
  exit 1
fi

cd "$work"
mkdir "$work/installed" "$work/repository"
"$installed" new "$work/installed/shop" --profile "$profile"
dotnet "$cli" new "$work/repository/shop" --profile "$profile"
diff -r "$work/repository" "$work/installed"

(cd "$work/installed/shop" && dotnet build build.proj -c Release)
echo "fabric $version: packed, installed from a folder feed, generated $profile as the repository's CLI does, and built it."
