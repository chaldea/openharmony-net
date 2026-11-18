#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$1" && pwd)
export PATH="$(dirname "$(command -v cl)"):$root/msys/usr/bin:$PATH"
mkdir -p "$root/icu-host"
cd "$root/icu-host"
CXXFLAGS='-std:c++17' "$root/icu-source/icu/source/runConfigureICU" MSYS/MSVC \
  --disable-tests --disable-samples --disable-extras --with-data-packaging=static
make -j8
