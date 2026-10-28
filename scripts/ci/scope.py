#!/usr/bin/env python3
"""Fail closed to full CI unless every changed file is an edited, non-payload document."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess

DOCS = frozenset(['README.md', 'README.zh-Hans.md', 'README.zh-Hant.md', 'README.ja.md',
                 'macos/README.md', 'docs/macos/STATUS.md', 'docs/macos/RELEASING.md',
                 'docs/macos/TESTING.md'])


def classify(changes):
    return bool(changes) and all(status == 'M' and path in DOCS for status, path in changes)


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
    docs = classify(changes)
    report = {'full': not docs, 'reason': 'Edited documentation allowlist only' if docs else reason,
              'changes': changes}
    print(json.dumps(report, indent=2))
    if os.environ.get('GITHUB_OUTPUT'):
        with open(os.environ['GITHUB_OUTPUT'], 'a') as stream:
            stream.write('full=' + str(not docs).lower() + '\n')


if __name__ == '__main__':
    main()
