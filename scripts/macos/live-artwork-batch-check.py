#!/usr/bin/env python3
"""Opt-in live LibreDMM artwork/batch check on disposable synthetic movies.

Requires an existing playable synthetic video. Remote images and provider text
stay in the new workspace; the report contains only URLs, counts and hashes.
This exercises a production Worker, not test-only provider or image injection.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('live_provider_support', Path(__file__).with_name('live-provider-check.py'))
support = importlib.util.module_from_spec(spec)
spec.loader.exec_module(support)
CheckFailure, require = support.CheckFailure, support.require
digest, value_hash, inventory = support.digest, support.value_hash, support.inventory
IDS = support.IDS


def image_info(path):
    """Read dimensions/format only. Never render, open or print the picture."""
    result = subprocess.run(['/usr/bin/sips', '-g', 'pixelWidth', '-g', 'pixelHeight', '-g', 'format', str(path)],
                            capture_output=True, text=True, check=True)
    values = {}
    for line in result.stdout.splitlines()[1:]:
        if ':' in line:
            name, value = line.strip().split(':', 1)
            values[name] = value.strip()
    width, height = int(values['pixelWidth']), int(values['pixelHeight'])
    require(width > 0 and height > 0, 'Image has no usable dimensions')
    return {'width': width, 'height': height, 'format': values['format'],
            'byteLength': path.stat().st_size, 'sha256': digest(path)}


def safe_operation(operation):
    result = operation.get('result') or {}
    return {'operationId': operation.get('operationId'), 'status': operation.get('status'),
            'errorReason': (operation.get('error') or {}).get('data', {}).get('reason'),
            'errorMessage': (operation.get('error') or {}).get('message'),
            'outcome': result.get('outcome')}


class ArtworkRun(support.LiveRun):
    def __init__(self, worker, media, references, report, report_path, prior_report=None):
        super().__init__(worker, media)
        self.references, self.report, self.report_path = references, report, report_path
        self.selected_values = {}
        self.plot_markers = {}
        self.prior_report = prior_report

    def checkpoint(self):
        support.save_report(self.report_path, self.report)

    def preview_batch(self, job_ids, overwrite=True, **extra):
        jobs = [self.job(job_id) for job_id in job_ids]
        options = {'writeNfo': True, 'downloadPoster': True, 'downloadFanart': True,
                   'downloadExtrafanart': True, 'replaceLocalExtrafanart': True,
                   'overwriteExisting': overwrite, 'includeIdInTitle': False,
                   'targetMode': 'VideoDirectory', 'renameVideo': False, **extra}
        started = self.call('save.preview', {'jobIds': job_ids,
            'expectedReviewRevisions': {job['jobId']: job['reviewRevision'] for job in jobs},
            'async': True, 'options': options})
        done = self.wait(started['operationId'])
        require(done['status'] == 'completed', 'Batch preview did not complete')
        return done['result']

    def select_artwork(self, job, artwork_id, selected=True):
        return self.remember(self.call('artwork.select', {'jobId': job['jobId'],
            'expectedReviewRevision': job['reviewRevision'], 'artworkId': artwork_id, 'selected': selected}))

    def pixel_compare(self, reference, output, mode):
        process = subprocess.run([self.report['imageComparator']['executable'], '--source', reference,
                                  '--output', str(output), '--mode', mode], capture_output=True, text=True)
        comparison = json.loads(process.stdout)
        require(process.returncode in (0, 1), 'Independent image comparison could not run')
        return {'mode': mode, 'exitCode': process.returncode, **comparison}

    def reuse_reference(self, identifier, asset, target_prefix):
        if not self.prior_report:
            return False
        previous_movie = next((row for row in self.prior_report.get('results', []) if row.get('movieId') == identifier), {})
        previous = next((a for a in previous_movie.get('assets', []) if a.get('artworkId') == asset['artworkId']
                         and a.get('role') == asset['role'] and a.get('outcome') == 'success'
                         and a.get('primarySourceUrl', a.get('sourceUrl')) == asset['primarySourceUrl']), None)
        if previous is None:
            return False
        try:
            base = (Path(self.prior_report['workspace']) / 'reference-images' / identifier).resolve(strict=True)
            source = Path(previous['referencePath'])
            require(not source.is_symlink() and source.resolve(strict=True).is_relative_to(base), 'Prior reference is outside its declared image directory')
            require(source.suffix in ('.jpg', '.png'), 'Unexpected prior reference format')
            info = image_info(source)
            require(info['sha256'] == previous['sha256'] and info['width'] == previous['width'] and info['height'] == previous['height'],
                    'Prior reference bytes or dimensions changed')
            target = target_prefix.with_suffix(source.suffix)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
            require(digest(target) == info['sha256'], 'Prior reference copy changed')
            asset.update(outcome='success', referencePath=str(target), **info,
                rawDownloadSha256=previous.get('rawDownloadSha256'), rawByteEvidence=previous.get('rawByteEvidence'),
                isLowResolution=previous.get('isLowResolution'), sourceUrl=previous.get('resolvedLocation', previous['sourceUrl']),
                resolvedLocation=previous.get('resolvedLocation', previous['sourceUrl']), usedFallback=previous.get('usedFallback', False),
                providerFetchAttempts=previous.get('providerFetchAttempts', []),
                referenceAcquisition='prior-run-verified-by-sha',
                referenceRunStartedAt=previous.get('referenceRunStartedAt', self.prior_report['startedAt']),
                priorReferencePath=str(source), attempts=[])
            return True
        except (OSError, KeyError, ValueError, CheckFailure) as error:
            asset['priorReferenceNotReused'] = self.clean(error)
            return False

    def query_and_select(self, job, row):
        identifier = row['movieId']
        local_sources = {field: next(c['source'] for c in job['candidates'] if c['field'] == field and c['selected'])
                         for field in ('actorsText', 'rating')}
        started_at = time.monotonic()
        started = self.call('jobs.search', {'jobId': job['jobId'], 'sourceMode': 'libredmm'})
        done = self.wait(started['operationId'])
        job = self.job(job['jobId'])
        result = done.get('result') or {}
        item = next((x for x in result.get('jobResults', []) if x['jobId'] == job['jobId']), {})
        candidates = {c['field']: c['value'] for c in job['candidates'] if c['source'] == 'libredmm'}
        row['provider'] = {'source': 'libredmm', 'operationStatus': done['status'], 'jobStatus': item.get('status'),
            'warnings': [self.clean(x) for x in result.get('warnings', [])],
            'jobWarnings': [self.clean(x) for x in item.get('warnings', [])],
            'errorReason': (item.get('error') or done.get('error') or {}).get('data', {}).get('reason'),
            'sourceUrl': job['metadata'].get('sourceUrl'), 'sourceCandidatePresent': bool(candidates),
            'fieldPresence': {field: bool(candidates.get(field, '').strip()) for field in support.FIELDS},
            'fieldValueSha256': {field: value_hash(value) for field, value in candidates.items()},
            'actorNameCount': len([x for x in candidates.get('actorsText', '').split(',') if x.strip()]),
            'actorCountMethod': 'nonempty comma-separated provider names',
            'elapsedMs': round((time.monotonic() - started_at) * 1000, 2)}
        require(done['status'] == 'completed' and item.get('status') == 'Completed' and candidates.get('title', '').strip(),
                'LibreDMM did not return completed usable metadata')
        selected_values = {}
        for field in support.SELECT_FIELDS:
            if candidates.get(field, '').strip():
                job = self.select(job, field, 'libredmm')
                require(job['metadata'][field] == candidates[field], 'Selected provider value did not apply: ' + field)
                selected_values[field] = candidates[field]
        for field, source in local_sources.items():
            job = self.select(job, field, source)
        marker = 'Synthetic live artwork batch QA annotation: ' + identifier
        job = self.edit(job, {'plot': marker})
        cover = next((a for a in job['artwork'] if a['kind'] == 'fanart' and a['source'] == 'libredmm'), None)
        require(cover is not None, 'LibreDMM returned no registered full cover')
        job = self.select_artwork(job, cover['artworkId'])
        for artwork in list(job['artwork']):
            if artwork['kind'] == 'sample' and artwork['source'] == 'local' and artwork['selected']:
                job = self.select_artwork(job, artwork['artworkId'], False)
        samples = sorted((a for a in job['artwork'] if a['kind'] == 'sample' and a['source'] != 'local'),
                         key=lambda a: int(a['artworkId'].split('-')[-1]))
        require(samples, 'LibreDMM returned no registered remote samples')
        # Reset remote order explicitly; sample-N indexes the registered available list.
        # This is review-only and leaves every sample from this sole provider selected.
        for artwork in samples:
            job = self.select_artwork(job, artwork['artworkId'], True)
        urls = job['metadata']['screenshotUrls']
        require(len(urls) == len(samples) and all(url.startswith(('https://', 'http://')) for url in urls),
                'Selected samples differ from the sole provider registered list')
        require(len(urls) <= 50, 'Source has more than 50 samples; refusing to describe capped output as complete')
        require(job['canReplaceLocalExtrafanart'], 'Complete replacement is not available for this matched-ID source')
        row.update(jobId=job['jobId'], selectedProviderFields={field: {'source': 'libredmm', 'valueSha256': value_hash(value)}
                   for field, value in selected_values.items()}, selectedSource='libredmm',
                   selectedSampleUrlCount=len(urls), remoteSampleCap=50, capTruncated=False,
                   selectedMetadataSha256=value_hash(job['metadata']), assets=[])
        self.selected_values[job['jobId']] = selected_values
        self.plot_markers[job['jobId']] = marker
        cover_url = job['metadata'].get('coverUrl') or job['metadata'].get('fallbackCoverUrl')
        assets = [(cover, cover_url, 'fullCover')]
        poster = next((a for a in job['artwork'] if a['kind'] == 'poster' and a['source'] == 'libredmm'), None)
        if poster:
            assets.append((poster, job['metadata'].get('posterUrl'), 'providerPosterReference'))
        row['providerPosterCandidatePresent'] = poster is not None
        assets.extend((sample, url, 'sample') for sample, url in zip(samples, urls))
        row['posterOutputPolicy'] = 'Generated from selected full cover: rightmost floor(width/2) for landscape, full image for portrait. Provider poster is fetched separately as reference when present.'
        for index, (artwork, url, role) in enumerate(assets):
            asset = {'artworkId': artwork['artworkId'], 'role': role, 'primarySourceUrl': url,
                     'sourceUrl': url, 'outcome': 'fail', 'referenceAcquisition': 'current-run-worker-preview'}
            row['assets'].append(asset)
            target_prefix = self.references / identifier / (f'{index:03d}-' + artwork['artworkId'])
            if self.reuse_reference(identifier, asset, target_prefix):
                self.checkpoint()
                print(f'{identifier} {artwork["artworkId"]}: verified prior reference', flush=True)
                continue
            asset['attempts'] = []
            for attempt_number in (1,):
                attempt = {'attempt': attempt_number}; asset['attempts'].append(attempt)
                try:
                    preview_start = self.call('artwork.getPreview', {'jobId': job['jobId'], 'artworkId': artwork['artworkId'], 'async': True})
                    preview_done = self.wait(preview_start['operationId'], timeout=60)
                    attempt['operation'] = safe_operation(preview_done)
                    if attempt['operation']['errorMessage']:
                        attempt['operation']['errorMessage'] = self.clean(attempt['operation']['errorMessage'])
                    require(preview_done['status'] == 'completed', 'Remote image preview failed')
                    preview = preview_done['result']
                    cache = Path(preview['path'])
                    require(cache.is_file() and cache.resolve().is_relative_to(Path(self.worker.initialized['cacheRoot']).resolve()),
                            'Artwork preview returned a path outside its managed cache')
                    target = target_prefix.with_suffix(cache.suffix)
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(cache, target)
                    info = image_info(target)
                    require(info['sha256'] == preview['cacheId'] and info['byteLength'] == preview['byteLength'], 'Preview cache hash/length mismatch')
                    require((info['width'], info['height']) == (preview['width'], preview['height']), 'Independent dimensions differ from Worker preview')
                    require(info['format'] in ('jpeg', 'png'), 'Unexpected preview image format')
                    asset.update(outcome='success', referencePath=str(target), **info,
                        rawDownloadSha256=info['sha256'] if info['format'] == 'jpeg' else None,
                        rawByteEvidence='Worker preserves original JPEG bytes' if info['format'] == 'jpeg' else 'Preview cache bytes; PNG may be a format-normalized preview',
                        isLowResolution=preview['isLowResolution'],
                        sourceUrl=preview.get('resolvedLocation', url), resolvedLocation=preview.get('resolvedLocation', url),
                        usedFallback=preview.get('usedFallback', False), providerFetchAttempts=preview.get('attempts', []))
                    attempt['outcome'] = 'success'
                    break
                except Exception as error:
                    attempt.update(outcome='fail', errorType=type(error).__name__, error=self.clean(error))
                    asset.update(errorType=type(error).__name__, error=self.clean(error))
            self.checkpoint()
            print(f'{identifier} {artwork["artworkId"]}: {asset["outcome"]}', flush=True)
        require(self.job(job['jobId'])['reviewRevision'] == job['reviewRevision'], 'Read-only image previews changed review revision')
        successes = [a for a in row['assets'] if a['role'] == 'sample' and a['outcome'] == 'success']
        unique_hashes = list(dict.fromkeys(a['sha256'] for a in successes))
        row.update(successfulSampleReferenceCount=len(successes),
                   successfulSampleDownloadsThisRun=sum(a['referenceAcquisition'] == 'current-run-worker-preview' for a in successes),
                   reusedSampleReferences=sum(a['referenceAcquisition'] == 'prior-run-verified-by-sha' for a in successes),
                   uniqueSampleByteShaCount=len(unique_hashes),
                   uniqueSampleByteSha256InOutputOrder=unique_hashes)
        row['outcome'] = 'ready' if all(a['outcome'] == 'success' for a in row['assets']) else 'partial'
        self.checkpoint()
        return job

    def verify_saved(self, row, save_item):
        job_id, identifier = row['jobId'], row['movieId']
        folder = self.media / 'selected' / identifier
        nfo, poster, fanart = folder / 'movie.nfo', folder / 'poster.png', folder / 'fanart.png'
        xml = ET.parse(nfo).getroot()
        for field, value in self.selected_values[job_id].items():
            require(xml.findtext(support.XML_FIELDS[field], '') == value, 'NFO provider field mismatch: ' + field)
        require(xml.findtext('plot') == self.plot_markers[job_id], 'Synthetic manual plot annotation was not saved')
        require(xml.attrib.get('qaRoot') == 'KEEP LIVE QA ROOT', 'Unknown NFO root attribute lost')
        require(xml.findtext('qaUnknown/nested') == 'KEEP UNKNOWN XML', 'Unknown NFO node lost')
        require(xml.findtext('resume/position') == '1.25' and xml.findtext('resume/total') == '2', 'NFO resume lost')
        actor = xml.find('actor')
        require(actor is not None and actor.attrib.get('qaActor') == 'KEEP ACTOR ATTRIBUTE'
                and actor.findtext('qaExtension') == 'KEEP ACTOR EXTENSION', 'Actor extension lost')
        require(xml.findtext('rating') == 'N/A', 'Raw NFO rating lost')
        require(xml.findtext("thumb[@aspect='poster']") == poster.name and xml.findtext('fanart/thumb') == fanart.name,
                'NFO artwork references do not name saved files')
        cover = next(a for a in row['assets'] if a['role'] == 'fullCover')
        require(cover['outcome'] == 'success', 'No reference full cover available')
        fanart_info, poster_info = image_info(fanart), image_info(poster)
        require((fanart_info['width'], fanart_info['height']) == (cover['width'], cover['height']), 'Fanart is not complete cover size')
        expected_width = max(1, cover['width'] // 2) if cover['width'] > cover['height'] else cover['width']
        require((poster_info['width'], poster_info['height']) == (expected_width, cover['height']), 'Poster dimensions differ from right-half crop policy')
        originals = [a for a in row['assets'] if a['role'] == 'sample' and a['outcome'] == 'success']
        # Explicit first occurrence order, including distinct URLs with identical bytes.
        unique = []; seen = set()
        for asset in originals:
            if asset['sha256'] not in seen:
                seen.add(asset['sha256']); unique.append(asset)
        actual = sorted((folder / 'extrafanart').glob('fanart*.jpg'), key=lambda p: int(p.stem[6:]))
        row['actualSampleOutputCount'] = len(actual)
        require(len(actual) == len(unique) == row['uniqueSampleByteShaCount'], 'Actual sample output count differs from all selected unique images')
        require(not (folder / 'extrafanart/local-original.png').exists(), 'Consented old local sample was not retired')
        mappings, inconclusive = [], []
        fanart_comparison = self.pixel_compare(cover['referencePath'], fanart, 'full')
        poster_comparison = self.pixel_compare(cover['referencePath'], poster, 'poster')
        for comparison, path in ((fanart_comparison, fanart), (poster_comparison, poster)):
            if not comparison['passed']:
                inconclusive.append({'path': str(path), 'reason': 'Independent pixel comparison failed; source CDN bytes can change between reference and save fetches.'})
        for index, (asset, path) in enumerate(zip(unique, actual), 1):
            require(path.name == f'fanart{index}.jpg', 'Sample output numbering/order changed')
            info = image_info(path)
            require((info['width'], info['height']) == (asset['width'], asset['height']), 'Sample dimensions differ from reference')
            raw_match = info['sha256'] == asset['rawDownloadSha256'] if asset['rawDownloadSha256'] else None
            mapping = {'path': str(path), 'firstSourceUrl': asset['sourceUrl'], 'firstArtworkId': asset['artworkId'],
                       'selectedUrlsWithSameBytes': [a['sourceUrl'] for a in originals if a['sha256'] == asset['sha256']],
                       'referenceSha256': asset['sha256'], 'rawByteShaMatches': raw_match, **info}
            mappings.append(mapping)
            if raw_match is not True:
                inconclusive.append({'path': str(path), 'reason': 'Reference and save are separate HTTP fetches; CDN change or format conversion cannot be ruled out.'})
        outputs = set(save_item['outputs'])
        expected_outputs = {str(nfo), str(poster), str(fanart), str(folder / (identifier + '.mp4'))} | {str(p) for p in actual}
        require(outputs == expected_outputs, 'Save report output paths differ from observed expected files')
        row.update(outcome='inconclusive' if inconclusive else 'fully', savedNfoSha256=digest(nfo),
                   outputPoster={'path': str(poster), **poster_info, 'expectedWidth': expected_width,
                       'fullCoverReferenceSha256': cover['sha256'], 'pixelComparison': poster_comparison,
                       'validation': 'Independent ImageIO/CoreGraphics right-half crop; numerical sRGB pixel comparison with declared JPEG/decoder tolerance, no visual acceptance'},
                   outputFanart={'path': str(fanart), **fanart_info, 'fullCoverSourceUrl': cover['sourceUrl'],
                       'fullCoverReferenceSha256': cover['sha256'], 'pixelComparison': fanart_comparison,
                       'validation': 'Independent full-cover pixel comparison; existing PNG target is re-encoded, so raw JPEG SHA equality is not expected'},
                   sampleMappings=mappings, byteComparisonInconclusive=inconclusive,
                   retiredLocalSamples=['extrafanart/local-original.png'],
                   safety={'providerFieldsSaved': True, 'unknownXmlPreserved': True, 'resumePreserved': True,
                           'actorExtensionPreserved': True, 'rawRatingPreserved': True, 'nfoArtworkReferencesMatch': True,
                           'completeSelectedSamplesOutput': True, 'noRemoteSampleCapTruncation': True})


def make_fixture(media, relative, identifier, video, poster):
    folder = media / relative; folder.mkdir(parents=True)
    (folder / 'extrafanart').mkdir()
    shutil.copyfile(video, folder / (identifier + '.mp4'))
    for target in ('poster.png', 'fanart.png', 'extrafanart/local-original.png'):
        shutil.copyfile(poster, folder / target)
    (folder / 'movie.nfo').write_bytes(support.initial_nfo(identifier))
    for target in ('KEEP.txt', 'extrafanart/KEEP.txt'):
        (folder / target).write_text('Synthetic untargeted file: KEEP BYTE FOR BYTE\n')
    return folder / (identifier + '.mp4')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run-live', action='store_true')
    parser.add_argument('--worker', type=Path, required=True)
    parser.add_argument('--video-template', type=Path, required=True)
    parser.add_argument('--poster-template', type=Path, default=ROOT / 'fixtures/macos/synthetic-cover-5x2.png')
    parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--reference-report', type=Path, help='Optional prior report: reuse matching registered-URL images only after SHA/dimension validation. Never calls prior data a fresh download.')
    args = parser.parse_args()
    if not args.run_live:
        parser.error('Explicit --run-live is required before network or workspace writes.')
    for name in ('worker', 'video_template', 'poster_template'):
        path = getattr(args, name).expanduser().resolve(strict=True)
        require(path.is_file(), name + ' must be a regular file')
        setattr(args, name, path)
    require(os.access(args.worker, os.X_OK), 'Worker must be executable')
    args.workspace, args.report = args.workspace.expanduser().absolute(), args.report.expanduser().absolute()
    require(not args.workspace.exists() and not args.workspace.is_symlink(), 'Workspace exists; refusing to overwrite')
    require(not args.report.exists() and not args.report.is_symlink(), 'Report exists; refusing to overwrite')
    require(args.workspace.parent.is_dir(), 'Workspace parent must already exist')
    prior_report = None
    if args.reference_report:
        args.reference_report = args.reference_report.expanduser().resolve(strict=True)
        require(args.reference_report.is_file() and args.reference_report.stat().st_size < 8 * 1024 * 1024, 'Prior reference report must be a JSON file under 8 MiB')
        prior_report = json.loads(args.reference_report.read_text())
        require(prior_report.get('schemaVersion') == 1 and prior_report.get('sourceMode') == 'libredmm', 'Unsupported prior reference report')
    report = {'schemaVersion': 1, 'startedAt': support.now(), 'runLive': True, 'outcome': 'fail',
        'workspace': str(args.workspace), 'movieIds': list(IDS), 'sourceMode': 'libredmm',
        'worker': {'path': str(args.worker), 'executableSha256': digest(args.worker),
            'managedAssemblySha256': digest(args.worker.parent / (args.worker.name + '.dll'))},
        'templates': {'videoSha256': digest(args.video_template), 'localArtworkSha256': digest(args.poster_template)},
        'harnessSha256': digest(Path(__file__)),
        'scope': 'Real metadata/full-cover/provider-poster/all-sample requests; synthetic playable videos; ordered three-job NFO/artwork save. No real video, remote image presentation, UI, signing or publication.',
        'limitations': ['Dimensions/formats checked with sips; image pixels compared numerically with independent ImageIO/CoreGraphics. No visual image acceptance.',
            'Remote references and save are separate HTTP requests. Changed raw JPEG bytes are inconclusive, never reported as identical.',
            'Cancellation/rollback/device-loss are covered separately by offline regression, not deliberately induced during this live download run.'],
        'results': [], 'safety': {}}
    if prior_report:
        report['referenceReuse'] = {'report': str(args.reference_report), 'reportSha256': digest(args.reference_report),
            'priorRunStartedAt': prior_report['startedAt'],
            'policy': 'Current registered primary URL, artwork ID and role must match; prior file must be inside its reference-images/movie directory and SHA/dimensions unchanged. Save still performs its own live downloads.'}
    worker = run = None
    media = args.workspace / 'synthetic-media'
    try:
        args.workspace.mkdir(exist_ok=False)
        comparator_source = ROOT / 'scripts/macos/compare-image-output.swift'
        comparator = args.workspace / 'compare-image-output'
        subprocess.run(['xcrun', 'swiftc', str(comparator_source), '-o', str(comparator)], check=True,
                       capture_output=True, text=True)
        report['imageComparator'] = {'source': str(comparator_source), 'sourceSha256': digest(comparator_source),
                                    'executable': str(comparator), 'executableSha256': digest(comparator)}
        videos = [make_fixture(media, 'selected/' + identifier, identifier, args.video_template, args.poster_template) for identifier in IDS]
        guard = make_fixture(media, 'protected/SSIS-001', 'PROTECT-999', args.video_template, args.poster_template)
        before = inventory(media)
        report['beforeInventory'] = before
        baseline = args.workspace / 'baseline-before'
        shutil.copytree(media, baseline)
        require(inventory(baseline) == before, 'Baseline snapshot does not match fixture bytes')
        report['baselineSnapshot'] = str(baseline)
        harness = support.load_harness(args.worker)
        worker = harness.Worker(args.workspace)
        report['worker']['reportedVersion'] = worker.initialized['workerVersion']
        run = ArtworkRun(worker, media, args.workspace / 'reference-images', report, args.report, prior_report)
        imported_start = run.call('workspace.importInputs', {'paths': [str(p) for p in videos + [guard]], 'recursive': False})
        imported = run.wait(imported_start['operationId'])
        require(imported['status'] == 'completed' and not imported['result']['warnings'], 'Fixture import failed or warned')
        jobs = [run.job(job_id) for job_id in imported['result']['importedJobIds']]
        require(len(jobs) == 4, 'Expected three selected jobs and one unselected protection job')
        guard_job = next(job for job in jobs if job['metadata']['id'] == 'PROTECT-999')
        report['protectedJob'] = {'jobId': guard_job['jobId'], 'movieId': 'PROTECT-999', 'queried': False,
                                  'initialReviewRevision': guard_job['reviewRevision']}
        job_ids = []
        for identifier in IDS:
            job = next(job for job in jobs if job['metadata']['id'] == identifier)
            row = {'movieId': identifier, 'jobId': job['jobId'], 'outcome': 'fail'}
            report['results'].append(row); job_ids.append(job['jobId'])
            try:
                run.query_and_select(job, row)
            except Exception as error:
                row.update(outcome='fail', errorType=type(error).__name__, error=run.clean(error))
            run.checkpoint()
        require(inventory(media) == before, 'Import/query/review/preview downloads modified input fixtures')
        report['safety']['readOnlyStagesPreservedAllInputs'] = True
        assets = [asset for row in report['results'] for asset in row.get('assets', [])]
        report['referenceSummary'] = {
            'registeredAssetCount': len(assets),
            'successfulReferenceCount': sum(a['outcome'] == 'success' for a in assets),
            'freshDownloadCount': sum(a['outcome'] == 'success' and a['referenceAcquisition'] == 'current-run-worker-preview' for a in assets),
            'reusedReferenceCount': sum(a['outcome'] == 'success' and a['referenceAcquisition'] == 'prior-run-verified-by-sha' for a in assets),
            'sourceAssetsComplete': all(row['outcome'] == 'ready' for row in report['results'])}
        if not all(row['outcome'] == 'ready' for row in report['results']):
            report['outcome'] = 'partial' if any(row['outcome'] in ('ready', 'partial') for row in report['results']) else 'fail'
            raise CheckFailure('At least one source/reference image failed; batch save skipped to preserve all fixtures')
        blocked = run.preview_batch(job_ids, overwrite=False)
        require(not blocked['canExecute'], 'Existing outputs did not require overwrite consent')
        run.expect_error('save.execute', {'planId': blocked['planId']}, 'PATH_CONFLICT')
        require(inventory(media) == before, 'Overwrite-blocked batch changed fixture files')
        report['safety']['overwriteBlockedBeforeWrite'] = True
        report['overwriteBlockedPreview'] = {'planId': blocked['planId'], 'canExecute': False,
            'blockingConflicts': [run.clean(x) for x in blocked['blockingConflicts']]}
        conflict = run.preview_batch(job_ids, targetMode='CustomRootNumberFolder',
                                     customRootDirectory=str(media / 'protected'))
        require(not conflict['canExecute'] and any('Cross-job input/output conflict' in x for x in conflict['blockingConflicts']),
                'Unselected registered job inputs were not protected by aggregate conflict detection')
        run.expect_error('save.execute', {'planId': conflict['planId']}, 'PATH_CONFLICT')
        require(inventory(media) == before, 'Protected-input blocked batch changed fixture files')
        report['safety']['unselectedInputConflictBlockedBeforeWrite'] = True
        report['protectedInputPreview'] = {'planId': conflict['planId'], 'canExecute': False,
            'blockingConflicts': [run.clean(x) for x in conflict['blockingConflicts']]}
        old = run.preview_batch(job_ids)
        require(old['canExecute'], 'Consented original-directory batch was blocked')
        changed = run.job(job_ids[1])
        marker = 'Synthetic live artwork batch QA revision after preview: ' + IDS[1]
        changed = run.edit(changed, {'plot': marker}); run.plot_markers[job_ids[1]] = marker
        run.expect_error('save.preview', {'jobIds': job_ids,
            'expectedReviewRevisions': {job['jobId']: job['reviewRevision'] for job in old['jobs']}}, 'REVISION_CONFLICT')
        require(run.job(job_ids[1])['reviewRevision'] == changed['reviewRevision'], 'Rejected stale review map changed the current review')
        report['safety']['staleReviewRevisionMapRejectedBeforeWrite'] = True
        run.expect_error('save.execute', {'planId': old['planId']}, 'PLAN_STALE')
        require(inventory(media) == before, 'Stale batch execution changed fixture files')
        report['safety']['oldBatchPlanRejectedBeforeWrite'] = True
        fresh = run.preview_batch(job_ids)
        require(fresh['canExecute'] and [j['jobId'] for j in fresh['jobs']] == job_ids, 'Fresh preview order/eligibility changed')
        report['freshPreview'] = fresh
        for preview_job in fresh['jobs']:
            identifier = next(row['movieId'] for row in report['results'] if row['jobId'] == preview_job['jobId'])
            folder = (media / 'selected' / identifier).resolve()
            require(any(c['operation'] == 'KeepFile' and c['target'] == str(folder / (identifier + '.mp4')) for c in preview_job['changes']),
                    'Preview did not explicitly preserve video')
            require(all(Path(c['target']).resolve().is_relative_to(folder) for c in preview_job['changes']), 'Preview writes outside the selected movie directory')
        run.checkpoint()
        started = run.call('save.execute', {'planId': fresh['planId']})
        report['saveOperationId'] = started['operationId']; run.checkpoint()
        done = run.wait(started['operationId'], timeout=900)
        result = done.get('result') or {}
        report['batchResult'] = {'operationStatus': done['status'], **result}
        if result.get('outcome') != 'completed':
            report['outcome'] = 'partial' if result.get('savedJobIds') else 'fail'
        report['afterInventory'] = after = inventory(media)
        report['changedPaths'] = sorted(path for path in before.keys() | after.keys() if before.get(path) != after.get(path))
        require(done['status'] == 'completed' and result.get('outcome') == 'completed', 'Batch did not complete all jobs')
        require(result.get('savedJobIds') == job_ids and not result.get('notStartedJobIds') and not result.get('cleanupWarnings'), 'Batch rows incomplete or cleanup required')
        require([item['jobId'] for item in result['jobResults']] == job_ids and all(item['status'] == 'Completed' for item in result['jobResults']), 'Batch result order/status mismatch')
        repeated = run.call('save.execute', {'planId': fresh['planId']})
        require(repeated['operationId'] == started['operationId'] and inventory(media) == after, 'Duplicate execute repeated mutations')
        report['safety']['duplicateExecuteSameOperationNoWrites'] = True
        protected_paths = [p for p in before if p.startswith('protected/') or p.endswith('.mp4') or p.endswith('KEEP.txt')]
        require(all(after.get(path) == before[path] for path in protected_paths), 'An unselected/video/untargeted file changed')
        report['protectedFileHashes'] = {p: after[p] for p in protected_paths}
        report['safety']['videosAndUnselectedAndUntargetedBytesPreserved'] = True
        report['protectedJob']['finalReviewRevision'] = run.job(guard_job['jobId'])['reviewRevision']
        require(report['protectedJob']['finalReviewRevision'] == guard_job['reviewRevision'], 'Unselected job review was mutated')
        for row, item in zip(report['results'], result['jobResults']):
            try:
                run.verify_saved(row, item)
            except Exception as error:
                row.update(outcome='fail', errorType=type(error).__name__, error=run.clean(error))
            run.checkpoint()
            print(f'{row["movieId"]}: {row["outcome"]}; selected={row["selectedSampleUrlCount"]}; outputs={row.get("actualSampleOutputCount", 0)}', flush=True)
        journals = [json.loads(p.read_text()) for p in sorted((worker.data / 'recovery').glob('*.json'))]
        report['recoveryJournals'] = journals
        require(len(journals) == 3 and all(journal['status'] == 'completed' for journal in journals), 'Recovery journals are not three clean completed transactions')
        require(not run.call('workspace.getSnapshot')['recoveryWarnings'], 'Recovery warnings remain')
        report['safety']['recoveryJournalsCompleted'] = True
        require(not any(p.name.startswith('.javmetalite-') for p in media.rglob('*')), 'Transaction temporary paths remain in fixture tree')
        report['transactionGuardsPassed'] = all(report['safety'].values())
        outcomes = [row['outcome'] for row in report['results']]
        report['outcome'] = 'fail' if 'fail' in outcomes else 'fully' if all(value == 'fully' for value in outcomes) else 'partial'
    except Exception as error:
        report['fatalError'] = {'type': type(error).__name__, 'message': run.clean(error) if run else str(error)[:500]}
    finally:
        if media.exists():
            report['finalInventory'] = inventory(media)
        if worker is not None:
            try:
                if run:
                    for operation_id in list(run.active):
                        worker.request('operations.cancel', {'operationId': operation_id})
                worker.close()
                report['worker']['exitCode'] = worker.proc.returncode
                report['worker']['normalShutdown'] = worker.proc.returncode == 0
                if worker.proc.returncode != 0:
                    report['outcome'] = 'fail'
            except Exception as error:
                report['worker']['shutdownError'] = type(error).__name__; report['outcome'] = 'fail'
        report['finishedAt'] = support.now()
        support.save_report(args.report, report)
    print(f'LIVE ARTWORK BATCH CHECK: {report["outcome"]}; report={args.report}', flush=True)
    return 0 if report['outcome'] == 'fully' else 1


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (CheckFailure, OSError) as error:
        print('LIVE ARTWORK CHECK REFUSED: ' + str(error), file=sys.stderr)
        sys.exit(2)
