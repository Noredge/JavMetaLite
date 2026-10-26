#!/usr/bin/env python3
"""Export current source, with a verifiable manifest and stable ZIP entry metadata."""
import argparse
import datetime
from pathlib import Path
import stat
import subprocess
import zipfile
from release_manifest import ROOT, canonical_bytes, digest, export_paths, source_inputs, require

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('output', type=Path)
parser.add_argument('--overwrite', action='store_true', help='Explicitly replace this output archive.')
parser.add_argument('--allow-modified-source-export', action='store_true')
args = parser.parse_args()
root = ROOT
output = args.output.expanduser().resolve()
require(not output.exists() or args.overwrite, 'Output already exists; choose a new path or explicitly pass --overwrite')
require(not output.is_relative_to(root) or output.is_relative_to(root / 'artifacts'), 'Source archive must be outside the source tree or inside ignored artifacts/')
source = source_inputs(root, args.allow_modified_source_export)
if (root / '.git').exists():
    paths = subprocess.check_output(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], cwd=root).decode().split('\0')
    epoch = int(subprocess.check_output(['git', 'show', '-s', '--format=%ct', 'HEAD'], cwd=root))
else:
    paths = export_paths(root)
    epoch = 946684800  # Stable metadata for re-exported source; no fake build timestamp.
stamp = datetime.datetime.fromtimestamp(max(epoch, 315532800), datetime.timezone.utc)
zip_time = (stamp.year, stamp.month, stamp.day, stamp.hour, stamp.minute, stamp.second)
files, contents = {}, {}
for relative in sorted(set(paths)):
    path = root / relative
    if not relative or not path.is_file() or relative in ('SOURCE-MANIFEST.json', 'SOURCE-SNAPSHOT.txt'):
        continue
    require(not path.is_symlink(), 'Source archive refuses symlinks: ' + relative)
    if path.resolve() == output:
        continue
    files[relative] = {'sha256': digest(path), 'mode': stat.S_IMODE(path.stat().st_mode)}
    contents[relative] = path.read_bytes()
snapshot = ('Working tree snapshot, including current uncommitted implementation changes.\n'
            f'Baseline HEAD: {source["gitCommit"]}\n'
            'SOURCE-MANIFEST.json records exact exported bytes and executable modes.\n'
            'No Git metadata, SDK, build products or user media are included.\n'
            'See docs/macos/STATUS.md for verified scope and limitations.\n').encode()
import hashlib
contents['SOURCE-SNAPSHOT.txt'] = snapshot
files['SOURCE-SNAPSHOT.txt'] = {'sha256': hashlib.sha256(snapshot).hexdigest(), 'mode': 0o644}
contents['SOURCE-MANIFEST.json'] = canonical_bytes({'schemaVersion': 1, 'kind': 'source-export', 'source': source, 'files': files})
output.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for relative, data in sorted(contents.items()):
        info = zipfile.ZipInfo('JavMetaLite/' + relative, date_time=zip_time)
        info.create_system = 3
        info.external_attr = (stat.S_IFREG | files.get(relative, {}).get('mode', 0o644)) << 16
        info.compress_type = zipfile.ZIP_DEFLATED
        archive.writestr(info, data, compresslevel=9)
output.with_suffix(output.suffix + '.sha256').write_text(f'{digest(output)}  {output.name}\n')
print(output)
