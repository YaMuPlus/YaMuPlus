#!/bin/sh
# Builds both YaMu+ releases without installing .NET: build.sh runs in the .NET SDK container (the signing and DMG
# tools are in tools/) on a copy of the sources mounted read-only; the only result here is dist/.
# GitHub Actions builds releases with this script too.
set -eu
cd "$(dirname "$0")"
rm -rf dist
mkdir dist
docker run --rm -v "$PWD":/repo:ro -v "$PWD/dist":/out -e BUILD="$(date +%s)" mcr.microsoft.com/dotnet/sdk:10.0 sh -euc \
  'cp -r /repo /build && cd /build && rm -rf dist src/bin src/obj; sh build.sh; cp dist/* /out/'
ls -l dist
