#!/usr/bin/env bash
set -euo pipefail
if (( $# > 1 )) || [[ $# == 1 && ${1-} != --write ]]; then
    printf 'Usage: %s [--write]\n' "$0" >&2
    exit 2
fi
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
compiler=$(sed -n 's/^compiler = "\(.*\)"$/\1/p' aiken/aiken.toml)
version=$(aiken --version)
if [[ -z "$compiler" || ( "$version" != "aiken $compiler" && "$version" != "aiken $compiler"+* ) ]]; then
    printf 'Expected Aiken %s, got %s\n' "$compiler" "$version" >&2
    exit 1
fi
scratch=$(mktemp -d "${TMPDIR:-/tmp}/chrysalis-blueprints.XXXXXXXX")
trap 'rm -rf -- "$scratch"' EXIT
cp -R aiken/{aiken.toml,aiken.lock,lib,validators} "$scratch"
(cd "$scratch" && aiken build --deny --out "$scratch/shapes.json")
case "${1-}" in
    --write) cp "$scratch/shapes.json" data/shapes.json; shasum -a 256 data/shapes.json ;;
    '') cmp "$scratch/shapes.json" data/shapes.json ;;
esac
