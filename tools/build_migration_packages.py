"""Preserve complete maintained payloads while preparing local LUZ migration imports."""
from pathlib import Path
import hashlib
import json
import os
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT.parent

def verify_assembly_version(path, version):
    command = '[Reflection.AssemblyName]::GetAssemblyName($env:NIVALIS_ASSEMBLY_TO_CHECK).Version.ToString()'
    environment = os.environ.copy()
    environment['NIVALIS_ASSEMBLY_TO_CHECK'] = str(path)
    result = subprocess.run(['powershell', '-NoProfile', '-Command', command],
                            check=True, capture_output=True, text=True, env=environment)
    observed = result.stdout.strip()
    expected = version + '.0'
    if observed != expected:
        raise RuntimeError(f'Assembly identity mismatch for {path}: expected {expected}, observed {observed}')

def prepare(output, baseline, selected_products=None):
    sys.path.insert(0, str(SOURCE / 'NivalisSlowDays/tools'))
    from release_hygiene import validate_release_payload
    output = Path(output).resolve(); baseline = Path(baseline).resolve(); output.mkdir(parents=True, exist_ok=True)
    definitions = [
        ('Better Time', 'NivalisBetterTime-2.2.14-Nexus.zip', 'NivalisSlowDays', '2.2.14', '2.3.6', SOURCE / 'NivalisSlowDays/src-v2/bin/Continuity/net6.0/NivalisSlowDays.dll'),
        ('Unofficial Patch', 'NivalisUnofficialPatch-0.4.11-Nexus.zip', 'NivalisUnofficialPatch', '0.4.11', '0.5.5', SOURCE / 'NivalisUnofficialPatch/src/bin/ContinuityPublic/net6.0/NivalisUnofficialPatch.dll'),
        ('City Life', 'NivalisCityLife-1.2.11-Continuity-OwnerQA.zip', 'NivalisCityLife', '1.2.11', '1.2.11', SOURCE / 'NivalisCityLife/analysis/update6-1129-20261022/build/continuity/NivalisCityLife.dll'),
        ('Object Framework', 'Nivalis-Object-Framework-Runtime-0.3.14.zip', 'NivalisFurnishings', '0.3.14', '0.4.1', SOURCE / 'NivalisFurnishings/Loader/bin/ContinuityUpdate6/net6.0/NivalisFurnishings.dll'),
        ('Furniture Lab', 'Nivalis-Furniture-Lab-0.1.9.zip', 'NivalisFurnitureLab', '0.1.9', '0.2.1', SOURCE / 'NivalisFurnitureLab/src/bin/ContinuityUpdate6/net6.0/NivalisFurnitureLab.dll')]
    records = []
    checker = SOURCE / 'NivalisUnofficialPatch/tools/Bindings/bin/Release/net10.0/Bindings.dll'
    selected = set(selected_products) if selected_products is not None else None
    for name, original, assembly, old, version, dll in definitions:
        if selected is not None and name not in selected:
            continue
        subprocess.run(['dotnet', str(checker), '--verify-release-identity', str(dll), version], check=True)
        if assembly == 'NivalisFurnishings':
            verify_assembly_version(dll.parent / 'NivalisFurnishings.Core.dll', '0.3.7')
        matches = list(baseline.rglob(original))
        if len(matches) != 1: raise RuntimeError('Choose the complete maintained baseline for ' + name)
        with zipfile.ZipFile(matches[0]) as archive:
            if archive.testzip() is not None: raise RuntimeError('Corrupt baseline')
            files = {entry.filename: archive.read(entry) for entry in archive.infolist() if not entry.is_dir()}
        targets = [path for path in files if path.endswith('/' + assembly + '.dll')]
        if len(targets) != 1: raise RuntimeError('Ambiguous main payload: ' + name)
        files[targets[0]] = dll.read_bytes()
        if assembly == 'NivalisFurnishings':
            for path in list(files):
                if path.endswith('/NivalisFurnishings.Core.dll'): files[path] = (dll.parent / 'NivalisFurnishings.Core.dll').read_bytes()
        for path in list(files):
            if path.endswith('/' + assembly + '/manifest.json'):
                data = json.loads(files[path]); data['version_number'] = version
                data['description'] = data.get('description', '') + ' Continuity migration: requires LUZ 0.7.7 protected saves.'
                data['release_channel'] = 'Continuity'
                files[path] = (json.dumps(data, indent=2) + '\n').encode()
            if Path(path).name.lower().startswith('readme'):
                retained = files[path].decode('utf8').replace(old, version)
                if name == 'Unofficial Patch':
                    retained = retained.replace('Nivalis Expanded 0.2.x', 'Nivalis Expanded 0.3.x').replace('Expanded 0.2.x', 'Expanded 0.3.x')
                files[path] = (name + ' ' + version + '\nRequires LUZ Civic Terminal 0.7.7 with protected saves enabled. Import this complete archive in LUZ and Apply the profile. Existing assets and credits are retained below.\n\n' + retained).encode()
            if name == 'Unofficial Patch' and Path(path).name.lower().startswith('changelog'):
                files[path] = files[path].decode('utf8').replace('# 0.4.10', '# 0.5.5', 1).encode()
        files['VERSION.txt'] = (name + ' ' + version + '\nContinuity API 1; LUZ 0.7.7 required.\n').encode()
        allowed = (frozenset({'API/Example/BetterTimeApiExample.csproj', 'API/Example/ExamplePlugin.cs'})
                   if name == 'Better Time' else frozenset())
        validate_release_payload(files, allowed_development_files=allowed)
        suffix = '-LUZ-Migration-OwnerQA.zip' if name == 'City Life' else '-LUZ-Migration.zip'
        destination = output / (name.replace(' ', '-') + '-' + version + suffix)
        stage = destination.with_suffix('.partial')
        with zipfile.ZipFile(stage, 'w', zipfile.ZIP_DEFLATED) as archive:
            for path, data in files.items(): archive.writestr(path, data)
        with zipfile.ZipFile(stage) as archive:
            if archive.testzip() is not None or {entry.filename: archive.read(entry) for entry in archive.infolist()} != files: raise RuntimeError('Migration archive readback differs')
        stage.replace(destination)
        with destination.open('rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        destination.with_suffix('.zip.sha256').write_text(digest + '  ' + destination.name + '\n')
        records.append(dict(Product=name, Version=version, Archive=destination.name, Sha256=digest, SourceDllSha256=hashlib.sha256(dll.read_bytes()).hexdigest(), Optional=name == 'Furniture Lab'))

    # Expanded is a new product, so there is no earlier complete archive to use as
    # a payload baseline. Construct its package from an explicit public allowlist.
    # Unofficial Patch remains a separate package and owns the existing companions.
    if selected is None or 'Nivalis Expanded' in selected:
        name, version = 'Nivalis Expanded', '0.3.15'
        dll = SOURCE / 'NivalisExpanded/src/bin/ContinuityPublic/net6.0/NivalisExpanded.dll'
        prefix = 'BepInEx/plugins/NivalisExpanded/'
        manifest = {
            'name': name,
            'version_number': version,
            'website_url': '',
            'description': 'Culinary Inspiration, recipe collection, manager policies and staff improvements for Unofficial Patch.',
            'dependencies': [],
            'required_plugins': [
                {'guid': 'hvizeu.nivalis.unofficialpatch', 'minimum_version': '0.5.5'},
                {'guid': 'hvizeu.luz.continuity', 'minimum_version': ''}],
            'author': 'Hvizeu',
            'release_channel': 'Continuity'}
        files = {
            prefix + 'NivalisExpanded.dll': dll.read_bytes(),
            prefix + 'manifest.json': (json.dumps(manifest, indent=2) + '\n').encode(),
            'README.md': (name + ' ' + version + '\nRequires Unofficial Patch >=0.5.5 <0.6.0 and LUZ Civic Terminal 0.7.7 with protected saves enabled. Import this complete archive in LUZ and Apply the profile. Unofficial Patch retains manager, Culinary Inspiration, restaurant-review companions, purchasing observation and recovery.\n\n' + (SOURCE / 'NivalisExpanded/README.md').read_text(encoding='utf8')).encode(),
            'CHANGELOG.md': (SOURCE / 'NivalisExpanded/CHANGELOG.md').read_bytes(),
            'VERSION.txt': (name + ' ' + version + '\nContinuity API 1; Unofficial Patch >=0.5.5 <0.6.0 and LUZ Continuity required.\n').encode()}
        icon = SOURCE.parent / 'Shared Assets/Icons/Nivalis Expanded/icon.png'
        files[prefix + 'icon.png'] = icon.read_bytes()
        if any(Path(path).name.casefold() == 'nivalisunofficialpatch.dll' for path in files):
            raise RuntimeError('Expanded must not bundle Unofficial Patch')
        owned = [Path(path).name for path in files if path.lower().endswith('.dll')]
        if owned != ['NivalisExpanded.dll']:
            raise RuntimeError('Expanded migration package contains an unexpected binary')
        validate_release_payload(files)
        destination = output / 'Nivalis-Expanded-0.3.15-LUZ-Migration.zip'
        stage = destination.with_suffix('.partial')
        with zipfile.ZipFile(stage, 'w', zipfile.ZIP_DEFLATED) as archive:
            for path, data in files.items(): archive.writestr(path, data)
        with zipfile.ZipFile(stage) as archive:
            if archive.testzip() is not None or {entry.filename: archive.read(entry) for entry in archive.infolist()} != files:
                raise RuntimeError('Expanded migration archive readback differs')
        stage.replace(destination)
        with destination.open('rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        destination.with_suffix('.zip.sha256').write_text(digest + '  ' + destination.name + '\n')
        retained = destination.resolve()
        for old in output.glob('Nivalis-Expanded-*-LUZ-Migration.zip'):
            if old.resolve() == retained:
                continue
            if old.is_symlink() or old.resolve().parent != output.resolve():
                raise RuntimeError('Refusing redirected superseded Expanded migration package: ' + str(old))
            old.unlink()
            checksum = old.with_suffix('.zip.sha256')
            if checksum.exists():
                if checksum.is_symlink() or checksum.resolve().parent != output.resolve():
                    raise RuntimeError('Refusing redirected superseded Expanded checksum: ' + str(checksum))
                checksum.unlink()
        records.append(dict(Product=name, Version=version, Archive=destination.name, Sha256=digest,
                            SourceDllSha256=hashlib.sha256(dll.read_bytes()).hexdigest(), Optional=True))
    package_records = records
    package_index = output / 'PACKAGES.json'
    if selected is not None and package_index.is_file():
        existing = json.loads(package_index.read_text(encoding='utf8'))
        replacements = {record['Product']: record for record in records}
        package_records = [replacements.pop(record.get('Product'), record) for record in existing]
        package_records.extend(replacements.values())
    package_index.write_text(json.dumps(package_records, indent=2) + '\n')
    print('Verified complete LUZ migration imports:', len(records))
    return records

def prepare_sdk(output, baseline, tools, studio):
    sys.path.insert(0, str(SOURCE / 'NivalisSlowDays/tools'))
    from release_hygiene import validate_release_payload, validate_payload
    from release_check import bundle
    matches = list(Path(baseline).resolve().rglob('Nivalis-Object-Framework-SDK-0.3.12.zip'))
    if len(matches) != 1: raise RuntimeError('Choose the complete maintained SDK baseline')
    with zipfile.ZipFile(matches[0]) as archive:
        if archive.testzip() is not None: raise RuntimeError('Corrupt SDK baseline')
        files = {entry.filename: archive.read(entry) for entry in archive.infolist() if not entry.is_dir()}
    checker = SOURCE / 'NivalisUnofficialPatch/tools/Bindings/bin/Release/net10.0/Bindings.dll'
    for executable, folder in [('NivalisObjectTools', tools), ('NivalisObjectStudio', studio)]:
        data = (Path(folder) / (executable + '.exe')).read_bytes()
        owned = {executable + '.dll', 'NivalisFurnishings.Core.dll'}
        found = set()
        with tempfile.TemporaryDirectory(prefix='nivalis-continuity-tool-') as temporary:
            extracted = {}
            for name, contents in bundle(data):
                if name not in owned: continue
                validate_payload(name, contents); found.add(name)
                target = Path(temporary) / name
                target.write_bytes(contents); extracted[name] = target
                if name == 'NivalisObjectStudio.dll' and any(marker in contents for marker in (b'RunReview', b'RunProjectReview', b'VerifyAndCapture', b'VerifyUx')):
                    raise RuntimeError('Private Studio review code must not ship')
            if found != owned: raise RuntimeError('SDK single-file owned assemblies are missing')
            verify_assembly_version(extracted[executable + '.dll'], '0.4.1')
            verify_assembly_version(extracted['NivalisFurnishings.Core.dll'], '0.3.7')
        files[executable + '.exe'] = data
    loader = SOURCE / 'NivalisFurnishings/Loader/bin/ContinuityUpdate6/net6.0'
    subprocess.run(['dotnet', str(checker), '--verify-release-identity',
                    str(loader / 'NivalisFurnishings.dll'), '0.4.1'], check=True)
    verify_assembly_version(loader / 'NivalisFurnishings.Core.dll', '0.3.7')
    for name in ('NivalisFurnishings.dll', 'NivalisFurnishings.Core.dll'): files['References/' + name] = (loader / name).read_bytes()
    files['Developer Guide.md'] = (SOURCE / 'NivalisFurnishings/SDK/Developer Guide.md').read_bytes()
    for name in ('README.md', 'QUICK START.txt'):
        files[name] = ('Object Studio / SDK 0.4.1\nIncludes continuity capability reporting. Runtime references target Object Framework 0.4.1 through LUZ 0.7.7.\n\n' + files[name].decode('utf8').replace('0.3.12', '0.4.1')).encode()
    files['VERSION.txt'] = b'Object Studio / SDK 0.4.1\nRuntime 0.4.1, Core 0.3.7; LUZ 0.7.7 continuity reporting.\n'
    allowed = frozenset({'Source/InteractionExample/InteractionExample.csproj', 'Source/InteractionExample/Plugin.cs'})
    validate_release_payload(files, allowed_development_files=allowed)
    output = Path(output).resolve(); output.mkdir(parents=True, exist_ok=True)
    destination = output / 'Nivalis-Object-Framework-SDK-0.4.1-LUZ.zip'
    stage = destination.with_suffix('.partial')
    with zipfile.ZipFile(stage, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, contents in files.items(): archive.writestr(name, contents)
    with zipfile.ZipFile(stage) as archive:
        if archive.testzip() is not None or {entry.filename: archive.read(entry) for entry in archive.infolist()} != files: raise RuntimeError('SDK readback differs')
    stage.replace(destination)
    with destination.open('rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    destination.with_suffix('.zip.sha256').write_text(digest + '  ' + destination.name + '\n')
    print('Verified complete SDK candidate:', len(files), 'entries, SHA256', digest)

if __name__ == '__main__':
    if len(sys.argv) == 6 and sys.argv[1] == '--sdk': prepare_sdk(*sys.argv[2:])
    elif len(sys.argv) == 4 and sys.argv[1] == '--better-time':
        prepare(Path(sys.argv[2]), Path(sys.argv[3]), {'Better Time'})
    elif len(sys.argv) == 4 and sys.argv[1] == '--culinary-inspiration':
        prepare(Path(sys.argv[2]), Path(sys.argv[3]), {'Unofficial Patch', 'Nivalis Expanded'})
    elif len(sys.argv) == 4 and sys.argv[1] == '--expanded':
        prepare(Path(sys.argv[2]), Path(sys.argv[3]), {'Nivalis Expanded'})
    elif len(sys.argv) == 4 and sys.argv[1] == '--city-life':
        prepare(Path(sys.argv[2]), Path(sys.argv[3]), {'City Life'})
    elif len(sys.argv) == 3: prepare(Path(sys.argv[1]), Path(sys.argv[2]))
    else: raise SystemExit('Supply output and complete baseline, --better-time output baseline, --culinary-inspiration output baseline, --expanded output baseline, --city-life output baseline, or --sdk output baseline tools-publish studio-publish.')
