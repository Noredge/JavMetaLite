#!/usr/bin/env python3
"""Check repository-relative Markdown links without executing or fetching document content."""
from pathlib import Path
import re
import subprocess
from urllib.parse import unquote, urlsplit


def check(root):
    names = subprocess.check_output(['git', 'ls-files', '-z', '*.md'], cwd=root).decode().split('\0')
    checked = 0
    failures = []
    for name in filter(None, names):
        path = root / name
        if not path.exists():
            failures.append(f'Missing document: {name}')
            continue
        for target in re.findall(r'\[[^\]]*\]\(([^)]+)\)', path.read_text()):
            target = target.strip('<>')
            url = urlsplit(target)
            if url.scheme or not url.path or url.netloc:
                continue
            checked += 1
            if not (path.parent / unquote(url.path)).exists():
                failures.append(f'{name}: missing {target}')
    return checked, failures


if __name__ == '__main__':
    count, failures = check(Path.cwd())
    if failures:
        raise SystemExit('\n'.join(failures))
    print(f'DOCUMENT LINKS PASS: {count} repository-relative links')
