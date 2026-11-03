#!/bin/sh
# Rebuilds the build tools kept in this folder: they are committed, so a build needs nothing but the .NET SDK.
#   docker run --rm -v "$PWD/tools:/tools" debian:trixie-slim sh /tools/make-tools.sh   (repository root, x86_64 host)
# rcodesign: official release binaries checked against their SHA-256. xorriso and dmg (libdmg-hfsplus, the fork and
# commit Bitcoin Core builds) for Linux: built statically from pinned sources, dmg.exe too. xorriso.exe with the
# msys-*.dll it loads: pinned MSYS2 packages (xorriso has no native Windows build).
set -eu
RCODESIGN=0.29.0
XORRISO=1.5.8.pl02
LIBDMG=1cc791e4173da9cb0b0cc16c5a1aaa25d5eb5efa
MSYS="xorriso-1.5.8.2-1 msys2-runtime-3.6.12-1 zlib-1.3.2-1 libbz2-1.0.8-4 libiconv-1.19-1 libreadline-8.3.006-2 ncurses-6.6-2"
export DEBIAN_FRONTEND=noninteractive
dpkg --add-architecture arm64
apt-get update -qq
apt-get install -y -qq --no-install-recommends ca-certificates curl unzip zstd cmake make gcc libc6-dev zlib1g-dev \
  gcc-aarch64-linux-gnu libc6-dev-arm64-cross zlib1g-dev:arm64 gcc-mingw-w64-x86-64 libz-mingw-w64-dev > /dev/null
mkdir -p /work /tools/linux-x86_64 /tools/linux-aarch64 /tools/windows-x86_64 && cd /work

rcodesign() { # $1: target folder, $2: release archive, $3: its SHA-256
  curl -fsSLO https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign/$RCODESIGN/$2
  echo "$3  $2" | sha256sum -c - > /dev/null
  case $2 in *.zip) unzip -q "$2" ;; *) tar -xzf "$2" ;; esac
  cp apple-codesign-*/rcodesign* "/tools/$1/"
  cp apple-codesign-*/COPYING /tools/COPYING.rcodesign # MPL-2.0 and the licenses of the crates it is built from
  rm -rf apple-codesign-* "$2"
}
rcodesign linux-x86_64 apple-codesign-$RCODESIGN-x86_64-unknown-linux-musl.tar.gz dbe85cedd8ee4217b64e9a0e4c2aef92ab8bcaaa41f20bde99781ff02e600002
rcodesign linux-aarch64 apple-codesign-$RCODESIGN-aarch64-unknown-linux-musl.tar.gz 4af92c87ddf52f5f2d1258a3b4e56c7dcb8f1b2468df744976c5f139e031961f
rcodesign windows-x86_64 apple-codesign-$RCODESIGN-x86_64-pc-windows-msvc.zip 54bb500e2da7a8de02fcae0f331d1cac6e6d7173b4281042ff9c528ba3159aaa

curl -fsSL https://ftp.gnu.org/gnu/xorriso/xorriso-$XORRISO.tar.gz | tar -xz
xorriso() { # $1: target folder, the rest: configure options
  out=/tools/$1; shift
  rm -rf build && mkdir build
  (cd build && ../xorriso-1.5.8/configure -q LDFLAGS="-static -s" --disable-libreadline --disable-libedit \
    --disable-libacl --disable-xattr --disable-zlib --disable-libbz2 --disable-libcdio "$@" > /dev/null && make -s > /dev/null)
  cp build/xorriso/xorriso "$out/"
}
xorriso linux-x86_64
xorriso linux-aarch64 --host=aarch64-linux-gnu

curl -fsSL https://github.com/fanquake/libdmg-hfsplus/archive/$LIBDMG.tar.gz | tar -xz
sed -i 's/^project (libdmg-hfsplus)/project (libdmg-hfsplus C)/' libdmg-hfsplus-$LIBDMG/CMakeLists.txt # C only, no C++ compiler needed
cp libdmg-hfsplus-$LIBDMG/LICENSE /tools/COPYING # GPL-3.0: libdmg-hfsplus, xorriso, readline
dmg() { # $1: target folder, the rest: cmake options
  out=/tools/$1; shift
  rm -rf build
  cmake -S libdmg-hfsplus-$LIBDMG -B build -DCMAKE_BUILD_TYPE=Release -DCMAKE_EXE_LINKER_FLAGS="-static -s" "$@" > /dev/null
  make -s -C build > /dev/null
  cp build/dmg/dmg* "$out/"
}
dmg linux-x86_64
dmg linux-aarch64 -DCMAKE_SYSTEM_NAME=Linux -DCMAKE_SYSTEM_PROCESSOR=aarch64 -DCMAKE_C_COMPILER=aarch64-linux-gnu-gcc \
  -DZLIB_LIBRARY=/usr/lib/aarch64-linux-gnu/libz.a -DZLIB_INCLUDE_DIR=/usr/include
dmg windows-x86_64 -DCMAKE_SYSTEM_NAME=Windows -DCMAKE_C_COMPILER=x86_64-w64-mingw32-gcc \
  -DZLIB_LIBRARY=/usr/x86_64-w64-mingw32/lib/libz.a -DZLIB_INCLUDE_DIR=/usr/x86_64-w64-mingw32/include

mkdir msys && cd msys
for p in $MSYS; do curl -fsSL https://repo.msys2.org/msys/x86_64/$p-x86_64.pkg.tar.zst | tar --zstd -x usr/bin; done
cd usr/bin
cp xorriso.exe msys-2.0.dll msys-z.dll msys-bz2-1.dll msys-iconv-2.dll msys-readline8.dll msys-ncursesw6.dll /tools/windows-x86_64/
cd /tools && find . -type f -path './*-*/*' | sort | xargs sha256sum
