#!/usr/bin/env bash
# The stand-in for a released fabric (OldFabric.cs), started through a link named fabric-<version> beside a file
# fabric-<version>.settings that holds the version, the catalog and the packs, one per line, and optionally
# numbered-adrs: then it names the pack ADRs as fabric 0.7.0 and earlier did, docs/adr/NNNN-<dimension>-<option>.md
# (numbered here in file-name order, which is all the stand-in needs: base and project number them alike).
set -euo pipefail
{ read -r version; read -r catalog; read -r packs; read -r adrs || adrs=; } < "$0.settings"
if [[ ${1:-} == --version ]]; then echo "$version"; exit 0; fi
project=.
arguments=("$@")
if [[ $1 == new ]]; then project=$2; fi
for ((i = 0; i < ${#arguments[@]}; i++)); do
  if [[ ${arguments[i]} == --project ]]; then project=${arguments[i + 1]}; fi
done
dotnet "$(dirname "$(readlink -f "$0")")/fabric.dll" "$@" --catalog "$catalog" --packs "$packs"
sed -i "s/^generator: .*/generator: $version/" "$project/fabric.yaml"
if [[ $adrs == numbered-adrs && -d $project/docs/adr/fabric ]]; then
  number=0
  for adr in $(cd "$project/docs/adr/fabric" && printf '%s\n' *.md | LC_ALL=C sort); do
    number=$((number + 1))
    mv "$project/docs/adr/fabric/$adr" "$project/docs/adr/$(printf %04d "$number")-$adr"
  done
  rmdir "$project/docs/adr/fabric"
fi
