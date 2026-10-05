"""Offline native-host fixture and headless UI checks. Never launches a game."""
from pathlib import Path
import platform
import subprocess

ROOT = Path(__file__).resolve().parents[1]

def validate():
    output = ROOT / 'qa/platform/native' / (platform.system().lower() + '-' + platform.machine().lower())
    output.mkdir(parents=True, exist_ok=True)
    commands = [
        ('fixtures.log', ['dotnet', 'run', '--project', 'Tests/Tests.csproj', '-c', 'Release']),
        ('headless.log', ['dotnet', 'run', '--project', 'Desktop/Desktop.csproj', '-c', 'Release', '-p:RenderTools=true', '--', '--render-preview', str(output / 'render')])]
    for name, command in commands:
        with (output / name).open('w', encoding='utf8') as log:
            result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
        if result.returncode:
            raise RuntimeError('Validation failed. See ' + str(output / name))
    print('Native-host fixtures and headless checks passed:', output, flush=True)

if __name__ == '__main__': validate()
