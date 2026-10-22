"""Supervised, serial continuity validation. No real library, installation, or game is changed."""
from pathlib import Path
import json
import os
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT.parent
OUT = ROOT / 'qa/continuity'
TEST_TEMP = OUT.resolve() / 'temp'

def run(name, command, use_test_temp=False):
    OUT.mkdir(parents=True, exist_ok=True)
    TEST_TEMP.mkdir(parents=True, exist_ok=True)
    print('START', name, flush=True)
    path = OUT / (name + '.log')
    environment = {**os.environ, 'MSBUILDDISABLENODEREUSE': '1', 'DOTNET_CLI_USE_MSBUILD_SERVER': '0'}
    if use_test_temp:
        environment['TEMP'] = environment['TMP'] = str(TEST_TEMP)
    with path.open('w', encoding='utf8') as log:
        child = subprocess.Popen([str(x) for x in command], cwd=ROOT, stdout=log, stderr=subprocess.STDOUT,
            env=environment,
            creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == 'nt' else 0)
        try:
            deadline = time.monotonic() + 1800
            while child.poll() is None:
                if time.monotonic() > deadline or path.stat().st_size > 32 * 1024 * 1024:
                    raise RuntimeError('Owned validation operation exceeded its deadline or output limit')
                time.sleep(0.25)
        except BaseException:
            if os.name == 'nt': subprocess.run(['taskkill', '/PID', str(child.pid), '/T', '/F'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            else: child.terminate()
            raise
    print('RESULT', name, child.returncode, flush=True)
    if child.returncode:
        for line in path.read_text(encoding='utf8', errors='replace').splitlines()[-18:]: print(line, flush=True)
    code = child.returncode
    if name.startswith('headless') and code == 0 and not any((OUT / 'render').glob('*.png')):
        code = 1
        print('Headless output missing; process exit alone is not acceptance', flush=True)
    return dict(Name=name, ExitCode=code, Log=str(path))

def validate():
    common = ['-c', 'Release', '--nologo', '-v', 'minimal', '-m:1', '-p:UseSharedCompilation=false']
    projects = [('BetterTime', 'NivalisSlowDays/src-v2'), ('CityLife', 'NivalisCityLife/src'), ('Framework', 'NivalisFurnishings/Loader'), ('FurnitureLab', 'NivalisFurnitureLab/src')]
    jobs = [
        ('runtime-boundaries-build', ['dotnet', 'build', 'Continuity/RuntimeChecks/RuntimeChecks.csproj', '-c', 'Release', '-p:UseSharedCompilation=false', '-m:1'], False),
        ('runtime-boundaries', ['dotnet', 'Continuity/RuntimeChecks/bin/Release/net10.0/RuntimeChecks.dll'], True)
    ]
    jobs += [(name + '-migration', ['dotnet', 'build', SOURCE / project] + common + ['-p:LuzContinuity=true', '-p:OutputPath=bin/Continuity/net6.0/', '-p:NivalisInteropDirectory=' + str(SOURCE / 'NivalisSlowDays/analysis/update-25984046/interop')], False) for name, project in projects]
    jobs += [('UnofficialPatch-variants', ['pwsh', '-NoProfile', '-File', SOURCE / 'NivalisUnofficialPatch/Build-Variants.ps1', '-LuzContinuity'], False)]
    results = [run(name, command, use_test_temp) for name, command, use_test_temp in jobs]
    (OUT / 'validation.json').write_text(json.dumps(results, indent=2) + '\n', encoding='utf8')
    if any(r['ExitCode'] for r in results): raise RuntimeError('Continuity validation failed; inspect retained logs')

if __name__ == '__main__':
    if '--finish' in sys.argv:
        common = ['-c', 'Release', '--nologo', '-v', 'minimal', '-m:1', '-p:UseSharedCompilation=false']
        render = OUT / 'render'; render.mkdir(parents=True, exist_ok=True)
        for old in render.glob('*.png'): old.unlink()
        jobs = [
            ('runtime-final', [sys.executable, '-B', 'tools/build_continuity.py', '--loader', SOURCE / 'NivalisSlowDays/loader/BepInEx/core'], True),
            ('fixtures-final-restore', ['dotnet', 'restore', 'Tests/Tests.csproj', '-p:NuGetAudit=false', '--ignore-failed-sources', '--nologo', '-v', 'minimal', '-m:1', '-p:UseSharedCompilation=false'], False),
            ('fixtures-final-build', ['dotnet', 'build', 'Tests/Tests.csproj', '-c', 'Release', '--no-restore', '-p:UseSharedCompilation=false', '-m:1'], False),
            ('fixtures-final', ['dotnet', 'Tests/bin/Release/net10.0/Tests.dll'], True),
            ('runtime-boundaries-final-build', ['dotnet', 'build', 'Continuity/RuntimeChecks/RuntimeChecks.csproj', '-c', 'Release', '-p:UseSharedCompilation=false', '-m:1'], False),
            ('runtime-boundaries-final', ['dotnet', 'Continuity/RuntimeChecks/bin/Release/net10.0/RuntimeChecks.dll'], True),
            ('CityLife-migration-corrected', ['dotnet', 'build', SOURCE / 'NivalisCityLife/src'] + common + ['-p:LuzContinuity=true', '-p:OutputPath=bin/Continuity/net6.0/'], False),
            ('NivalisExpanded-migration-corrected', ['dotnet', 'build', SOURCE / 'NivalisExpanded/src'] + common + ['-p:LuzContinuity=true', '-p:OutputPath=bin/ContinuityPublic/net6.0/'], False),
            ('SDK-tools', ['dotnet', 'build', SOURCE / 'NivalisFurnishings/ToolsCli'] + common + ['-p:BaseOutputPath=bin/Continuity/', '-p:LuzContinuity=true'], False),
            ('coverage-build', ['dotnet', 'build', 'Continuity/Coverage/Coverage.csproj'] + common, False),
            ('native-adapter-targets', ['dotnet', 'Continuity/Coverage/bin/Release/net10.0/Coverage.dll', '--check-adapters', SOURCE / 'NivalisSlowDays/analysis/update-25984046/interop/Assembly-CSharp.dll'], False),
            ('preview-tools-restore', ['dotnet', 'restore', 'Desktop/Desktop.csproj', '-p:RenderTools=true', '-p:NuGetAudit=false', '--ignore-failed-sources', '--nologo', '-v', 'minimal', '-m:1', '-p:UseSharedCompilation=false'], False),
            ('preview-tools-build', ['dotnet', 'build', 'Desktop/Desktop.csproj', '-c', 'Release', '--no-restore', '-p:RenderTools=true', '-p:UseSharedCompilation=false', '-m:1'], False),
            ('headless-final', ['dotnet', 'Desktop/bin/Release/net10.0/win-x64/LUZ Civic Terminal.dll', '--render-preview', render], True)]
        results = [run(name, command, use_test_temp) for name, command, use_test_temp in jobs]
        (OUT / 'finish-validation.json').write_text(json.dumps(results, indent=2) + '\n', encoding='utf8')
        if any(r['ExitCode'] for r in results): raise RuntimeError('Consolidated continuity checks failed')
    else: validate()
