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
EDITIONS = {'win-x64': 'Windows x64', 'linux-x64': 'Linux x64', 'osx-x64': 'macOS Intel', 'osx-arm64': 'macOS Apple Silicon'}

def run(*args):
    subprocess.run([str(a) for a in args], cwd=ROOT, check=True, env={**os.environ, 'MSBUILDDISABLENODEREUSE': '1', 'DOTNET_CLI_USE_MSBUILD_SERVER': '0'})

def deployment_root():
    return ROOT.parents[1] / 'Deployments/LUZ Civic Terminal' if ROOT.parent.name == 'Projects' else ROOT / 'dist'

def retain_one(dist, current, pattern):
    root = dist.resolve()
    for old in dist.glob(pattern):
        if old.name == current.name or old.resolve().parent != root or old.is_symlink(): continue
        old.unlink()

def package_vortex_extension():
    extension = ROOT / 'VortexExtension'
    metadata = json.loads((extension / 'package.json').read_text(encoding='utf8'))
    extension_version = metadata['version']
    run('npm.cmd' if os.name == 'nt' else 'npm', '--prefix', extension, 'run', 'package')
    run(sys.executable, ROOT / 'tools/release_check.py', extension / 'dist')
    source = extension / 'release' / ('game-nivalisnights-luz-' + extension_version + '.zip')
    if not source.is_file(): raise RuntimeError('Vortex extension package was not created')
    dist = deployment_root(); dist.mkdir(parents=True, exist_ok=True)
    destination = dist / source.name
    with tempfile.TemporaryDirectory(prefix='.luz-vortex-export-', dir=dist) as pending:
        candidate = Path(pending) / source.name
        shutil.copy2(source, candidate)
        run(sys.executable, ROOT / 'tools/release_check.py', Path(pending))
        with candidate.open('rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        checksum = candidate.with_name(candidate.name + '.sha256')
        checksum.write_text(digest + '  ' + destination.name + '\n', encoding='utf8')
        os.replace(candidate, destination)
        os.replace(checksum, destination.with_name(destination.name + '.sha256'))
    retain_one(dist, destination, 'game-nivalisnights-*.zip')
    retain_one(dist, destination.with_name(destination.name + '.sha256'), 'game-nivalisnights-*.zip.sha256')
    retain_one(dist, destination, 'game-nivalisnights-luz-bridge-*.zip')
    retain_one(dist, destination.with_name(destination.name + '.sha256'), 'game-nivalisnights-luz-bridge-*.zip.sha256')
    print('Packaged:', destination, '\nSHA256:', digest, flush=True)

def package_record(path, product, edition):
    with path.open('rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    with zipfile.ZipFile(path) as archive: files = len([entry for entry in archive.infolist() if not entry.is_dir()])
    return {'product': product, 'version': VERSION, 'edition': edition, 'private': False,
            'file': 'LUZ Civic Terminal/' + path.name, 'sha256': digest, 'files': files, 'bytes': path.stat().st_size}

def write_deployment_handoff():
    dist = deployment_root()
    records = [package_record(dist / ('LUZ-Civic-Terminal-' + VERSION + '-' + rid + '.zip'), APP, EDITIONS[rid]) for rid in RIDS]
    extension_version = json.loads((ROOT / 'VortexExtension/package.json').read_text(encoding='utf8'))['version']
    bridge = dist / ('game-nivalisnights-luz-' + extension_version + '.zip')
    records.append(package_record(bridge, 'LUZ for Nivalis Nights', 'Vortex 2.9+ on Windows'))
    records[-1]['version'] = extension_version
    (dist / 'PACKAGE RECORD.json').write_text(json.dumps(records, indent=2) + '\n', encoding='utf8')
    status = {'version': VERSION, 'published': False, 'gameBuild': 26118043, 'automatedValidation': 'passed',
              'nativeVortexAcceptance': False, 'gameplayAcceptance': False, 'packages': records, 'releaseUrl': None}
    (dist / 'PACKAGE STATUS.json').write_text(json.dumps(status, indent=2) + '\n', encoding='utf8')
    for name in ('README.md', 'QUICK START.txt', 'PLAYER-GUIDE.md', 'CORPSEC-GUIDE.md', 'BUILDING.md', 'CHANGELOG.md', 'RELEASE-NOTES.md'):
        shutil.copy2(ROOT / name, dist / name)
    (dist / 'START HERE.md').write_text(
        '# LUZ Civic Terminal ' + VERSION + '\n\nThe four platform packages and the Windows Vortex bridge in this folder are the current locally validated candidates. '
        'They have not been published. Windows x64 is the main desktop build; Linux and macOS remain experimental.\n\n'
        'Install LUZ for Nivalis Nights in Vortex and choose Manage. It installs or finds LUZ and connects automatically. Use the adjacent package record for exact hashes.\n', encoding='utf8')
    (dist / 'OWNER QA.md').write_text(
        '# LUZ Civic Terminal ' + VERSION + ' owner acceptance\n\n'
        '1. With the game closed, choose Maintenance > Prepare saved-data recovery and select a disposable save with a valid older Unofficial Patch manager companion. Expected: the preview names a separate recovered slot, says native progress and originals are preserved, and identifies the manager/accounting, Inspiration and restaurant-review reset.\n'
        '2. Cancel the preview, then select the save again. Expected: the same recovery remains available and cancellation has written no destination save.\n'
        '3. Confirm the preview, start Nivalis yourself, and choose Settings > Mods > Recover prepared save from the idle main menu. Expected: a separate recovered timeline is published; the original slot remains untouched.\n'
        '4. Save and reload the recovered timeline. Expected: native progress remains usable and its fresh Unofficial Patch companion loads normally.\n'
        '5. Update a known mod in Vortex and press Play. Expected: LUZ opens, applies the update, and remains at its launcher.\n'
        '6. Repeat with a disabled mod and a pinned mod. Expected: disabled remains disabled; pinned remains unchanged and is reported.\n'
        '7. Repeat Play without changes. Expected: no duplicate entry, deployment, or restore point.\n', encoding='utf8')
    nexus = dist / 'Nexus'; nexus.mkdir(exist_ok=True)
    for source in (ROOT / 'Store/Nexus').iterdir():
        if source.is_file(): shutil.copy2(source, nexus / source.name)
    shutil.copy2(ROOT / 'CHANGELOG.md', nexus / 'CHANGELOG.md')
    shutil.copy2(ROOT / 'README.md', nexus / 'DESCRIPTION.md')
    shutil.copy2(ROOT / 'README.md', nexus / 'README.md')
    shutil.copy2(ROOT / 'Store/Nexus/UPLOAD INSTRUCTIONS.txt', nexus / 'UPLOAD.txt')
    checksums = ''.join(record['sha256'] + '  ' + Path(record['file']).name + '\n' for record in records)
    (nexus / 'SHA256SUMS.txt').write_text(checksums, encoding='utf8')
    (nexus / 'CHECKSUMS.sha256').write_text(checksums, encoding='utf8')
    (dist / 'UPLOAD.txt').write_text((ROOT / 'Store/Nexus/UPLOAD INSTRUCTIONS.txt').read_text(encoding='utf8'), encoding='utf8')

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
    from build_continuity import digest
    payload = ROOT / 'Continuity/Payload'
    manifest = json.loads((payload / 'payload.json').read_text())
    if set(manifest) != {'Luz.Continuity.Runtime.dll', 'Luz.Continuity.dll', 'catalog.json', 'maintained-support.json'} or any(digest(payload / name) != value for name, value in manifest.items()):
        raise RuntimeError('Continuity bundle is incomplete or changed; build it before packaging')
    dist = deployment_root()
    dist.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='luz-package-') as temp:
        staging = Path(temp)
        folder = staging / (APP + ' ' + VERSION + ' ' + rid); folder.mkdir()
        bundle = folder / (APP + '.app')
        publish = bundle / 'Contents/MacOS' if rid.startswith('osx-') else folder
        run('dotnet', 'publish', ROOT / 'Desktop/Desktop.csproj', '-c', 'Release', '-r', rid,
            '-p:RenderTools=false', '-p:UseSharedCompilation=false', '-m:1', '--self-contained', 'true', '-o', publish, '--nologo', '-v', 'minimal')
        for symbols in publish.rglob('*.pdb'): symbols.unlink()
        verify_target(publish, rid)
        notices(publish)
        for name in ('README.md', 'QUICK START.txt', 'PLAYER-GUIDE.md', 'CORPSEC-GUIDE.md',
                     'BUILDING.md', 'CHANGELOG.md', 'RELEASE-NOTES.md'):
            shutil.copy2(ROOT / name, folder / name)
        if rid == 'win-x64':
            with tempfile.TemporaryDirectory(prefix='luz-handoff-') as helper_stage:
                helper = Path(helper_stage)
                run('dotnet', 'publish', ROOT / 'VortexHandoff/VortexHandoff.csproj', '-c', 'Release', '-r', rid,
                    '-p:UseSharedCompilation=false', '-m:1', '--self-contained', 'true', '-o', helper, '--nologo', '-v', 'minimal')
                for name in ('LUZ Vortex.exe', 'LUZ Vortex.dll', 'LUZ Vortex.deps.json', 'LUZ Vortex.runtimeconfig.json'):
                    shutil.copy2(helper / name, folder / name)
            (folder / 'luz-vortex-package.json').write_text(json.dumps({
                'schema': 1, 'product': APP, 'gameId': 'nivalisnights', 'version': VERSION, 'platform': rid
            }, indent=2) + '\n', encoding='utf8')
        (folder / 'Continuity').mkdir(exist_ok=True)
        shutil.copy2(ROOT / 'Continuity/README.md', folder / 'Continuity/README.md')
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
               'Cross-built macOS app. Run Prepare macOS.command on the target Mac. Not notarized.\n' if rid.startswith('osx-') else 'Unsigned application build.\n')
            + 'Protected saves require native Windows, a supported game/loader build and continuity-compatible mods. See Continuity/README.md.\n', encoding='utf8')
        run(sys.executable, ROOT / 'tools/release_check.py', folder)
        archive = dist / ('LUZ-Civic-Terminal-' + VERSION + '-' + rid + '.zip')
        # Validate a complete replacement before touching the current usable ZIP.
        with tempfile.TemporaryDirectory(prefix='.luz-export-', dir=dist) as pending:
            candidate = Path(pending) / archive.name
            archive_folder(staging, folder, candidate)
            if rid == 'win-x64':
                with zipfile.ZipFile(candidate) as packaged:
                    marker = folder.name + '/luz-vortex-package.json'
                    if marker not in packaged.namelist(): raise RuntimeError('Windows package is missing its Vortex identity marker')
            with candidate.open('rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
            checksum = candidate.with_name(candidate.name + '.sha256')
            checksum.write_text(digest + '  ' + archive.name + '\n', encoding='utf8')
            os.replace(candidate, archive)
            os.replace(checksum, archive.with_name(archive.name + '.sha256'))
        retain_one(dist, archive, 'LUZ-Civic-Terminal-*-' + rid + '.zip')
        retain_one(dist, archive.with_name(archive.name + '.sha256'), 'LUZ-Civic-Terminal-*-' + rid + '.zip.sha256')
        print('Packaged:', archive, '\nSHA256:', digest, flush=True)

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--rid', choices=(*RIDS, 'all'), default='all')
    parser.add_argument('--loader', type=Path, help='Pinned official loader compiler references; nothing is installed')
    parser.add_argument('--object-framework', action='store_true', help='Include the real sibling Object Framework author-workflow check')
    arguments = parser.parse_args()
    from build_continuity import build
    build(arguments.loader)
    from validate import validate
    validate(arguments.object_framework)
    if arguments.rid in ('win-x64', 'all'): package_vortex_extension()
    for target in RIDS if arguments.rid == 'all' else (arguments.rid,): package(target)
    if arguments.rid == 'all': write_deployment_handoff()
