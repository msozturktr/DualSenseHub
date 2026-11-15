#!/usr/bin/env bash
# Builds dualsensehub_ets2.dll (Windows x64) with clang + lld-link + llvm-dlltool; no mingw/CRT needed.
set -euo pipefail
cd "$(dirname "$0")"
OUT=build
mkdir -p "$OUT"
llvm-dlltool -m i386:x86-64 -d ws2_32.def -l "$OUT/ws2_32.lib"
clang --target=x86_64-pc-windows-msvc -ffreestanding -fno-builtin -nostdlib -fno-stack-protector \
  -O2 -Wall -Wextra -c dualsensehub_ets2.c -o "$OUT/dualsensehub_ets2.obj"
lld-link /dll /nodefaultlib /entry:DllMain /subsystem:windows /out:dualsensehub_ets2.dll \
  "$OUT/dualsensehub_ets2.obj" "$OUT/ws2_32.lib"
echo "Built $(pwd)/dualsensehub_ets2.dll"
