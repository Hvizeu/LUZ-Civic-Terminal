"""Read-only provenance and integrity audit of a complete distribution archive."""
from io import BytesIO
from pathlib import Path, PurePosixPath
from zipfile import ZipFile, is_zipfile
import argparse
import hashlib
import json
import re

import release_check

MARKERS = re.compile(r'(?i)\b(?:chatgpt|openai|codex|claude|copilot|anthropic|midjourney|comfyui)\b|'
                     r'\bgpt-\d|\bdall[ -]?e\b|\bstable diffusion\b|'
                     r'\b(?:AI|LLM)[- ](?:generated|assisted)\b|'
                     r'\b(?:generated|written|created)\s+(?:by|with)\s+(?:an?\s+)?(?:ai|llm)\b|'
                     r'[A-Z]:[\\/]Users[\\/]|(?<![A-Za-z0-9.])/(?:Users|home)/|/mnt/data/')
PRIVATE = re.compile(r'(?i)(?:^|/)(?:AGENTS\.md|SKILL\.md|provenance\.md|transcripts?|prompts?|sessions?|analysis|qa)(?:/|$)|\.(?:pdb|jsonl|cs|csproj|py|ps1)$')


def audit(path):
    report = dict(archive=str(path), sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                  files=0, archives=0, embedded_png_files=0, findings=[], checks=[])

    def inspect(name, data, owned=True):
        report['files'] += 1
        if PRIVATE.search(name):
            report['findings'].append(dict(file=name, kind='private file'))
        matches = set()
        for label, text in [('name', name), ('utf8', data.decode('utf8', 'ignore')),
                            ('utf16', data.decode('utf-16-le', 'ignore')),
                            ('utf16-offset', data[1:].decode('utf-16-le', 'ignore'))]:
            for hit in MARKERS.finditer(text):
                key = hit.group().lower()
                if key in matches:
                    continue
                matches.add(key)
                report['findings'].append(dict(file=name, owned=owned, kind='text marker',
                    encoding=label, marker=hit.group(), context=text[max(0, hit.start()-70):hit.end()+100]))
        if b'\x89PNG\r\n\x1a\n' in data and owned:
            report['embedded_png_files'] += 1
        try:
            release_check.check(name, data, owned)
        except ValueError as error:
            report['findings'].append(dict(file=name, owned=owned, kind='release check', error=str(error)))

    def archive(raw, label):
        report['archives'] += 1
        with ZipFile(BytesIO(raw)) as z:
            names = z.namelist()
            assert len(names) == len(set(names)) and z.testzip() is None
            assert all(not PurePosixPath(n).is_absolute() and '..' not in PurePosixPath(n).parts for n in names)
            allowed = set()
            for n in names:
                if not n.endswith('/LUZ Civic Terminal.deps.json'):
                    continue
                parent = PurePosixPath(n).parent
                deps = json.loads(z.read(n))
                for target in deps['targets'].values():
                    for identity, assets in target.items():
                        if deps['libraries'][identity]['type'] not in {'package', 'runtimepack'}:
                            continue
                        for section in ('runtime', 'native', 'resources', 'runtimeTargets'):
                            allowed.update(str(parent/PurePosixPath(a).name) for a in assets.get(section, {}))
            if z.comment:
                inspect(label+' [archive comment]', z.comment)
            for entry in z.infolist():
                if entry.is_dir():
                    continue
                data = z.read(entry)
                name = label+'/'+entry.filename
                if entry.comment:
                    inspect(name+' [entry comment]', entry.comment)
                if entry.extra:
                    inspect(name+' [entry metadata]', entry.extra)
                if is_zipfile(BytesIO(data)):
                    archive(data, name)
                else:
                    owned = entry.filename not in allowed and 'Notices' not in PurePosixPath(entry.filename).parts
                    inspect(name, data, owned)
            report['checks'].append(dict(archive=label, entries=len(names), integrity='passed'))
            print(f'Checked {label}: {len(names)} entries', flush=True)

    archive(path.read_bytes(), path.name)
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('archive', type=Path)
    parser.add_argument('report', type=Path)
    args = parser.parse_args()
    result = audit(args.archive)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, indent=2), encoding='utf8')
    print(json.dumps({k:v for k,v in result.items() if k != 'checks'}, indent=2))
