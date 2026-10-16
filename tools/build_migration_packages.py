"""Preserve complete maintained payloads while preparing local LUZ migration imports."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT.parent

def prepare(output, baseline):
    sys.path.insert(0, str(SOURCE / 'NivalisSlowDays/tools'))
    from release_hygiene import validate_release_payload
    output = Path(output).resolve(); baseline = Path(baseline).resolve(); output.mkdir(parents=True, exist_ok=True)
    definitions = [
        ('Better Time', 'NivalisBetterTime-2.2.7-Nexus.zip', 'NivalisSlowDays', '2.2.7', '2.3.0', SOURCE / 'NivalisSlowDays/src-v2/bin/Continuity/net6.0/NivalisSlowDays.dll'),
        ('Unofficial Patch', 'Nivalis_Unofficial_Patch-0.4.6-Nexus.zip', 'NivalisUnofficialPatch', '0.4.6', '0.5.0', SOURCE / 'NivalisUnofficialPatch/src/bin/ContinuityPublic/net6.0/NivalisUnofficialPatch.dll'),
        ('City Life', 'NivalisCityLife-1.1.0-Nexus.zip', 'NivalisCityLife', '1.1.0', '1.2.0', SOURCE / 'NivalisCityLife/src/bin/Continuity/net6.0/NivalisCityLife.dll'),
        ('Object Framework', 'Nivalis-Object-Framework-Runtime-0.3.13.zip', 'NivalisFurnishings', '0.3.13', '0.4.0', SOURCE / 'NivalisFurnishings/Loader/bin/Continuity/net6.0/NivalisFurnishings.dll'),
        ('Furniture Lab', 'Nivalis-Furniture-Lab-0.1.8.zip', 'NivalisFurnitureLab', '0.1.8', '0.2.0', SOURCE / 'NivalisFurnitureLab/src/bin/Continuity/net6.0/NivalisFurnitureLab.dll')]
    records = []
    for name, original, assembly, old, version, dll in definitions:
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
                data['description'] = data.get('description', '') + ' Continuity migration: requires LUZ 0.5.0 protected saves.'
                files[path] = (json.dumps(data, indent=2) + '\n').encode()
            if Path(path).name.lower().startswith('readme'):
                files[path] = (name + ' ' + version + '\nRequires LUZ Civic Terminal 0.5.0 with protected saves enabled. Import this complete archive in LUZ and Apply the profile. Existing assets and credits are retained below.\n\n' + files[path].decode('utf8').replace(old, version)).encode()
        files['VERSION.txt'] = (name + ' ' + version + '\nContinuity API 1; LUZ 0.5.0 required.\n').encode()
        validate_release_payload(files)
        destination = output / (name.replace(' ', '-') + '-' + version + '-LUZ-Migration.zip')
        stage = destination.with_suffix('.partial')
        with zipfile.ZipFile(stage, 'w', zipfile.ZIP_DEFLATED) as archive:
            for path, data in files.items(): archive.writestr(path, data)
        with zipfile.ZipFile(stage) as archive:
            if archive.testzip() is not None or {entry.filename: archive.read(entry) for entry in archive.infolist()} != files: raise RuntimeError('Migration archive readback differs')
        stage.replace(destination)
        with destination.open('rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        destination.with_suffix('.zip.sha256').write_text(digest + '  ' + destination.name + '\n')
        records.append(dict(Product=name, Version=version, Archive=destination.name, Sha256=digest, SourceDllSha256=hashlib.sha256(dll.read_bytes()).hexdigest(), Optional=name == 'Furniture Lab'))
    (output / 'PACKAGES.json').write_text(json.dumps(records, indent=2) + '\n')
    print('Verified complete LUZ migration imports:', len(records))
    return records

def prepare_sdk(output, baseline, tools, studio):
    sys.path.insert(0, str(SOURCE / 'NivalisSlowDays/tools'))
    from release_hygiene import validate_release_payload, validate_payload
    from release_check import bundle
    matches = list(Path(baseline).resolve().rglob('Nivalis-Object-Framework-SDK-0.3.11.zip'))
    if len(matches) != 1: raise RuntimeError('Choose the complete maintained SDK baseline')
    with zipfile.ZipFile(matches[0]) as archive:
        if archive.testzip() is not None: raise RuntimeError('Corrupt SDK baseline')
        files = {entry.filename: archive.read(entry) for entry in archive.infolist() if not entry.is_dir()}
    for executable, folder in [('NivalisObjectTools', tools), ('NivalisObjectStudio', studio)]:
        data = (Path(folder) / (executable + '.exe')).read_bytes()
        owned = {executable + '.dll', 'NivalisFurnishings.Core.dll'}
        found = set()
        for name, contents in bundle(data):
            if name not in owned: continue
            validate_payload(name, contents); found.add(name)
            if name == 'NivalisObjectStudio.dll' and any(marker in contents for marker in (b'RunReview', b'RunProjectReview', b'VerifyAndCapture', b'VerifyUx')):
                raise RuntimeError('Private Studio review code must not ship')
        if found != owned: raise RuntimeError('SDK single-file owned assemblies are missing')
        files[executable + '.exe'] = data
    loader = SOURCE / 'NivalisFurnishings/Loader/bin/Continuity/net6.0'
    for name in ('NivalisFurnishings.dll', 'NivalisFurnishings.Core.dll'): files['References/' + name] = (loader / name).read_bytes()
    files['Developer Guide.md'] = (SOURCE / 'NivalisFurnishings/SDK/Developer Guide.md').read_bytes()
    for name in ('README.md', 'QUICK START.txt'):
        files[name] = ('Object Studio / SDK 0.4.0\nIncludes continuity capability reporting. Runtime references target Object Framework 0.4.0 through LUZ 0.5.0.\n\n' + files[name].decode('utf8').replace('0.3.11', '0.4.0')).encode()
    files['VERSION.txt'] = b'Object Studio / SDK 0.4.0\nRuntime 0.4.0, Core 0.3.6; LUZ 0.5.0 continuity reporting.\n'
    allowed = frozenset({'Source/InteractionExample/InteractionExample.csproj', 'Source/InteractionExample/Plugin.cs'})
    validate_release_payload(files, allowed_development_files=allowed)
    output = Path(output).resolve(); output.mkdir(parents=True, exist_ok=True)
    destination = output / 'Nivalis-Object-Framework-SDK-0.4.0-LUZ.zip'
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
    elif len(sys.argv) == 3: prepare(Path(sys.argv[1]), Path(sys.argv[2]))
    else: raise SystemExit('Supply output and complete baseline, or --sdk output baseline tools-publish studio-publish.')
