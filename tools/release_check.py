"""Check project payloads and inspect compressed .NET bundle entries before release."""
from pathlib import Path
import io
import json
import re
import struct
import sys
import zipfile
import zlib

MARKERS = re.compile(r'(?i)chatgpt|openai|codex|claude|copilot|(?:AI|LLM)[- ](?:generated|assisted)|[A-Z]:[\\/]Users[\\/]|(?<![A-Za-z0-9.])/(?:Users|home)/|/mnt/data/')
PRIVATE = {'.pdb', '.cs', '.csproj', '.py', '.ps1', '.jsonl'}

def check(name, data, owned=True):
    path = Path(name)
    if path.name.lower() in {'agents.md', 'skill.md'} or path.suffix.lower() in PRIVATE:
        raise ValueError('Development file: ' + name)
    if zipfile.is_zipfile(io.BytesIO(data)):
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            if archive.testzip() is not None: raise ValueError('Invalid archive: ' + name)
            for item in archive.infolist():
                if not item.is_dir(): check(name + '/' + item.filename, archive.read(item), owned)
        return
    if owned:
        for text in (name, data.decode('utf8', 'ignore'), data.decode('utf-16-le', 'ignore'), data[1:].decode('utf-16-le', 'ignore')):
            match = MARKERS.search(text)
            if match:
                raise ValueError('Review payload trace: ' + name + ': ' + match.group())
            if '--render-preview' in text:
                raise ValueError('Preview tooling was compiled into the release: ' + name)
    # Include PNG frames embedded in DLL resources, Windows ICO and macOS ICNS.
    if owned:
        cursor = 0
        while (start := data.find(b'\x89PNG\r\n\x1a\n', cursor)) >= 0:
            pos = start + 8
            if data[pos+4:pos+8] != b'IHDR': raise ValueError('Invalid PNG header: ' + name)
            while True:
                if pos + 12 > len(data): raise ValueError('Truncated PNG: ' + name)
                size = struct.unpack_from('>I', data, pos)[0]
                kind = data[pos+4:pos+8]; end = pos + size + 12
                if end > len(data) or zlib.crc32(data[pos+4:end-4]) & 0xffffffff != struct.unpack_from('>I', data, end-4)[0]:
                    raise ValueError('Invalid PNG checksum: ' + name)
                if kind in {b'tEXt', b'iTXt', b'zTXt', b'eXIf', b'caBX'}: raise ValueError('Image metadata: ' + name)
                pos = end
                if kind == b'IEND': cursor = end; break

def bundle(data):
    signature = bytes.fromhex('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae')
    pos = data.find(signature)
    if pos < 8:
        raise ValueError('Missing .NET bundle header')
    offset = struct.unpack_from('<q', data, pos-8)[0]
    stream = io.BytesIO(data); stream.seek(offset)
    def number(fmt):
        return struct.unpack(fmt, stream.read(struct.calcsize(fmt)))[0]
    def string():
        length = shift = 0
        while True:
            byte = number('B'); length |= (byte & 127) << shift
            if byte < 128: break
            shift += 7
        return stream.read(length).decode('utf8')
    major, minor, count = number('<I'), number('<I'), number('<i')
    if major != 6: raise ValueError('Unsupported bundle version')
    string(); stream.read(40)
    entries = []
    for _ in range(count):
        start, size, compressed = number('<q'), number('<q'), number('<q')
        number('B'); name = string()
        raw = data[start:start+(compressed or size)]
        if compressed: raw = zlib.decompress(raw, -15)
        if len(raw) != size: raise ValueError('Invalid bundle entry ' + name)
        entries.append((name, raw))
    return entries

def main():
    folder = Path(sys.argv[1]); count = 0
    manifests = list(folder.rglob('LUZ Civic Terminal.deps.json'))
    allowed = set()
    if manifests:
        if len(manifests) != 1: raise ValueError('Expected one application dependency manifest')
        payload = manifests[0].parent
        deps = json.loads(manifests[0].read_text())
        for target in deps['targets'].values():
            for identity, assets in target.items():
                if deps['libraries'][identity]['type'] not in {'package', 'runtimepack'}: continue
                for section in ('runtime', 'native', 'resources', 'runtimeTargets'):
                    for name in assets.get(section, {}):
                        allowed.add((payload / Path(name).name).relative_to(folder).as_posix())
        if not (payload / 'Core.dll').is_file(): raise ValueError('Core assembly missing')
    for path in folder.rglob('*'):
        if not path.is_file(): continue
        relative = path.relative_to(folder).as_posix(); raw = path.read_bytes()
        third_party = 'Notices' in path.relative_to(folder).parts or relative in allowed
        check(relative, raw, not third_party); count += 1
        if path.suffix.lower() == '.exe' and not manifests:
            entries = bundle(raw)
            for name, contents in entries:
                owned = name in {'Core.dll', 'LUZ Civic Terminal.dll', 'LUZ Civic Terminal.deps.json', 'LUZ Civic Terminal.runtimeconfig.json'}
                check(name, contents, owned)
                count += 1
            if not any(name == 'Core.dll' for name, _ in entries): raise ValueError('Core missing from bundle')
    print(f'Release check passed: {count} files/bundle entries. Runtime binaries and required notices preserved.')

if __name__ == '__main__': main()
