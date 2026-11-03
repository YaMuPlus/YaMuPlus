# Build tools

Everything the builds use to sign the macOS app and pack it into a DMG where macOS `codesign` and `hdiutil` are not available, prebuilt once by [`make-tools.sh`](make-tools.sh): a build needs nothing but the .NET SDK. `rcodesign` seals the `.app` bundle with an ad-hoc signature, `xorriso` writes an ISO 9660 image with Rock Ridge, `dmg` compresses it into a DMG (UDZO).

| File | Program | Version | License | Source |
|---|---|---|---|---|
| `*/rcodesign`, `windows-x86_64/rcodesign.exe` | [rcodesign](https://github.com/indygreg/apple-platform-rs), official release builds | 0.29.0 | MPL-2.0 | [apple-codesign/0.29.0](https://github.com/indygreg/apple-platform-rs/tree/apple-codesign/0.29.0) |
| `linux-x86_64/xorriso`, `linux-aarch64/xorriso` | [GNU xorriso](https://www.gnu.org/software/xorriso/), static | 1.5.8.pl02 | GPL-3.0+ | [tar.gz](https://ftp.gnu.org/gnu/xorriso/xorriso-1.5.8.pl02.tar.gz) |
| `*/dmg`, `windows-x86_64/dmg.exe` | [libdmg-hfsplus](https://github.com/fanquake/libdmg-hfsplus), static | commit `1cc791e` | GPL-3.0 | [tar.gz](https://github.com/fanquake/libdmg-hfsplus/archive/1cc791e4173da9cb0b0cc16c5a1aaa25d5eb5efa.tar.gz) |
| `windows-x86_64/xorriso.exe` | GNU xorriso, MSYS2 package | 1.5.8.2-1 | GPL-3.0+ | [src](https://repo.msys2.org/msys/sources/xorriso-1.5.8.2-1.src.tar.zst) |
| `windows-x86_64/msys-2.0.dll` | MSYS2 runtime | 3.6.12-1 | LGPL-3.0+ | [src](https://repo.msys2.org/msys/sources/msys2-runtime-3.6.12-1.src.tar.zst) |
| `windows-x86_64/msys-z.dll` | zlib | 1.3.2-1 | Zlib | [src](https://repo.msys2.org/msys/sources/zlib-1.3.2-1.src.tar.zst) |
| `windows-x86_64/msys-bz2-1.dll` | bzip2 | 1.0.8-4 | bzip2-1.0.6 | [src](https://repo.msys2.org/msys/sources/bzip2-1.0.8-4.src.tar.zst) |
| `windows-x86_64/msys-iconv-2.dll` | GNU libiconv | 1.19-1 | LGPL-2.1+ | [src](https://repo.msys2.org/msys/sources/libiconv-1.19-1.src.tar.zst) |
| `windows-x86_64/msys-readline8.dll` | GNU Readline | 8.3.006-2 | GPL-3.0+ | [src](https://repo.msys2.org/msys/sources/readline-8.3.006-2.src.tar.zst) |
| `windows-x86_64/msys-ncursesw6.dll` | ncurses | 6.6-2 | X11 | [src](https://repo.msys2.org/msys/sources/ncurses-6.6-2.src.tar.zst) |

These are separate programs that the build scripts run; they are not part of YaMu+ and keep their own licenses. [`COPYING`](COPYING) is the text of the GPL-3.0, [`COPYING.rcodesign`](COPYING.rcodesign) holds the licenses of rcodesign and the libraries it is built from.
