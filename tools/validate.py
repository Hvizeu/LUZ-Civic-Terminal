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
    jobs = [
        ('continuity-runtime.log', 'Continuity/RuntimeChecks/RuntimeChecks.csproj', [], []),
        ('fixtures.log', 'Tests/Tests.csproj', [], fixture_arguments),
        ('headless.log', 'Desktop/Desktop.csproj', ['-p:RenderTools=true'],
         ['--', '--render-preview', str(output / 'render')])]
    for name, project, build_properties, run_arguments in jobs:
        with (output / name).open('w', encoding='utf8') as log:
            environment = {**os.environ, 'MSBUILDDISABLENODEREUSE': '1', 'DOTNET_CLI_USE_MSBUILD_SERVER': '0'}
            commands = [
                ['dotnet', 'build', project, '-c', 'Release', '-p:UseSharedCompilation=false', '-m:1', *build_properties],
                ['dotnet', 'run', '--project', project, '-c', 'Release', '--no-build', '--no-restore',
                 '-p:UseSharedCompilation=false', *build_properties, *run_arguments]]
            for command in commands:
                result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, env=environment)
                if result.returncode:
                    raise RuntimeError('Validation failed. See ' + str(output / name))
    print('Native-host fixtures and headless checks passed:', output, flush=True)

if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('--object-framework', action='store_true', help='Also require and exercise both built editions in the sibling NivalisFurnishings project')
    validate(parser.parse_args().object_framework)
