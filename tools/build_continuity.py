"""Build LUZ's bundled Windows IL2CPP bridge without game assemblies or game execution."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
LOADER_URL = 'https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip'
LOADER_HASH = '46CF1EF802BDF8CB6587FC1E4D98F7AF1073A60487B39A64E230E6F6E4C23AEC'

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()

def catalog():
    def rule(owner, name, implementation='native', dependencies=()):
        return dict(Owner=owner, Name=name, Implementation=implementation, Dependencies=list(dependencies))
    save = ('core/save-lifecycle',)
    rules = [rule('core', 'save-lifecycle'), rule('core', 'input-display', 'unbound-input-v1'),
             rule('bettertime', 'module', dependencies=save), rule('bettertime', 'clock', dependencies=save),
             rule('unofficialpatch', 'module', dependencies=save), rule('unofficialpatch', 'inventory-transactions', dependencies=save),
             rule('expanded', 'module', dependencies=('core/save-lifecycle', 'unofficialpatch/module', 'unofficialpatch/inventory-transactions')),
             rule('citylife', 'module', dependencies=save), rule('framework', 'module', dependencies=save),
             rule('framework', 'catalogue', dependencies=save), rule('framework', 'behaviours', dependencies=('framework/catalogue',)),
             rule('furniturelab', 'module', dependencies=save)]
    return dict(Schema=1, Version='0.2.0', Builds=[dict(Id='25984046-be788', Identity=dict(
        Native='C65362434D61434A6FF5BFF771E7C2C44F2AEEABFDB1398C10B25FD9CBCF424E',
        Metadata='012A276B3D48E5CD85E0AD910B427742C0052FA9F974BFFDCEA30A30A4915FC5', Loader=LOADER_HASH), Capabilities=rules)])

def maintained_support():
    def module(guid, assembly, owner, name, capabilities, companions=()):
        return dict(Guid=guid, Assembly=assembly, Owner=owner, DisplayName=name,
                    Capabilities=list(capabilities), Companions=list(companions))
    def companion(root, pattern, slot, validation='json-revision', minimum=1, maximum=1):
        return dict(Root=root, Pattern=pattern, Slot=slot, Required=False, Validation=validation,
                    SchemaMinimum=minimum, SchemaMaximum=maximum, RevisionProperty='Revision')
    return dict(Schema=1, Modules=[
        module('hvizeu.nivalis.slowdays', 'NivalisSlowDays', 'bettertime', 'Better Time', ['module', 'clock']),
        module('hvizeu.nivalis.unofficialpatch', 'NivalisUnofficialPatch', 'unofficialpatch', 'Nivalis Unofficial Patch', ['module', 'inventory-transactions'], [
            companion('config', 'NivalisUnofficialPatch/Hauling/{slot}.json', 'sha256', minimum=1, maximum=4),
            companion('config', 'NivalisUnofficialPatch/ManagerPolicies/{slot}.json', 'sha256', minimum=2, maximum=4)]),
        module('hvizeu.nivalis.expanded', 'NivalisExpanded', 'expanded', 'Nivalis Expanded', ['module']),
        module('hvizeu.nivalis.citylife', 'NivalisCityLife', 'citylife', 'Nivalis City Life', ['module'], [
            companion('config', 'NivalisCityLife/Saves/{slot}.json', 'sha256-upper', minimum=1, maximum=3)]),
        module('hvizeu.nivalis.furnishings', 'NivalisFurnishings', 'framework', 'Nivalis Object Framework', ['module', 'catalogue', 'behaviours']),
        module('hvizeu.nivalis.furniturelab', 'NivalisFurnitureLab', 'furniturelab', 'Nivalis Furniture Lab', ['module'])])

def build(loader=None):
    with tempfile.TemporaryDirectory(prefix='luz-continuity-build-') as temp:
        temp = Path(temp)
        if loader is None:
            archive = temp / 'loader.zip'
            request = urllib.request.Request(LOADER_URL, headers={'User-Agent': 'LUZ-Civic-Terminal-Build/0.7.6 (+https://github.com/Hvizeu/LUZ-Civic-Terminal)'})
            with urllib.request.urlopen(request, timeout=60) as response, archive.open('wb') as output:
                total = 0
                while block := response.read(1024 * 1024):
                    total += len(block)
                    if total > 128 * 1024 * 1024: raise RuntimeError('Loader download exceeds build-input limit')
                    output.write(block)
            # Only compiler references are extracted. Nothing is installed or distributed.
            loader = temp / 'references'; loader.mkdir()
            with zipfile.ZipFile(archive) as package:
                for name in ('BepInEx.Core.dll', 'BepInEx.Unity.IL2CPP.dll', '0Harmony.dll'):
                    (loader / name).write_bytes(package.read('BepInEx/core/' + name))
        loader = Path(loader).resolve()
        if digest(loader / 'BepInEx.Unity.IL2CPP.dll') != LOADER_HASH: raise RuntimeError('Unrecognized IL2CPP loader compiler input')
        # Subsequent fixture builds must work from this repository alone. Compiler
        # references stay in ignored build storage and are never launcher payloads.
        references = ROOT / 'build/continuity-references'; references.mkdir(parents=True, exist_ok=True)
        for name in ('BepInEx.Core.dll', 'BepInEx.Unity.IL2CPP.dll', '0Harmony.dll'):
            if (loader / name).resolve() != (references / name).resolve(): shutil.copy2(loader / name, references / name)
        subprocess.run(['dotnet', 'build', str(ROOT / 'Continuity/Runtime/Runtime.csproj'), '-c', 'Release',
                        '--nologo', '-v', 'minimal', '-m:1', '-p:UseSharedCompilation=false', '-p:LoaderDirectory=' + str(references)], cwd=ROOT, check=True, env={**os.environ, 'MSBUILDDISABLENODEREUSE': '1', 'DOTNET_CLI_USE_MSBUILD_SERVER': '0'})
        output = ROOT / 'Continuity/Payload'; output.mkdir(parents=True, exist_ok=True)
        for name in ('Luz.Continuity.Runtime.dll', 'Luz.Continuity.dll'):
            shutil.copy2(ROOT / 'Continuity/Runtime/bin/Release/net6.0' / name, output / name)
        (output / 'catalog.json').write_text(json.dumps(catalog(), indent=2) + '\n', encoding='utf8')
        (output / 'maintained-support.json').write_text(json.dumps(maintained_support(), indent=2) + '\n', encoding='utf8')
        (output / 'payload.json').write_text(json.dumps({name: digest(output / name) for name in
            ('Luz.Continuity.Runtime.dll', 'Luz.Continuity.dll', 'catalog.json', 'maintained-support.json')}, indent=2) + '\n', encoding='utf8')
        print('Verified continuity runtime, API and build catalog:', output)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--loader', type=Path)
    build(parser.parse_args().loader)
