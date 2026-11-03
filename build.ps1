# Builds both YaMu+ releases with the installed .NET SDK 9+ on Windows: dist\YaMuPlus-windows-x64.exe and
#   dist\YaMuPlus-macos-arm64.dmg (YaMu+.app for Apple Silicon, ad-hoc signed with rcodesign). The signing and DMG
#   tools are prebuilt in tools\windows-x86_64 (tools\make-tools.sh).
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if ([int](dotnet --version).Split('.')[0] -lt 9) { throw '.NET SDK 9 or newer is required' }
$build = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() # build number: Unix time (UTC), the same in both releases
$version = [regex]::Match((Get-Content src\YaMuPlus.csproj -Raw), '<Version>(.+)</Version>').Groups[1].Value
$pub = Join-Path $PSScriptRoot 'src\bin\publish'
foreach ($d in 'dist', $pub) { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
New-Item -ItemType Directory dist | Out-Null

# runs a tool that writes a log (rcodesign lists every signed file): the log is shown only when it fails
function Run($exe, [string[]]$arguments) {
  $log = "$pub\tool.log"
  $p = Start-Process $exe -ArgumentList ($arguments | ForEach-Object { "`"$_`"" }) -WorkingDirectory $PSScriptRoot `
    -NoNewWindow -Wait -PassThru -RedirectStandardError $log -RedirectStandardOutput "$log.out"
  if ($p.ExitCode -ne 0) { Get-Content $log, "$log.out"; throw "$(Split-Path -Leaf $exe) failed: $($p.ExitCode)" }
}

function Publish($rid) {
  dotnet publish src\YaMuPlus.csproj -c Release -r $rid --self-contained true -p:DebugType=none "-p:BuildNumber=$build" -o "$pub\$rid"
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish $rid failed: $LASTEXITCODE" }
}

'── 1/2 Windows (win-x64)'
Publish win-x64
Copy-Item "$pub\win-x64\YaMuPlus.exe" dist\YaMuPlus-windows-x64.exe

'── 2/2 macOS (osx-arm64)'
Publish osx-arm64
$app = "$pub\mac\YaMu+.app"
New-Item -ItemType Directory "$app\Contents\MacOS", "$app\Contents\Resources" | Out-Null
Copy-Item "$pub\osx-arm64\YaMuPlus", "$pub\osx-arm64\*.dylib" "$app\Contents\MacOS\"
Copy-Item src\Assets\icon.icns "$app\Contents\Resources\AppIcon.icns"
$plist = (Get-Content src\Info.plist -Raw).Replace('__VERSION__', $version).Replace('__BUILD__', "$build")
[IO.File]::WriteAllText("$app\Contents\Info.plist", $plist) # UTF-8 without BOM

# Ad-hoc signature of the whole bundle with rcodesign (codesign exists only on macOS): macOS calls a downloaded app with
# an unsealed bundle damaged.
$bin = Join-Path $PSScriptRoot 'tools\windows-x86_64'
Run "$bin\rcodesign.exe" sign, $app

# the DMG window: the background with the arrow and .DS_Store with the layout (made once with dmgbuild, refers to
# /Volumes/YaMu+/.background.tiff); the link to Applications is added by xorriso below
Copy-Item src\Assets\dmg\background.tiff "$pub\mac\.background.tiff"
Copy-Item src\Assets\dmg\DS_Store "$pub\mac\.DS_Store"

# DMG with the usual drag-to-Applications window, the same as build.sh makes in Docker: xorriso (MSYS2) writes an
# ISO 9660 image with Rock Ridge, dmg (libdmg-hfsplus) compresses it. Windows files carry no unix permissions and
# links, so xorriso sets them in the image: rwxr-xr-x in Contents/MacOS (macOS will not start the app without it),
# root as the owner, the link to /Applications. Paths are relative: MSYS2 programs do not take every Windows path.
Run "$bin\xorriso.exe" ('-report_about SORRY -as mkisofs -V YaMu+ -D -l -R -no-pad -o src/bin/publish/image.iso ' +
  'src/bin/publish/mac -- -chown_r 0 / -- -chgrp_r 0 / -- -chmod 0755 /YaMu+.app/Contents/MacOS/* -- ' +
  '-lns /Applications /Applications').Split(' ')
Run "$bin\dmg.exe" src/bin/publish/image.iso, dist/YaMuPlus-macos-arm64.dmg
Remove-Item -Recurse -Force $pub
'Done: dist\YaMuPlus-windows-x64.exe, dist\YaMuPlus-macos-arm64.dmg'
