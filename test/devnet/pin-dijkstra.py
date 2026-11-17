"""Rebuild the DJ manifest's derived hashes and attribution from retained public fixtures.

Run from any directory with Python 3. No node, wallet, network or third-party module is used.
Capture bytes come from DijkstraProbes; provenance fields retain the recorded fetch pin.
"""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2] / 'src/Chrysalis.IntegrationTests/Golden'
folder = root / 'Dijkstra'
manifest = json.loads((folder / 'manifest.json').read_text())
sources = json.loads((root / 'SOURCES.json').read_text())
pin = sources['dijkstranet']
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
configuration = json.loads((folder / 'config.json').read_text())['Configuration']
manifest['genesis_hashes'] = {name: value for section in configuration.values() for name, value in section.items() if name.endswith('GenesisHash')}
fork_paths = set()
for row in manifest['files']:
    for kind in ('block', 'header', 'chain_sync'):
        item = row[kind]
        item['sha256'] = sha(folder / item['path'])
        fork_paths.add(item['path'])
manifest['artifacts'] = [{'path': path.name, 'sha256': sha(path)} for path in sorted(folder.iterdir()) if path.is_file() and path.name not in fork_paths | {'manifest.json', 'README.md'}]
for name in ('license', 'notice'):
    pin['retained_' + name + '_sha256'] = sha(root / pin[name + '_path'])
manifest['sources'].update({key: value for key, value in pin.items() if key not in ('manifest', 'manifest_sha256', 'golden_class', 'notice')})
(folder / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
pin['manifest_sha256'] = sha(folder / 'manifest.json')
(root / 'SOURCES.json').write_text(json.dumps(sources, indent=2) + '\n')
