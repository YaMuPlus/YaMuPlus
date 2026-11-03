<p align="center">
  <img src="images/logo.png" width="128" alt="">
</p>

<h1 align="center">YaMu+</h1>

<p align="center">
  Save tracks, albums and playlists from the Yandex Music desktop app to regular files
</p>

<p align="center">
  <a href="https://github.com/YaMuPlus/YaMuPlus/releases/latest/download/YaMuPlus-windows-x64.exe"><img src="https://img.shields.io/badge/Download-Windows-FFFF00?style=for-the-badge&labelColor=1A1A1A" alt="Download for Windows"></a>
  <a href="https://github.com/YaMuPlus/YaMuPlus/releases/latest/download/YaMuPlus-macos-arm64.dmg"><img src="https://img.shields.io/badge/Download-macOS-FFFF00?style=for-the-badge&labelColor=1A1A1A" alt="Download for macOS"></a>
</p>

<p align="center">
  <a href="README.md">Русский</a> · <b>English</b>
</p>

<p align="center">
  <img src="images/menu.en.png" alt="Album menu in Yandex Music with the Save locally and Delete downloads items">
</p>

## Features

- **Track to a file.** "Save locally" in a track's ⋯ menu.
- **Album, single or playlist to a folder.** The same in a collection's ⋯ menu, with optional track numbering.
- **Cleanup.** "Delete downloads" removes the collection's tracks downloaded inside the music app.

<table>
  <tr>
    <td width="50%"><img src="images/dialog.en.png" alt="Save dialog: folder name and destination"></td>
    <td width="50%"><img src="images/progress.en.png" alt="Save panel: album saving in progress, 4 of 14"></td>
  </tr>
  <tr>
    <td align="center">Folder name and destination</td>
    <td align="center">Progress right inside the music app</td>
  </tr>
</table>

## Installation

1. Download YaMu+ with the buttons above or from [Releases](https://github.com/YaMuPlus/YaMuPlus/releases). The macOS version is for Apple Silicon.
2. Install Yandex Music from the official website if you do not have it yet; the Microsoft Store version is not supported.
3. macOS: open `YaMuPlus-macos-arm64.dmg` and drag YaMu+ to Applications. YaMu+ is not notarized by Apple, so macOS will not open it the first time:
   - macOS 15 and later: “Apple could not verify ‘YaMu+’ is free of malware…”. Click Done, open System Settings → Privacy & Security, click Open Anyway next to YaMu+ under Security and confirm with your password or Touch ID.
   - macOS 12–14: “‘YaMu+’ can’t be opened because Apple cannot check it for malicious software.” Click OK, then Control-click YaMu+ in Applications, choose Open and confirm.

   After that YaMu+ opens as usual. On any version you can instead run this once in Terminal: `xattr -cr "/Applications/YaMu+.app"`.

## Usage

Open YaMu+ and click Launch: the music app opens with the new menu items.

<p align="center">
  <img src="images/window.en.png" width="495" alt="YaMu+ window">
</p>

<details>
<summary><b>Building from source</b></summary>

Each script builds both releases, `dist/YaMuPlus-windows-x64.exe` and `dist/YaMuPlus-macos-arm64.dmg`:

| System | With .NET SDK 9+ installed | Without .NET, in Docker |
|---|---|---|
| Windows | `powershell -ExecutionPolicy Bypass -File build.ps1` | `powershell -ExecutionPolicy Bypass -File build-docker.ps1` |
| macOS, Linux | `./build.sh` | `./build-docker.sh` |

Releases are built by GitHub Actions with the same `build-docker.sh` (`.github/workflows/release.yml`): bump `<Version>` in `src/YaMuPlus.csproj` and push a tag with the same version, for example `v1.0.1`.

</details>
