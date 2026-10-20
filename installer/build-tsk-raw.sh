#!/usr/bin/env bash
# Run in Git Bash on Windows with the pinned WinLibs toolchain on PATH.
# Usage: bash build-tsk-raw.sh /path/to/sleuthkit-4.15.0 /new/output
# Sources are unmodified. Only RAW image support is required by Revivo.
set -euo pipefail
source_dir=$(cd "$1" && pwd)
output_dir=$2
mkdir -p "$output_dir"
output_dir=$(cd "$output_dir" && pwd)
cd "$source_dir"
MAKE=mingw32-make CC=gcc CXX=g++ CFLAGS='-O2' CXXFLAGS='-O2' \
LDFLAGS='-static -static-libgcc -static-libstdc++' ./configure \
  --host=x86_64-w64-mingw32 --disable-shared --enable-static \
  --disable-java --disable-cppunit --without-afflib --without-libbfio \
  --without-libewf --without-libvhdi --without-libvmdk --without-libvslvm \
  --without-zlib
flags=(CFLAGS='-O2 -std=gnu11 -Wno-format' CXXFLAGS='-O2 -std=gnu++17')
mingw32-make -C tsk -j4 "${flags[@]}"
mingw32-make -C tools/fstools -j4 "${flags[@]}" fls.o icat.o
mingw32-make -C tools/vstools -j4 "${flags[@]}" mmls.o
# Direct static links avoid libtool introducing a dynamic libstdc++ import.
g++ -static -o "$output_dir/fls.exe" tools/fstools/fls.o tsk/.libs/libtsk.a
g++ -static -o "$output_dir/icat.exe" tools/fstools/icat.o tsk/.libs/libtsk.a
g++ -static -o "$output_dir/mmls.exe" tools/vstools/mmls.o tsk/.libs/libtsk.a
