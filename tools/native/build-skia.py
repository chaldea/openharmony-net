"""Build the pinned SkiaSharp C APIs with the official HarmonyOS API15 NDK.

Only the dependencies used by this configuration are fetched. All revisions come
from the pinned upstream DEPS; no host or Android libraries are used.
"""
import argparse
import concurrent.futures
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = ROOT / 'dotnet/SkiaSharp'
OUT = ROOT / 'artifacts/native/skia'
SKIASHARP_COMMIT = '2a53a3c8641073bc914c63ee158bbf62b8a0bb92'
SKIA_COMMIT = '51cb594d773b5383a45065ea3c8cbec398ad2c36'
SOURCE = OUT / ('source-' + SKIA_COMMIT[:12])
DEPENDENCIES = ('expat', 'freetype', 'harfbuzz', 'libjpeg-turbo', 'libpng',
                'libwebp', 'zlib')
MIRRORS = {
    'freetype': 'https://chromium.googlesource.com/chromium/src/third_party/freetype2',
    'libpng': 'https://github.com/glennrp/libpng.git',
}


def run(*args, cwd=ROOT):
    subprocess.run([str(a) for a in args], cwd=cwd, check=True)


def capture(*args, cwd=ROOT):
    return subprocess.check_output([str(a) for a in args], cwd=cwd, text=True).strip()


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def sync_dependency(item):
    relative, spec = item
    url, revision = spec.rsplit('@', 1)
    directory = SOURCE / relative
    url = MIRRORS.get(directory.name, url)
    if not (directory / '.git').exists():
        directory.mkdir(parents=True, exist_ok=True)
        run('git', 'init', '--quiet', directory)
        run('git', 'remote', 'add', 'origin', url, cwd=directory)
    if subprocess.run(['git', 'cat-file', '-e', revision], cwd=directory,
                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
        run('git', 'fetch', '--depth', '1', url, revision, cwd=directory)
    status = capture('git', 'status', '--porcelain', cwd=directory)
    if status:
        # Upstream GN prepends WinRTCompat.h even for non-Windows targets.
        # Accept exactly that generated change, but never arbitrary source edits.
        for line in status.splitlines():
            path = line.strip().removeprefix('M ')
            allowed = {'libpng': ['pngpriv.h'], 'zlib': ['deflate.c'],
                       'libjpeg-turbo': [f'simd/jsimd_{cpu}.c' for cpu in ['x86_64', 'i386', 'arm', 'arm64']]}
            if line.strip() != 'M ' + path or path not in allowed.get(directory.name, []):
                raise RuntimeError(f'Dirty dependency: {directory}: {line}')
            original = subprocess.check_output(['git', 'show', f'{revision}:{path}'], cwd=directory).decode('utf-8').replace('\r\n', '\n')
            prefix = '../' * (relative + '/' + path).count('/')
            expected = f'#include "{prefix}include/xamarin/WinRTCompat.h"\n' + original
            if (directory / path).read_text(encoding='utf-8') != expected:
                raise RuntimeError(f'Unexpected dependency edit: {directory / path}')
    run('git', 'checkout', '--quiet', '--detach', revision, cwd=directory)
    print(f'{directory.name}: {revision}', flush=True)


def prepare_source():
    for path, commit in ((UPSTREAM, SKIASHARP_COMMIT), (UPSTREAM / 'externals/skia', SKIA_COMMIT)):
        if capture('git', 'rev-parse', 'HEAD', cwd=path) != commit:
            raise RuntimeError(f'Unexpected upstream revision: {path}. Initialize the pinned submodules.')
        if capture('git', 'diff', 'HEAD', '--', '.', ':(exclude)externals', ':(exclude)docs', cwd=path):
            raise RuntimeError(f'Upstream source has local changes: {path}')
    OUT.mkdir(parents=True, exist_ok=True)
    marker = SOURCE / '.source-revision'
    if not marker.exists():
        archive = OUT / 'source.tar'
        run('git', 'archive', '--format=tar', '-o', archive, SKIA_COMMIT, cwd=UPSTREAM / 'externals/skia')
        SOURCE.mkdir(exist_ok=True)
        with tarfile.open(archive) as contents:
            contents.extractall(SOURCE, filter='data')
        marker.write_text(SKIA_COMMIT)
    if marker.read_text() != SKIA_COMMIT:
        raise RuntimeError('Source cache revision changed. Use a fresh artifacts/native/skia directory.')
    # API15's libc++ does not implement C++20 parenthesized aggregate
    # construction. Materialize the same aggregate before optional::emplace.
    pdf_tag = SOURCE / 'src/pdf/SkPDFTag.cpp'
    original = subprocess.check_output(['git', 'show', f'{SKIA_COMMIT}:src/pdf/SkPDFTag.cpp'],
                                       cwd=UPSTREAM / 'externals/skia').decode('utf-8')
    if original.count('fData.emplace(ci, ci);') != 1:
        raise RuntimeError('The pinned Skia PDF compatibility patch needs review.')
    patched = original.replace('fData.emplace(ci, ci);', 'fData.emplace(Data{ci, ci});')
    if pdf_tag.read_text() not in (original, patched):
        raise RuntimeError('Unexpected changes in the cached Skia PDF source.')
    pdf_tag.write_text(patched, encoding='utf-8')
    deps = {}
    deps['Var'] = lambda name: deps['vars'][name]
    exec((SOURCE / 'DEPS').read_text(), deps)
    selected = {p: spec for p, spec in deps['deps'].items() if p.split('/')[-1] in DEPENDENCIES}
    if len(selected) != len(DEPENDENCIES):
        raise RuntimeError('Pinned dependency list is incomplete.')
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        list(pool.map(sync_dependency, selected.items()))
    if not (SOURCE / 'bin/gn.exe').exists():
        run(sys.executable, SOURCE / 'bin/fetch-gn', cwd=SOURCE)
    return selected


def gn_value(value):
    # JSON strings/lists are also valid GN literals for these values.
    return json.dumps(value)


def build(sdk, arch, ninja, jobs, dependencies):
    abi, triple, machine = ('x86_64', 'x86_64-linux-ohos', 62) if arch == 'x64' else ('arm64-v8a', 'aarch64-linux-ohos', 183)
    llvm = sdk / 'llvm/bin'
    common = [f'--target={triple}', f'--sysroot={sdk.as_posix()}/sysroot', '-DOHOS']
    config = dict(
        target_os='linux', target_cpu=arch, is_official_build=True,
        cc=(llvm / 'clang.exe').as_posix(), cxx=(llvm / 'clang++.exe').as_posix(),
        ar=(llvm / 'llvm-ar.exe').as_posix(),
        skia_enable_tools=False, skia_enable_ganesh=True, skia_use_egl=True,
        skia_gl_standard='gles', skia_use_x11=False, skia_use_vulkan=False,
        skia_use_fontconfig=False, skia_use_icu=False, skia_use_piex=False,
        skia_use_dng_sdk=False, skia_use_wuffs=False,
        skia_use_harfbuzz=False, skia_use_partition_alloc=False,
        skia_enable_graphite=False,
        skia_use_system_expat=False, skia_use_system_freetype2=False,
        skia_use_system_libjpeg_turbo=False,
        skia_use_system_libpng=False, skia_use_system_libwebp=False, skia_use_system_zlib=False,
        skia_enable_skottie=True, visibility_hidden=False,
        extra_cflags=common + ['-DSKIA_C_DLL', '-DHAVE_SYSCALL_GETRANDOM', '-DXML_DEV_URANDOM',
                              '-DSK_FONT_FILE_PREFIX="/system/fonts/"'],
        extra_asmflags=common,
        extra_ldflags=common + ['-static-libstdc++', '-Wl,--no-undefined', '-Wl,-z,max-page-size=16384',
                               f'-Wl,--version-script={ROOT.as_posix()}/tools/native/skia.exports'],
    )
    build_dir = SOURCE / f'out/ohos-{arch}'
    build_dir.mkdir(parents=True, exist_ok=True)
    args = '\n'.join(f'{key} = {gn_value(value)}' for key, value in config.items()) + '\n'
    (build_dir / 'args.gn').write_text(args)
    run(SOURCE / 'bin/gn.exe', 'gen', build_dir, f'--script-executable={sys.executable}', cwd=SOURCE)
    run(ninja, '-C', build_dir, '-j', jobs, 'SkiaSharp', 'HarfBuzzSharp', cwd=SOURCE)
    destination = OUT / abi
    destination.mkdir(exist_ok=True)
    files = []
    for name, symbol in [('libSkiaSharp.so', 'sk_version_get_milestone'), ('libHarfBuzzSharp.so', 'hb_shape')]:
        library = build_dir / name
        data = library.read_bytes()
        if data[:5] != b'\x7fELF\x02' or int.from_bytes(data[18:20], 'little') != machine:
            raise RuntimeError(f'Incorrect ELF architecture: {library}')
        symbols = capture(llvm / 'llvm-nm.exe', '-D', '--defined-only', library)
        if not re.search(r'\b' + symbol + r'\b', symbols):
            raise RuntimeError(f'Missing native API: {symbol}')
        dynamic = capture(llvm / 'llvm-readelf.exe', '-d', library)
        needed = re.findall(r'\(NEEDED\).*?\[(.*?)\]', dynamic)
        allowed = {'libc.so', 'libm.so', 'libdl.so', 'libEGL.so', 'libGLESv2.so', 'libGLESv3.so'}
        if set(needed) - allowed:
            raise RuntimeError(f'Unexpected dynamic dependencies in {name}: {needed}')
        shutil.copy2(library, destination / name)
        files.append(dict(name=name, sha256=sha256(library), needed=needed))
    manifest = dict(schemaVersion=1, abi=abi, targetTriple=triple, apiLevel=15,
                    skiaSharpVersion='4.157.0', harfBuzzSharpVersion='14.4.0.200',
                    skiaSharpCommit=SKIASHARP_COMMIT, skiaCommit=SKIA_COMMIT,
                    sdk=json.loads((sdk / 'oh-uni-package.json').read_text()),
                    sdkManifestSha256=sha256(sdk / 'oh-uni-package.json'),
                    buildScriptSha256=sha256(Path(__file__)), gnArgs=args,
                    dependencies=dependencies, files=files)
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')


def licenses(dependencies, sdk):
    parts = []
    for title, directory in [('SkiaSharp', UPSTREAM), ('Skia', SOURCE)] + [(p, SOURCE / p) for p in dependencies]:
        candidates = sorted({p for pattern in ('LICENSE*', 'COPYING*', 'docs/FTL.TXT', 'README.ijg') for p in directory.glob(pattern) if p.is_file()})
        if not candidates:
            raise RuntimeError(f'License missing: {title}')
        for path in candidates:
            parts.append(f'===== {title}: {path.name} =====\n{path.read_text(encoding="utf-8", errors="replace")}')
    parts.append('===== HarmonyOS SDK (including static libc++ runtime) =====\n' +
                 (sdk / 'NOTICE.txt').read_text(encoding='utf-8'))
    (OUT / 'THIRD-PARTY-NOTICES.txt').write_text('\n\n'.join(parts), encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk-root', type=Path, required=True)
    parser.add_argument('--architectures', nargs='+', choices=['x64', 'arm64'], default=['x64', 'arm64'])
    parser.add_argument('--ninja', default='ninja')
    parser.add_argument('--jobs', type=int, default=8)
    args = parser.parse_args()
    sdk = args.sdk_root.resolve() / '15/native'
    if not (sdk / 'llvm/bin/clang.exe').is_file():
        raise RuntimeError(f'API15 Native SDK missing: {sdk}')
    dependencies = prepare_source()
    for arch in args.architectures:
        build(sdk, arch, args.ninja, args.jobs, dependencies)
    licenses(dependencies, sdk)


if __name__ == '__main__':
    main()
