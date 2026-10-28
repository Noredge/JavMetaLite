#!/usr/bin/env python3
"""Select docs, core or full validation; unknown/packaging/pressure-sensitive changes stay full."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess

DOCS = frozenset(['README.md', 'README.zh-Hans.md', 'README.zh-Hant.md', 'README.ja.md',
                 'macos/README.md', 'docs/macos/STATUS.md', 'docs/macos/RELEASING.md',
                 'docs/macos/TESTING.md'])

PRESSURE_SENSITIVE = frozenset([
    'JavMetaLite.Worker/Retention.cs', 'JavMetaLite.Worker/ArtworkDiskCache.cs',
    'JavMetaLite.Worker/Workspace.cs', 'JavMetaLite.Worker/Operations.cs',
    'JavMetaLite.Worker/Discovery.cs', 'JavMetaLite.Worker/SavePlanning.cs',
    *['JavMetaLite.Core/Services/' + name + '.cs' for name in
      ['LocalSidecarInventory', 'LocalSidecarLocator', 'MovieJobLoader',
       'MovieFileDiscovery', 'MovieInputDiscovery', 'FileSystemPathSemantics']]])
CORE_SOURCE_ROOTS = (
    'JavMetaLite.Core/', 'JavMetaLite.Worker/', 'JavMetaLite.Worker.RegressionHost/',
    'JavMetaLite.PortableTests/', 'JavMetaLite.App/', 'JavMetaLite.SmokeTests/',
    'JavMetaLite.RegressionTests/', 'JavMetaLite.UiSmokeTests/',
    'macos/JavMetaLiteMac/', 'macos/WorkerKit/', 'macos/NativeBridgeKit/',
    'macos/WorkerKitTests/', 'macos/NativeBridgeKitTests/')


def classify(changes):
    return bool(changes) and all(status == 'M' and path in DOCS for status, path in changes)


def validation_level(changes, event_name, event):
    if event_name == 'workflow_dispatch':
        requested = event.get('inputs', {}).get('validation', 'full')
        return requested if requested in ('core', 'full') else 'full'
    if event_name == 'push' and event.get('ref', '').startswith('refs/tags/'):
        return 'full'
    if event_name not in ('push', 'pull_request') or not changes:
        return 'full'
    if classify(changes):
        return 'docs'
    for status, path in changes:
        if status == 'M' and path in DOCS:
            continue
        if status != 'M' or path in PRESSURE_SENSITIVE:
            return 'full'
        if not path.startswith(CORE_SOURCE_ROOTS) or Path(path).suffix not in ('.cs', '.swift'):
            return 'full'
    return 'core'


def changes_for_event(event_name, event, root):
    if event_name == 'pull_request':
        base = event.get('pull_request', {}).get('base', {}).get('sha', '')
    elif event_name == 'push':
        base = event.get('before', '')
    else:
        return [], 'Manual or unknown event requires full validation'
    if not re.fullmatch(r'[0-9a-fA-F]{40,64}', base) or set(base) == {'0'}:
        return [], 'Missing comparison commit requires full validation'
    try:
        raw = subprocess.check_output(['git', 'diff', '--name-status', '--no-renames', '-z',
                                       base, 'HEAD', '--'], cwd=root, stderr=subprocess.PIPE)
    except subprocess.CalledProcessError:
        return [], 'Comparison commit unavailable; full validation required'
    fields = raw.decode().strip('\0').split('\0') if raw else []
    if len(fields) % 2:
        return [], 'Unrecognized change list requires full validation'
    return list(zip(fields[::2], fields[1::2])), 'Changed-file classification'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--event', default=os.environ.get('GITHUB_EVENT_NAME', 'workflow_dispatch'))
    parser.add_argument('--event-file', type=Path, default=os.environ.get('GITHUB_EVENT_PATH'))
    parser.add_argument('--root', type=Path, default=Path.cwd())
    args = parser.parse_args()
    event = json.loads(args.event_file.read_text()) if args.event_file else {}
    changes, reason = changes_for_event(args.event, event, args.root)
    level = validation_level(changes, args.event, event)
    if args.event == 'workflow_dispatch':
        reason = 'Explicit manual validation profile=' + level
    elif event.get('ref', '').startswith('refs/tags/'):
        reason = 'Release tag requires full validation'
    elif level == 'docs':
        reason = 'Edited documentation allowlist only'
    elif level == 'core':
        reason = 'Known source edits; pressure-sensitive and packaging inputs unchanged'
    elif changes:
        reason = 'Sensitive, unknown, added or deleted files require full validation'
    report = {'level': level, 'reason': reason, 'changes': changes}
    print(json.dumps(report, indent=2))
    if os.environ.get('GITHUB_OUTPUT'):
        with open(os.environ['GITHUB_OUTPUT'], 'a') as stream:
            stream.write('level=' + level + '\n')


if __name__ == '__main__':
    main()
