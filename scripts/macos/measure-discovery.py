#!/usr/bin/env python3
"""Measure real discovery/confirmation and three-job NFO transactions on disposable fixtures."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import plistlib
import shutil
import statistics
import subprocess
import tempfile
import time
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location('lifecycle_rpc', Path(__file__).with_name('measure-worker-lifecycle.py'))
rpc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rpc)  # Its main() is guarded; importing does not parse this script's arguments.


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(chunk)
    return value.hexdigest()


def inventory(directory):
    return {str(path.relative_to(directory)): digest(path) for path in directory.rglob('*') if path.is_file()}


def run_operation(worker, method, params):
    start = worker.call(method, params)
    completed = worker.operation(start['operationId'])
    assert completed['status'] == 'completed', completed
    return completed['result']


def unknown_data(path):
    root = ET.parse(path, parser=ET.XMLParser(target=ET.TreeBuilder(insert_comments=True))).getroot()
    node = root.find('customNode')
    assert node is not None and node.find('nested') is not None
    return {'rootAttribute': root.attrib['syntheticAttribute'], 'customAttribute': node.attrib,
            'nestedText': node.find('nested').text,
            'comments': [child.text for child in root if child.tag is ET.Comment],
            'originalTitle': root.findtext('originaltitle'),
            'actorExtension': root.findtext('actor/customActor')}


def hardware():
    result = {'platform': platform.platform(), 'architecture': platform.machine()}
    if platform.system() == 'Darwin':
        for key, name in [('model', 'hw.model'), ('memoryBytes', 'hw.memsize'), ('cpu', 'machdep.cpu.brand_string')]:
            result[key] = subprocess.check_output(['/usr/sbin/sysctl', '-n', name], text=True).strip()
    return result


def volume(directory):
    result = {'stDev': directory.stat().st_dev}
    fs = os.statvfs(directory)
    result.update(blockSize=fs.f_frsize, availableBytes=fs.f_bavail * fs.f_frsize)
    if platform.system() == 'Darwin':
        # diskutil requires a device/mount, not an arbitrary nested directory.
        # Ask df for the actual containing filesystem; lexical '/' is not a volume.
        df = subprocess.run(['/bin/df', '-P', str(directory)], capture_output=True, text=True)
        result['dfExitCode'] = df.returncode
        fields = df.stdout.splitlines()[-1].split(maxsplit=5) if df.returncode == 0 else []
        device = fields[0] if len(fields) == 6 else str(directory)
        if len(fields) == 6: result.update(dfDevice=device, mountPoint=fields[5])
        command = subprocess.run(['/usr/sbin/diskutil', 'info', '-plist', device], capture_output=True)
        result['diskutilExitCode'] = command.returncode
        if command.returncode == 0:
            data = plistlib.loads(command.stdout)
            result.update({key: data[key] for key in ('DeviceIdentifier', 'MountPoint', 'FilesystemType', 'TypeBundle', 'SolidState', 'Internal') if key in data})
    return result


def worker_identity(executable):
    result = {'path': str(executable), 'executableSha256': digest(executable),
              'assemblySha256': digest(executable.parent / (executable.name + '.dll'))}
    for parent in executable.parents:
        if parent.suffix == '.app':
            info = plistlib.loads((parent / 'Contents/Info.plist').read_bytes())
            result['bundleVersion'] = info.get('CFBundleShortVersionString')
            result['bundleBuild'] = info.get('CFBundleVersion')
            manifest = parent / 'Contents/Resources/BUILD-MANIFEST.json'
            if manifest.is_file(): result['buildManifestSha256'] = digest(manifest)
            break
    return result


def measure_count(args, count):
    report = {'jobs': count, 'fixture': {'video': 'Caller-provided synthetic fixture; playback and duration are not verified by this script.'
                                     if args.synthetic_video else 'Tiny nonplayable synthetic bytes.',
                                     'artwork': 'Caller-provided local image fixture.' if args.artwork_fixture else 'Generated solid-color 600 × 400 PNG.'}}
    with tempfile.TemporaryDirectory(prefix=f'JavMetaLite discovery {count} 合成 ', dir=args.temporary_parent) as temporary:
        root = Path(temporary)
        media = root / 'media'; media.mkdir()
        report['volume'] = volume(media)
        image_extension = args.artwork_fixture.suffix.lower() if args.artwork_fixture else '.png'
        nfos = []
        for index in range(count):
            identifier = f'DEMO-{10000 + index}'
            folder = media / identifier; folder.mkdir()
            video = folder / (identifier + '.mp4')
            if args.synthetic_video: shutil.copyfile(args.synthetic_video, video)
            else: video.write_bytes(b'SYNTHETIC NONPLAYABLE DISCOVERY PERFORMANCE FIXTURE')
            image = folder / ('fanart' + image_extension)
            if args.artwork_fixture: shutil.copyfile(args.artwork_fixture, image)
            else: image.write_bytes(rpc.png())
            nfo = video.with_suffix('.nfo')
            nfo.write_text(f'<?xml version="1.0" encoding="UTF-8"?>\n<movie syntheticAttribute="keep">\n'
                           f'<!-- preserved technical fixture {identifier} -->\n'
                           f'<title>Synthetic local title {identifier}</title><id>{identifier}</id>'
                           '<originaltitle>Untouched original title 原題</originaltitle>'
                           '<actor><name>Synthetic actor</name><customActor>KEEP ACTOR EXTENSION</customActor></actor>'
                           f'<customNode custom="yes"><nested>KEEP UNKNOWN XML {identifier}</nested></customNode>\n</movie>\n')
            nfos.append(nfo)
        original = inventory(media)
        unknown = {str(path.relative_to(media)): unknown_data(path) for path in nfos}
        files = list(path for path in media.rglob('*') if path.is_file())
        report['fixture'].update(fileCount=len(files), totalBytes=sum(path.stat().st_size for path in files),
                                 videoBytes=sum(path.stat().st_size for path in files if path.suffix == '.mp4'),
                                 artworkBytes=sum(path.stat().st_size for path in files if path.suffix == image_extension))
        worker, startup_ms = rpc.timed(lambda: rpc.Worker(args.worker, root / 'engine'))
        report['workerVersion'] = worker.hello['workerVersion']
        timings = {'spawnAndInitializeMs': startup_ms}
        report['timings'] = timings
        cache_root = Path(worker.hello['cacheRoot'])
        try:
            assert worker.call('workspace.getSnapshot')['jobs'] == []
            discovery, timings['discoverInputsMs'] = rpc.timed(lambda: run_operation(worker, 'workspace.discoverInputs', {'paths': [str(media)], 'recursive': True}))
            assert len(discovery['items']) == count and not discovery['diagnostics'], discovery
            assert all(item['canImport'] and item['movieId'] for item in discovery['items'])
            assert {item['movieId'] for item in discovery['items']} == {f'DEMO-{10000 + index}' for index in range(count)}
            assert worker.call('workspace.getSnapshot')['jobs'] == [], 'Discovery loaded jobs before confirmation'
            assert inventory(media) == original, 'Discovery changed media'
            report['discoveryBeforeConfirmation'] = {'jobs': 0, 'items': len(discovery['items']), 'mediaShaUnchanged': True}
            loaded, timings['confirmImportAndLocalNfoMs'] = rpc.timed(lambda: run_operation(worker, 'workspace.importDiscovery', {
                'discoveryId': discovery['discoveryId'], 'selectedItemIds': [item['itemId'] for item in discovery['items']]}))
            assert len(loaded['importedJobIds']) == count and not loaded['diagnostics'], loaded
            snapshot, timings['firstLoadedSnapshotMs'] = rpc.timed(lambda: worker.call('workspace.getSnapshot'))
            jobs = sorted(snapshot['jobs'], key=lambda job: job['metadata']['id'])
            assert len(jobs) == count
            assert all(job['localNfoPath'] and job['metadata']['title'].startswith('Synthetic local title ') for job in jobs)
            assert all(job['metadata']['originalTitle'] == 'Untouched original title 原題' for job in jobs)
            assert inventory(media) == original, 'Confirmation changed media'
            report['confirmation'] = {'jobs': len(jobs), 'localNfosLoaded': count, 'mediaShaUnchanged': True,
                                      'rssBytesAfterConfirmation': rpc.rss(worker.proc.pid)}
            first_job = jobs[0]
            params = {'jobId': first_job['jobId'], 'artworkId': 'local-fanart'}
            first, timings['firstLocalImagePreviewMs'] = rpc.timed(lambda: worker.call('artwork.getPreview', params))
            repeats = []
            for _ in range(5):
                repeated, elapsed = rpc.timed(lambda: worker.call('artwork.getPreview', params))
                assert repeated['cacheId'] == first['cacheId']
                repeats.append(elapsed)
            timings['repeatLocalImagePreviewMs'] = repeats
            timings['repeatLocalImagePreviewMedianMs'] = statistics.median(repeats)
            report['imagePreview'] = {'width': first['width'], 'height': first['height'], 'cacheIdStable': True,
                                      'cacheFiles': sum(path.is_file() for path in cache_root.rglob('*'))}
            chosen = []
            for job in jobs[:3]:
                chosen.append(worker.call('jobs.applyEdits', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'],
                    'edits': {'title': 'Reviewed synthetic title ' + job['metadata']['id']}}))
            preview, timings['threeJobNfoBatchPreviewMs'] = rpc.timed(lambda: run_operation(worker, 'save.preview', {
                'jobIds': [job['jobId'] for job in chosen],
                'expectedReviewRevisions': {job['jobId']: job['reviewRevision'] for job in chosen}, 'async': True,
                'options': {'writeNfo': True, 'downloadPoster': False, 'downloadFanart': False, 'downloadExtrafanart': False,
                            'overwriteExisting': True, 'includeIdInTitle': False, 'targetMode': 'VideoDirectory', 'renameVideo': False}}))
            assert preview['canExecute'], preview
            assert inventory(media) == original, 'Review or preview changed media before execute'
            expected_changed = {str(Path(job['localNfoPath']).resolve().relative_to(media.resolve())) for job in chosen}
            saved, timings['threeJobNfoBatchExecuteMs'] = rpc.timed(lambda: run_operation(worker, 'save.execute', {'planId': preview['planId']}))
            assert saved['outcome'] == 'completed' and len(saved['savedJobIds']) == 3, saved
            final = inventory(media)
            assert final.keys() == original.keys(), 'Transaction added or removed an unexpected media file'
            changed = {path for path in original if original[path] != final[path]}
            assert changed == expected_changed, (changed, expected_changed)
            assert unknown == {str(path.relative_to(media)): unknown_data(path) for path in nfos}, 'Unknown/unmodified NFO content changed'
            for job in chosen:
                nfo = Path(job['localNfoPath'])
                assert ET.parse(nfo).getroot().findtext('title') == 'Reviewed synthetic title ' + job['metadata']['id']
            report['transaction'] = {'savedJobs': 3, 'changedFiles': sorted(changed), 'onlyThreeTargetNfosChanged': True,
                                     'unknownXmlAndCommentsPreserved': True, 'videoAndImageShaUnchanged': True,
                                     'otherNfoShaUnchanged': True, 'noExtraOrMissingMediaFiles': True}
        finally:
            worker.close()
        assert not cache_root.exists(), 'Preview cache was not removed at shutdown'
        report['cacheRemovedAtShutdown'] = True
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('worker', type=Path)
    parser.add_argument('--counts', type=int, nargs='+', default=[8, 71, 362, 425])
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--synthetic-video', type=Path)
    parser.add_argument('--artwork-fixture', type=Path)
    parser.add_argument('--temporary-parent', type=Path)
    args = parser.parse_args()
    if args.output.exists(): parser.error('output already exists; choose a new evidence path')
    if any(count < 3 or count > 1000 for count in args.counts): parser.error('counts must be 3..1000')
    args.worker = args.worker.resolve()
    if not args.worker.is_file(): parser.error('Worker executable is missing')
    for key in ('synthetic_video', 'artwork_fixture'):
        path = getattr(args, key)
        if path is not None:
            path = path.resolve(); setattr(args, key, path)
            if not path.is_file(): parser.error(key + ' fixture is missing')
    if args.artwork_fixture and args.artwork_fixture.suffix.lower() not in ('.png', '.jpg', '.jpeg'):
        parser.error('artwork fixture must be PNG or JPEG')
    if args.temporary_parent and not args.temporary_parent.is_dir(): parser.error('temporary-parent must already exist')
    report = {'scope': 'Real Worker only; no HTTP, UI, cross-volume throughput or playback validation.',
              'thresholds': None, 'coldDefinition': 'Each size starts a fresh process and session cache. OS filesystem cache is not purged.',
              'hardware': hardware(), 'worker': worker_identity(args.worker), 'scriptSha256': digest(Path(__file__)),
              'startedUtc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()), 'measurements': []}
    report['providedFixtures'] = {key: {'path': str(path), 'sha256': digest(path), 'bytes': path.stat().st_size}
        for key, path in [('video', args.synthetic_video), ('artwork', args.artwork_fixture)] if path}
    try:
        for count in args.counts:
            measurement = measure_count(args, count)
            report['measurements'].append(measurement)
            print(json.dumps({'jobs': count, 'timings': measurement['timings'], 'onlyThreeTargetNfosChanged': True}), flush=True)
        assert worker_identity(args.worker) == report['worker'], 'Worker binary changed during measurements'
        for fixture in report['providedFixtures'].values():
            assert digest(Path(fixture['path'])) == fixture['sha256'], 'Original supplied fixture changed'
        report['completed'] = True
    except Exception as error:
        report['completed'] = False
        report['error'] = f'{type(error).__name__}: {error}'
        raise
    finally:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n')


if __name__ == '__main__':
    main()
