#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$1" && pwd)
sdk=$(cd "$2" && pwd)
arch=$3
triple=x86_64-linux-ohos
host=x86_64-linux-musl
if [ "$arch" = arm64 ]; then triple=aarch64-linux-ohos; host=aarch64-linux-musl; fi
export PATH="$root/msys/usr/bin:$root/icu-host/lib:$root/icu-host/bin:$sdk/llvm/bin:$PATH"
export CC="$sdk/llvm/bin/clang --target=$triple --sysroot=$sdk/sysroot"
export CXX="$sdk/llvm/bin/clang++ --target=$triple --sysroot=$sdk/sysroot"
export AR="$sdk/llvm/bin/llvm-ar"
export RANLIB="$sdk/llvm/bin/llvm-ranlib"
export CFLAGS='-O2 -fPIC'
export CXXFLAGS='-O2 -fPIC -std=c++17'
export LDFLAGS="-L$sdk/llvm/lib/$triple"
mkdir -p "$root/icu-$arch" "$root/icu-install-$arch/lib"
cd "$root/icu-$arch"
"$root/icu-source/icu/source/configure" --host="$host" --with-cross-build="$root/icu-host" \
  --disable-tests --disable-samples --disable-extras --enable-shared --disable-static --with-data-packaging=library
make -j8
# The Windows pkgdata install step expects an MSVC import library even for ELF.
# Stage the ELF files directly and keep the runtime dependency contract explicit.
for library in icuuc icui18n; do
  cp "lib/lib$library.so.78.3" "$root/icu-install-$arch/lib/lib$library.so.78"
done
$CC -shared -nodefaultlibs -nostdlib data/out/tmp/icudt78l_dat.o \
  -Wl,-soname,libicudata.so.78 -Wl,--no-as-needed -lc \
  -o "$root/icu-install-$arch/lib/libicudata.so.78"
