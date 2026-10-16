"""Offline native-host fixture and headless UI checks. Never launches a game."""
from pathlib import Path
import os
import platform
import subprocess

ROOT = Path(__file__).resolve().parents[1]

def validate():
    from check_shared_recovery import verify
    verify()
    output = ROOT / 'qa/platform/native' / (platform.system().lower() + '-' + platform.machine().lower())
    output.mkdir(parents=True, exist_ok=True)
    commands = [
        ('continuity-runtime.log', ['dotnet', 'run', '--project', 'Continuity/RuntimeChecks/RuntimeChecks.csproj', '-c', 'Release', '-p:UseSharedCompilation=false']),
        ('fixtures.log', ['dotnet', 'run', '--project', 'Tests/Tests.csproj', '-c', 'Release', '-p:UseSharedCompilation=false']),
        ('headless.log', ['dotnet', 'run', '--project', 'Desktop/Desktop.csproj', '-c', 'Release', '-p:RenderTools=true', '-p:UseSharedCompilation=false', '--', '--render-preview', str(output / 'render')])]
    for name, command in commands:
        with (output / name).open('w', encoding='utf8') as log:
            result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, env={**os.environ, 'MSBUILDDISABLENODEREUSE': '1', 'DOTNET_CLI_USE_MSBUILD_SERVER': '0'})
        if result.returncode:
            raise RuntimeError('Validation failed. See ' + str(output / name))
    print('Native-host fixtures and headless checks passed:', output, flush=True)

if __name__ == '__main__': validate()
