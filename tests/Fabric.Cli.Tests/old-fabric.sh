#!/usr/bin/env bash
# The stand-in for a released fabric (OldFabric.cs), started through a link named fabric-<version> beside a file
# fabric-<version>.settings that holds the version, the catalog and the packs, one per line, and optionally
# numbered-adrs: then it names the pack ADRs as fabric 0.7.0 and earlier did, docs/adr/NNNN-<dimension>-<option>.md
# (numbered here in file-name order, which is all the stand-in needs: base and project number them alike).
# add-module in a git repository reads fabric.yaml as the release would: one that names its own version. The release is
# this build, so the stand-in names this build's version and commits that, and records its own version again after.
set -euo pipefail
{ read -r version; read -r catalog; read -r packs; read -r adrs || adrs=; } < "$0.settings"
if [[ ${1:-} == --version ]]; then echo "$version"; exit 0; fi
project=.
arguments=("$@")
if [[ $1 == new ]]; then project=$2; fi
for ((i = 0; i < ${#arguments[@]}; i++)); do
  if [[ ${arguments[i]} == --project ]]; then project=${arguments[i + 1]}; fi
done
fabric="$(dirname "$(readlink -f "$0")")/fabric.dll"
if [[ $1 == add-module && -d $project/.git ]]; then
  sed -i "s/^generator: .*/generator: $(dotnet "$fabric" --version)/" "$project/fabric.yaml"
  git -C "$project" -c user.name=old-fabric -c user.email=old-fabric@example.com -c commit.gpgsign=false \
    commit -q --allow-empty --no-verify -m "fabric.yaml as fabric $version reads it" -- fabric.yaml
fi
dotnet "$fabric" "$@" --catalog "$catalog" --packs "$packs"
sed -i "s/^generator: .*/generator: $version/" "$project/fabric.yaml"
if [[ $adrs == numbered-adrs && -d $project/docs/adr/fabric ]]; then
  number=0
  for adr in $(cd "$project/docs/adr/fabric" && printf '%s\n' *.md | LC_ALL=C sort); do
    number=$((number + 1))
    mv "$project/docs/adr/fabric/$adr" "$project/docs/adr/$(printf %04d "$number")-$adr"
  done
  rmdir "$project/docs/adr/fabric"
fi
