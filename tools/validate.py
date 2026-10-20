"""Offline native-host fixture and headless UI checks. Never launches a game."""
from pathlib import Path
import os
import platform
import subprocess

ROOT = Path(__file__).resolve().parents[1]

def validate(object_framework=False):
    from check_shared_recovery import verify
    verify()
    output = ROOT / 'qa/platform/native' / (platform.system().lower() + '-' + platform.machine().lower())
    output.mkdir(parents=True, exist_ok=True)
    fixture_arguments = ['--', '--object-framework'] if object_framework else []
    commands = [
        ('continuity-runtime.log', ['dotnet', 'run', '--project', 'Continuity/RuntimeChecks/RuntimeChecks.csproj', '-c', 'Release', '-p:UseSharedCompilation=false']),
        ('fixtures.log', ['dotnet', 'run', '--project', 'Tests/Tests.csproj', '-c', 'Release', '-p:UseSharedCompilation=false', *fixture_arguments]),
        ('headless.log', ['dotnet', 'run', '--project', 'Desktop/Desktop.csproj', '-c', 'Release', '-p:RenderTools=true', '-p:UseSharedCompilation=false', '--', '--render-preview', str(output / 'render')])]
    for name, command in commands:
        with (output / name).open('w', encoding='utf8') as log:
            result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, env={**os.environ, 'MSBUILDDISABLENODEREUSE': '1', 'DOTNET_CLI_USE_MSBUILD_SERVER': '0'})
        if result.returncode:
            raise RuntimeError('Validation failed. See ' + str(output / name))
    print('Native-host fixtures and headless checks passed:', output, flush=True)

if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('--object-framework', action='store_true', help='Also require and exercise both built editions in the sibling NivalisFurnishings project')
    validate(parser.parse_args().object_framework)
