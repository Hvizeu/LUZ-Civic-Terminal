"""Publish desktop packages without running the application or game."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import shutil
import stat
import struct
import subprocess
import sys
import tempfile
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
VERSION = ET.parse(ROOT / 'Desktop/Desktop.csproj').findtext('.//Version')
APP = 'LUZ Civic Terminal'
RIDS = ('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')

def run(*args):
    subprocess.run([str(a) for a in args], cwd=ROOT, check=True)

def notices(publish):
    destination = publish / 'Notices'
    shutil.copytree(ROOT / 'tools/notices', destination)
    packages = Path(os.environ.get('NUGET_PACKAGES', Path.home() / '.nuget/packages'))
    deps = json.loads((publish / (APP + '.deps.json')).read_text())
    records = []
    for identity, library in deps['libraries'].items():
        if library['type'] not in ('package', 'runtimepack'): continue
        name, version = identity.split('/')
        name = name.removeprefix('runtimepack.')
        folder = packages / name.lower() / version
        if not folder.is_dir(): raise RuntimeError('Missing dependency package: ' + name)
        records.append(name + ' ' + version)
        for path in folder.iterdir():
            if path.is_file() and any(word in path.name.upper() for word in ('LICENSE', 'LICENCE', 'NOTICE')):
                shutil.copy2(path, destination / (name + '-' + path.name))
    (destination / 'Dependencies.txt').write_text('\n'.join(sorted(records)) + '\n', encoding='utf8')
    for prefix in ('Microsoft.NETCore.App.Runtime', 'SkiaSharp.NativeAssets', 'HarfBuzzSharp.NativeAssets'):
        if not any(p.name.startswith(prefix) and 'NOTICES' in p.name.upper() for p in destination.iterdir()):
            raise RuntimeError('Missing native dependency notices: ' + prefix)

def executable(path):
    return path.name in (APP, 'Install LUZ', 'createdump') or path.suffix in ('.so', '.dylib', '.command')

def modes(folder):
    for path in folder.rglob('*'):
        path.chmod(0o755 if path.is_dir() or executable(path) else 0o644)

def verify_target(publish, rid):
    data = (publish / (APP + ('.exe' if rid.startswith('win-') else ''))).read_bytes()
    if rid == 'win-x64':
        offset = struct.unpack_from('<I', data, 0x3c)[0]
        valid = data[:2] == b'MZ' and data[offset:offset+4] == b'PE\0\0' and struct.unpack_from('<H', data, offset+4)[0] == 0x8664
    elif rid == 'linux-x64':
        valid = data[:5] == b'\x7fELF\x02' and struct.unpack_from('<H', data, 18)[0] == 62
    else:
        magic, cpu = struct.unpack_from('<II', data)
        valid = magic == 0xfeedfacf and cpu == (0x100000c if rid == 'osx-arm64' else 0x1000007)
    if not valid: raise RuntimeError('Application host architecture does not match ' + rid)
    deps = json.loads((publish / (APP + '.deps.json')).read_text())
    if not deps['runtimeTarget']['name'].endswith('/' + rid): raise RuntimeError('Dependency target mismatch')

def sign_macos(bundle):
    for path in (bundle / 'Contents/MacOS').iterdir():
        if path.is_file() and executable(path): run('/usr/bin/codesign', '--force', '--sign', '-', path)
    run('/usr/bin/codesign', '--force', '--sign', '-', bundle)
    run('/usr/bin/codesign', '--verify', '--deep', '--strict', bundle)


def archive_folder(staging, folder, archive):
    # Unix attributes keep Linux/macOS entry points executable after ZIP extraction.
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as output:
        for path in folder.rglob('*'):
            if not path.is_file(): continue
            info = zipfile.ZipInfo(path.relative_to(staging).as_posix())
            info.create_system = 3
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (stat.S_IFREG | (0o755 if executable(path) else 0o644)) << 16
            output.writestr(info, path.read_bytes())
    with zipfile.ZipFile(archive) as check:
        assert check.testzip() is None
        for entry in check.infolist():
            if executable(Path(entry.filename)):
                assert entry.create_system == 3 and (entry.external_attr >> 16) & 0o111

def package(rid):
    dist = ROOT / 'dist'; dist.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='luz-package-') as temp:
        staging = Path(temp)
        folder = staging / (APP + ' ' + VERSION + ' ' + rid); folder.mkdir()
        bundle = folder / (APP + '.app')
        publish = bundle / 'Contents/MacOS' if rid.startswith('osx-') else folder
        run('dotnet', 'publish', ROOT / 'Desktop/Desktop.csproj', '-c', 'Release', '-r', rid,
            '-p:RenderTools=false', '--self-contained', 'true', '-o', publish, '--nologo', '-v', 'minimal')
        for symbols in publish.rglob('*.pdb'): symbols.unlink()
        verify_target(publish, rid)
        notices(publish)
        shutil.copy2(ROOT / 'README.md', folder / 'README.md')
        shutil.copy2(ROOT / 'QUICK START.txt', folder / 'QUICK START.txt')
        if not rid.startswith('osx-'):
            suffix = '.exe' if rid.startswith('win-') else ''
            shutil.copy2(publish / (APP + suffix), publish / ('Install LUZ' + suffix))
        if rid.startswith('osx-'):
            (bundle / 'Contents/Resources').mkdir()
            shutil.copy2(ROOT / 'Desktop/Assets/luz-icon.icns', bundle / 'Contents/Resources/luz-icon.icns')
            shutil.copy2(ROOT / 'tools/Install LUZ.command', folder / 'Install LUZ.command')
            with (bundle / 'Contents/Info.plist').open('wb') as stream:
                plistlib.dump(dict(CFBundleName=APP, CFBundleDisplayName=APP, CFBundleExecutable=APP,
                    CFBundleIdentifier='tools.luz.civicterminal', CFBundleVersion=VERSION,
                    CFBundleShortVersionString=VERSION, CFBundlePackageType='APPL', CFBundleIconFile='luz-icon.icns',
                    LSMinimumSystemVersion='15.0', NSHighResolutionCapable=True,
                    NSPrincipalClass='NSApplication', CFBundleURLTypes=[dict(CFBundleURLName='Nexus download', CFBundleURLSchemes=['nxm'], CFBundleTypeRole='Viewer')]), stream)
            if sys.platform != 'darwin': shutil.copy2(ROOT / 'tools/Prepare macOS.command', folder / 'Prepare macOS.command')
        modes(folder)
        signed = rid.startswith('osx-') and sys.platform == 'darwin'
        if signed: sign_macos(bundle)
        (folder / 'BUILD-STATUS.txt').write_text(
            'LUZ Civic Terminal ' + VERSION + '\nTarget: ' + rid + '\n'
            + ('Locally ad-hoc signed on macOS; not notarized.\n' if signed else
               'Cross-built macOS app. Run Prepare macOS.command on the target Mac before testing. Not notarized.\n' if rid.startswith('osx-') else 'Unsigned preview build.\n')
            + 'A successful build does not establish native runtime or game compatibility. See README for acceptance steps.\n', encoding='utf8')
        run(sys.executable, ROOT / 'tools/release_check.py', folder)
        archive = dist / ('LUZ-Civic-Terminal-' + VERSION + '-' + rid + '.zip')
        archive_folder(staging, folder, archive)
        with archive.open('rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        archive.with_name(archive.name + '.sha256').write_text(digest + '  ' + archive.name + '\n')
        print('Packaged:', archive, '\nSHA256:', digest, flush=True)

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--rid', choices=(*RIDS, 'all'), default='all')
    arguments = parser.parse_args()
    from validate import validate
    validate()
    for target in RIDS if arguments.rid == 'all' else (arguments.rid,): package(target)
