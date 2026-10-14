"""Verify standalone recovery sources and their maintained source manifest."""
from pathlib import Path
import hashlib,json

def verify():
    root=Path(__file__).resolve().parents[1]
    folder=root/'Core/Recovery'
    expected=json.loads((folder/'SOURCE-HASHES.json').read_text(encoding='utf8'))
    actual={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in folder.glob('Recovery*.cs')}
    if actual!=expected:raise ValueError('Vendored recovery sources differ from the verified manifest.')
    shared=root.parent/'NivalisShared'
    if shared.exists():
        canonical={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in shared.glob('Recovery*.cs')}
        if canonical!=actual:raise ValueError('Runtime and launcher recovery sources have diverged.')
    print('Shared recovery source parity passed.')

if __name__=='__main__':verify()
