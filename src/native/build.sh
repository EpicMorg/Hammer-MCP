#!/bin/bash
# Linux cross-build of the native part (needs clang-cl, lld-link, llvm-rc and xwin's CRT/SDK in /opt/xwin):
#   src/native/build.sh            -> build/native-x86 and build/native-x86_64 (Release)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
for arch in x86 x86_64; do
  cmake -S "$ROOT/src/native" -B "$ROOT/build/native-$arch" -G "Unix Makefiles" \
    -DCMAKE_TOOLCHAIN_FILE="$ROOT/src/native/cmake/clang-cl-xwin.cmake" -DHMCP_ARCH=$arch -DCMAKE_BUILD_TYPE=Release
  cmake --build "$ROOT/build/native-$arch" -j"$(nproc)"
done
ls -l "$ROOT"/build/native-*/*.{exe,dll} 2>/dev/null
