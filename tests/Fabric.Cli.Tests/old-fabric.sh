#!/usr/bin/env bash
# The stand-in for a released fabric (OldFabric.cs), started through a link named fabric-<version> beside a file
# fabric-<version>.settings that holds the version, the catalog and the packs, one per line.
set -euo pipefail
{ read -r version; read -r catalog; read -r packs; } < "$0.settings"
if [[ ${1:-} == --version ]]; then echo "$version"; exit 0; fi
project=.
arguments=("$@")
if [[ $1 == new ]]; then project=$2; fi
for ((i = 0; i < ${#arguments[@]}; i++)); do
  if [[ ${arguments[i]} == --project ]]; then project=${arguments[i + 1]}; fi
done
dotnet "$(dirname "$(readlink -f "$0")")/fabric.dll" "$@" --catalog "$catalog" --packs "$packs"
sed -i "s/^generator: .*/generator: $version/" "$project/fabric.yaml"
