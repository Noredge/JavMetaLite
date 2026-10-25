#!/usr/bin/env python3
"""Validate the String Catalog and actual Swift localization resolver with synthetic bundles."""
import json
from pathlib import Path
import plistlib
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
RESOURCES = ROOT / 'macos/JavMetaLiteMac/Resources'
CATALOG = RESOURCES / 'Localizable.xcstrings'
LANGUAGES = ('en', 'zh-Hans', 'zh-Hant', 'ja')
STRING_LINE = re.compile(r'^\s*("(?:\\.|[^"\\])*")\s*=\s*("(?:\\.|[^"\\])*");\s*$')
PLACEHOLDER = re.compile(r'%(?:(\d+)\$)?[-+ #0]*(?:\d+|\*)?(?:\.(?:\d+|\*))?([hlLzjtq]*)([@diuoxXfFeEgGaAcCsSp])|%%')


def placeholders(value):
    signature, position = [], 0
    for match in PLACEHOLDER.finditer(value):
        if match[0] == '%%':
            continue
        position += 1
        signature.append((int(match[1] or position), match[2] + match[3]))
    return sorted(signature)


def read_strings(path):
    subprocess.run(['/usr/bin/plutil', '-lint', str(path)], check=True, capture_output=True)
    result = {}
    for number, line in enumerate(path.read_text().splitlines(), 1):
        if not line.strip():
            continue
        match = STRING_LINE.fullmatch(line)
        assert match, f'Unexpected strings format: {path}:{number}'
        key, value = (json.loads(item) for item in match.groups())
        assert key not in result, f'Duplicate key: {path}/{key}'
        result[key] = value
    return result


catalog = json.loads(CATALOG.read_text())
assert catalog['sourceLanguage'] == 'en'
entries = catalog['strings']
assert entries
for language in LANGUAGES:
    strings = read_strings(RESOURCES / (language + '.lproj') / 'Localizable.strings')
    assert set(strings) == set(entries), f'Missing or extra keys for {language}'
    for key, entry in entries.items():
        unit = entry['localizations'][language]['stringUnit']
        assert unit['state'] == 'translated' and unit['value'].strip(), f'Untranslated {language}/{key}'
        assert unit['value'] == strings[key], f'Catalog/strings drift: {language}/{key}'
        english = entry['localizations']['en']['stringUnit']['value']
        assert placeholders(unit['value']) == placeholders(english), f'Placeholder mismatch: {language}/{key}'

# Literal application keys must be present; dynamic field/state/reason keys are checked by their entries.
for source in (ROOT / 'macos/JavMetaLiteMac').rglob('*.swift'):
    for key in re.findall(r'\btr\("([^"\\]+)"\)', source.read_text()):
        assert key in entries, f'Missing used key {key} in {source.relative_to(ROOT)}'
# Stable public error reasons must also have localized user-facing messages.
reason_sources = list((ROOT / 'JavMetaLite.Worker').glob('*.cs')) + [ROOT / 'JavMetaLite.Core/Services/BrowserPageImporter.cs']
for source in reason_sources:
    for reason in re.findall(r'"([A-Z][A-Z_]{3,})"', source.read_text()):
        assert 'error.' + reason in entries, f'Missing localized error reason: {reason}'
info = plistlib.loads((RESOURCES / 'Info.plist').read_bytes())
assert set(LANGUAGES).issubset(info['CFBundleLocalizations']), 'Info.plist omits a supported language'
package = (ROOT / 'macos/Package.swift').read_text()
for language in LANGUAGES:
    assert f'Resources/{language}.lproj' in package, f'SwiftPM omits {language}'
project = subprocess.check_output(['/usr/bin/plutil', '-convert', 'json', '-o', '-', str(ROOT / 'macos/JavMetaLiteMac.xcodeproj/project.pbxproj')])
objects = json.loads(project)['objects']
project_settings = next(value for value in objects.values() if value.get('isa') == 'PBXProject')
assert set(LANGUAGES).issubset(project_settings['knownRegions']), 'Xcode knownRegions omit a supported language'
print(f'LOCALIZATION STRUCTURE PASS: {len(entries)} keys × 4 languages; complete catalog/strings, placeholders, literal UI keys, package/Xcode registrations', flush=True)

with tempfile.TemporaryDirectory(prefix='JavMetaLite localization 测试 ') as temporary:
    directory = Path(temporary)
    fallback = directory / 'Fallback.bundle'
    (fallback / 'en.lproj').mkdir(parents=True)
    (fallback / 'ja.lproj').mkdir()
    (fallback / 'Info.plist').write_bytes(plistlib.dumps({'CFBundleIdentifier': 'com.noredge.JavMetaLite.LocalizationFixture', 'CFBundleDevelopmentRegion': 'en'}))
    (fallback / 'en.lproj/Localizable.strings').write_text('"englishFallback" = "English fallback value";\n')
    (fallback / 'ja.lproj/Localizable.strings').write_text('"anotherKey" = "別のキー";\n')
    executable = directory / 'LocalizationChecks'
    subprocess.run(['/usr/bin/xcrun', 'swiftc', str(ROOT / 'macos/JavMetaLiteMac/App/Localization.swift'), str(ROOT / 'scripts/macos/LocalizationChecks.swift'), '-o', str(executable)], check=True)
    subprocess.run([str(executable), str(RESOURCES), str(CATALOG), str(fallback)], check=True)
