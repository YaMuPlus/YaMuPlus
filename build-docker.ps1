# Builds both YaMu+ releases without installing .NET: build.sh runs in the .NET SDK container (the signing and DMG
# tools are in tools\) on a copy of the sources mounted read-only; the only result here is dist\.
# Usage: powershell -ExecutionPolicy Bypass -File build-docker.ps1   (requires Docker)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (Test-Path dist) { Remove-Item -Recurse -Force dist }
New-Item -ItemType Directory dist | Out-Null
docker run --rm -v "${PSScriptRoot}:/repo:ro" -v "${PSScriptRoot}\dist:/out" -e "BUILD=$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())" `
  mcr.microsoft.com/dotnet/sdk:10.0 sh -euc 'cp -r /repo /build && cd /build && rm -rf dist src/bin src/obj; sh build.sh; cp dist/* /out/'
if ($LASTEXITCODE -ne 0) { throw "docker run failed: $LASTEXITCODE" }
Get-ChildItem dist
