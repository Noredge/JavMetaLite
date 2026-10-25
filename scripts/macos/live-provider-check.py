#!/usr/bin/env python3
"""Opt-in real provider/NFO check using isolated, user-supplied synthetic video.

No remote artwork or video is requested. Provider text stays in the disposable
workspace; the report contains only presence/counts/hashes and source URLs.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sys
import time
from datetime import datetime, timezone
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
IDS = ('SSIS-001', 'STARS-804', 'IPX-177')
PROVIDERS = ('libredmm', 'r18dev')
FIELDS = ('title', 'originalTitle', 'releaseDate', 'runtimeMinutes', 'maker',
          'director', 'label', 'actorsText', 'genresText', 'plot', 'rating')
SELECT_FIELDS = ('title', 'originalTitle', 'releaseDate', 'runtimeMinutes', 'maker')
XML_FIELDS = {'title': 'title', 'originalTitle': 'originaltitle', 'releaseDate': 'premiered',
              'runtimeMinutes': 'runtime', 'maker': 'studio'}


class CheckFailure(Exception):
    pass


def require(condition, message):
    if not condition:
        raise CheckFailure(message)


def now():
    return datetime.now(timezone.utc).isoformat()


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def value_hash(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=False,
                                     separators=(',', ':')).encode()).hexdigest()


def inventory(directory):
    return {str(path.relative_to(directory)): digest(path)
            for path in sorted(directory.rglob('*')) if path.is_file()}


def save_report(path, report):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.tmp')
    temporary.write_text(json.dumps(report, indent=2, ensure_ascii=False) + '\n')
    temporary.replace(path)


def load_harness(worker):
    previous = sys.argv
    try:
        sys.argv = [str(Path(__file__)), str(worker)]
        spec = importlib.util.spec_from_file_location('live_protocol_harness', ROOT / 'scripts/macos/test_worker.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module
    finally:
        sys.argv = previous


def initial_nfo(identifier):
    movie = ET.Element('movie', {'qaRoot': 'KEEP LIVE QA ROOT'})
    for key, value in [('id', identifier), ('title', 'Synthetic local baseline'),
                       ('plot', 'Synthetic local plot'), ('rating', 'N/A')]:
        ET.SubElement(movie, key).text = value
    ET.SubElement(ET.SubElement(movie, 'qaUnknown'), 'nested').text = 'KEEP UNKNOWN XML'
    resume = ET.SubElement(movie, 'resume')
    ET.SubElement(resume, 'position').text = '1.25'
    ET.SubElement(resume, 'total').text = '2'
    actor = ET.SubElement(movie, 'actor', {'qaActor': 'KEEP ACTOR ATTRIBUTE'})
    ET.SubElement(actor, 'name').text = 'Synthetic QA Actor'
    ET.SubElement(actor, 'role').text = 'Synthetic local role'
    ET.SubElement(actor, 'qaExtension').text = 'KEEP ACTOR EXTENSION'
    return ET.tostring(movie, encoding='utf-8', xml_declaration=True)


class LiveRun:
    def __init__(self, worker, media):
        self.worker = worker
        self.media = media
        self.active = set()
        self.secrets = set()

    def remember(self, job):
        for candidate in job.get('candidates', []):
            value = candidate.get('value')
            if isinstance(value, str) and len(value) >= 6:
                self.secrets.add(value)
        return job

    def clean(self, value):
        text = str(value)
        for secret in sorted(self.secrets, key=len, reverse=True):
            text = text.replace(secret, '[provider field redacted]')
        return text[:1000]

    def call(self, method, params=None):
        response = self.worker.request(method, params)
        if 'error' in response:
            reason = response['error'].get('data', {}).get('reason', 'RPC_ERROR')
            raise CheckFailure(method + ': ' + reason)
        require('result' in response, method + ': missing result')
        return response['result']

    def wait(self, operation_id, timeout=180):
        self.active.add(operation_id)
        try:
            result = self.worker.operation(operation_id, timeout=timeout)
        except Exception:
            self.worker.request('operations.cancel', {'operationId': operation_id})
            try:
                self.worker.operation(operation_id, timeout=20)
                self.active.discard(operation_id)
            except Exception:
                pass
            raise
        self.active.discard(operation_id)
        return result

    def job(self, job_id):
        return self.remember(self.call('jobs.get', {'jobId': job_id}))

    def edit(self, job, edits):
        return self.remember(self.call('jobs.applyEdits', {'jobId': job['jobId'],
            'expectedReviewRevision': job['reviewRevision'], 'edits': edits}))

    def select(self, job, field, source):
        return self.remember(self.call('jobs.selectCandidate', {'jobId': job['jobId'],
            'expectedReviewRevision': job['reviewRevision'], 'field': field, 'source': source}))

    def preview(self, job, overwrite):
        start = self.call('save.preview', {'jobId': job['jobId'], 'expectedReviewRevision': job['reviewRevision'],
            'async': True, 'options': {'writeNfo': True, 'downloadPoster': False, 'downloadFanart': False,
            'downloadExtrafanart': False, 'replaceLocalExtrafanart': False, 'overwriteExisting': overwrite,
            'includeIdInTitle': False, 'targetMode': 'VideoDirectory', 'renameVideo': False}})
        done = self.wait(start['operationId'])
        require(done['status'] == 'completed' and 'result' in done, 'Preview operation did not complete')
        return done['result']

    def expect_error(self, method, params, reason):
        response = self.worker.request(method, params)
        require(response.get('error', {}).get('data', {}).get('reason') == reason,
                method + ': expected ' + reason)

    def check_movie(self, identifier, row):
        folder = self.media / identifier
        video, nfo, poster = folder / (identifier + '.mp4'), folder / 'movie.nfo', folder / 'poster.png'
        row['beforeHashes'] = {'video': digest(video), 'nfo': digest(nfo), 'poster': digest(poster)}
        before_files = inventory(self.media)
        start = self.call('workspace.importInputs', {'paths': [str(video)], 'recursive': False})
        imported = self.wait(start['operationId'])
        require(imported['status'] == 'completed', 'Import operation failed')
        result = imported['result']
        require(len(result['importedJobIds']) == 1 and not result['warnings'], 'Import was not a clean single job')
        job = self.job(result['importedJobIds'][0])
        require(job['metadata']['id'] == identifier and not job['localNfoSaveBlocked'], 'Local ID/NFO baseline mismatch')
        row['jobId'] = job['jobId']
        initial_revision = job['reviewRevision']
        local_actor_source = next(c['source'] for c in job['candidates'] if c['field'] == 'actorsText' and c['selected'])
        local_rating_source = next(c['source'] for c in job['candidates'] if c['field'] == 'rating' and c['selected'])
        successful = []
        row['providerAttempts'] = []
        for provider in PROVIDERS:
            started = time.monotonic()
            attempt = {'source': provider}
            row['providerAttempts'].append(attempt)
            try:
                search = self.call('jobs.search', {'jobId': job['jobId'], 'sourceMode': provider})
                done = self.wait(search['operationId'])
                job = self.job(job['jobId'])
                result = done.get('result', {})
                rows = result.get('jobResults', [])
                entry = next((x for x in rows if x['jobId'] == job['jobId']), {})
                candidates = {c['field']: c['value'] for c in job['candidates'] if c['source'] == provider}
                attempt.update(operationStatus=done['status'], jobStatus=entry.get('status'),
                    warnings=[self.clean(x) for x in result.get('warnings', [])],
                    jobWarnings=[self.clean(x) for x in entry.get('warnings', [])],
                    errorReason=(entry.get('error') or done.get('error') or {}).get('data', {}).get('reason'),
                    snapshotSourceName=job['metadata'].get('sourceName'),
                    snapshotSourceUrl=job['metadata'].get('sourceUrl'),
                    sourceCandidatePresent=bool(candidates),
                    fieldPresence={field: bool(candidates.get(field, '').strip()) for field in FIELDS},
                    fieldValueSha256={field: value_hash(value) for field, value in candidates.items()},
                    candidateActorNameCount=len([x for x in candidates.get('actorsText', '').split(',') if x.strip()]),
                    candidateActorCountMethod='nonempty comma-separated names in provider actorsText',
                    snapshotActorCount=len(job['metadata'].get('actors', [])),
                    metadataSha256=value_hash(job['metadata']))
                attempt['success'] = done['status'] == 'completed' and entry.get('status') == 'Completed' and bool(candidates.get('title', '').strip())
                if attempt['success']:
                    successful.append(provider)
            except Exception as error:
                attempt.update(success=False, errorType=type(error).__name__, error=self.clean(error))
                job = self.job(job['jobId'])
            attempt['elapsedMs'] = round((time.monotonic() - started) * 1000, 2)
            print(f'{identifier} {provider}: {"success" if attempt["success"] else "failed"}; warnings={len(attempt.get("warnings", []))}', flush=True)
        require(inventory(self.media) == before_files, 'Import/search changed a media file')
        require(successful, 'No live provider returned a usable title')
        require(job['metadata']['id'] == identifier, 'Provider result changed the movie ID')
        require(job['reviewRevision'] > initial_revision, 'Search did not advance review revision')
        current = value_hash(job['metadata'])
        self.expect_error('jobs.applyEdits', {'jobId': job['jobId'], 'expectedReviewRevision': initial_revision,
                          'edits': {'plot': 'MUST NOT APPLY'}}, 'REVISION_CONFLICT')
        require(value_hash(self.job(job['jobId'])['metadata']) == current, 'Rejected stale edit changed metadata')
        selected = {}
        for field in SELECT_FIELDS:
            candidate = next((c for source in successful for c in job['candidates']
                              if c['field'] == field and c['source'] == source and c['value'].strip()), None)
            if candidate:
                job = self.select(job, field, candidate['source'])
                require(job['metadata'][field] == candidate['value'], 'Selected provider field did not apply: ' + field)
                selected[field] = {'source': candidate['source'], 'valueSha256': value_hash(candidate['value'])}
        require('title' in selected, 'No provider title was selected')
        # Preserve the deliberately synthetic actor extension and raw rating while
        # writing the explicitly selected live title/date/runtime/maker fields.
        job = self.select(job, 'actorsText', local_actor_source)
        job = self.select(job, 'rating', local_rating_source)
        job = self.edit(job, {'plot': 'Synthetic live metadata QA annotation for ' + identifier})
        blocked = self.preview(job, False)
        require(not blocked['canExecute'], 'Existing NFO unexpectedly allowed without overwrite consent')
        self.expect_error('save.execute', {'planId': blocked['planId']}, 'PATH_CONFLICT')
        require(inventory(self.media) == before_files, 'Blocked preview/execute wrote files')
        old = self.preview(self.job(job['jobId']), True)
        require(old['canExecute'], 'Consented preview was blocked')
        job = self.job(job['jobId'])
        marker = 'Synthetic live metadata QA annotation, revised before save: ' + identifier
        job = self.edit(job, {'plot': marker})
        self.expect_error('save.execute', {'planId': old['planId']}, 'PLAN_STALE')
        require(inventory(self.media) == before_files, 'Stale preview execution wrote files')
        fresh = self.preview(job, True)
        require(fresh['canExecute'], 'Fresh reviewed preview was blocked')
        changes = fresh['changes']
        writes = [c for c in changes if c['operation'] != 'KeepFile']
        require(len(writes) == 1 and Path(writes[0]['target']).resolve() == nfo.resolve(), 'Preview would write outside the intended NFO')
        require(writes[0]['operation'] in ('UpdateFile', 'CreateFile'), 'Unexpected NFO operation')
        require(any(c['operation'] == 'KeepFile' and Path(c['target']).resolve() == video.resolve() for c in changes), 'Preview did not retain original video')
        selected_values = {field: job['metadata'][field] for field in selected}
        execute = self.call('save.execute', {'planId': fresh['planId']})
        done = self.wait(execute['operationId'])
        saved = done.get('result', {})
        require(done['status'] == 'completed' and saved.get('outcome') == 'completed', 'Save report was not completed')
        require(saved.get('savedJobIds') == [job['jobId']] and not saved.get('cleanupWarnings'), 'Save had incomplete rows/cleanup warnings')
        require(len(saved['jobResults']) == 1 and saved['jobResults'][0]['status'] == 'Completed', 'Save row failed')
        after_save = inventory(self.media)
        repeated = self.call('save.execute', {'planId': fresh['planId']})
        require(repeated['operationId'] == execute['operationId'], 'Duplicate execute started another operation')
        require(inventory(self.media) == after_save, 'Duplicate execute changed files')
        changed = [path for path in before_files.keys() | after_save.keys() if before_files.get(path) != after_save.get(path)]
        require(changed == [str(nfo.relative_to(self.media))], 'Save changed files other than this NFO')
        xml = ET.parse(nfo).getroot()
        for field, value in selected_values.items():
            require(xml.findtext(XML_FIELDS[field], '') == value, 'Saved NFO differs from selected provider field: ' + field)
        require(xml.findtext('plot') == marker, 'Manual plot annotation was not saved')
        require(xml.attrib.get('qaRoot') == 'KEEP LIVE QA ROOT', 'Root extension lost')
        require(xml.findtext('qaUnknown/nested') == 'KEEP UNKNOWN XML', 'Unknown XML lost')
        require(xml.findtext('resume/position') == '1.25' and xml.findtext('resume/total') == '2', 'Resume values lost')
        actor = xml.find('actor')
        require(actor is not None and actor.attrib.get('qaActor') == 'KEEP ACTOR ATTRIBUTE'
                and actor.findtext('qaExtension') == 'KEEP ACTOR EXTENSION', 'Synthetic actor extension lost')
        require(xml.findtext('rating') == 'N/A', 'Raw local rating lost')
        require(digest(video) == row['beforeHashes']['video'] and digest(poster) == row['beforeHashes']['poster'], 'Original video/poster changed')
        reloaded = self.job(job['jobId'])
        require(reloaded['metadata']['title'] == selected_values['title'], 'Post-save reload lost selected title')
        row.update(outcome='fully' if len(successful) == len(PROVIDERS) else 'partial',
            selectedProviderFields=selected, preservedActorSource=local_actor_source,
            afterHashes={'video': digest(video), 'poster': digest(poster), 'nfo': digest(nfo)},
            postSaveMetadataSha256=value_hash(reloaded['metadata']),
            safety={'searchDidNotWrite': True, 'staleRevisionRejected': True, 'overwriteBlocked': True,
                    'oldPlanRejected': True, 'freshPlanOnlyWroteNfo': True, 'duplicateExecuteSameOperation': True,
                    'unknownXmlPreserved': True, 'resumePreserved': True, 'actorExtensionPreserved': True,
                    'rawRatingPreserved': True, 'videoShaUnchanged': True, 'posterShaUnchanged': True},
            saveOperationId=execute['operationId'], saveOutcome=saved['outcome'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run-live', action='store_true', help='Explicitly authorize requests to LibreDMM and R18.dev.')
    parser.add_argument('--worker', type=Path, required=True)
    parser.add_argument('--video-template', type=Path, required=True, help='User-provided playable synthetic clip, copied without modification.')
    parser.add_argument('--poster-template', type=Path, default=ROOT / 'fixtures/macos/synthetic-cover-5x2.png')
    parser.add_argument('--workspace', type=Path, required=True, help='Must not already exist; raw provider text/NFO remains here.')
    parser.add_argument('--report', type=Path, required=True, help='New JSON report; never contains provider title/plot text.')
    args = parser.parse_args()
    if not args.run_live:
        parser.error('No network or workspace writes without explicit --run-live.')
    for label in ('worker', 'video_template', 'poster_template'):
        path = getattr(args, label).expanduser().resolve(strict=True)
        require(path.is_file(), label + ' must be a regular file')
        setattr(args, label, path)
    require(os.access(args.worker, os.X_OK), 'Worker is not executable')
    args.workspace = args.workspace.expanduser().absolute()
    args.report = args.report.expanduser().absolute()
    require(not args.workspace.exists() and not args.workspace.is_symlink(), 'Workspace already exists; refusing to overwrite')
    require(not args.report.exists() and not args.report.is_symlink(), 'Report already exists; choose a new report')
    report = {'schemaVersion': 1, 'startedAt': now(), 'runLive': True, 'outcome': 'fail',
              'movieIds': list(IDS), 'workspace': str(args.workspace),
              'worker': {'path': str(args.worker), 'executableSha256': digest(args.worker),
                         'managedAssemblySha256': digest(args.worker.parent / (args.worker.name + '.dll'))},
              'templates': {'videoSha256': digest(args.video_template), 'posterSha256': digest(args.poster_template)},
              'scope': 'Real provider metadata requests; user-supplied synthetic video/local poster; NFO-only save. No remote artwork/video downloads, live browser or UI acceptance.',
              'providerOutcomePolicy': 'fully requires both provider rows completed with usable title candidates; partial retains source failures and is not an all-pass result.',
              'results': []}
    worker = None
    run = None
    try:
        args.workspace.mkdir(parents=False, exist_ok=False)
        media = args.workspace / 'synthetic-media'; media.mkdir()
        for identifier in IDS:
            folder = media / identifier; folder.mkdir()
            shutil.copyfile(args.video_template, folder / (identifier + '.mp4'))
            shutil.copyfile(args.poster_template, folder / 'poster.png')
            (folder / 'movie.nfo').write_bytes(initial_nfo(identifier))
        harness = load_harness(args.worker)
        worker = harness.Worker(args.workspace)
        report['worker']['reportedVersion'] = worker.initialized['workerVersion']
        run = LiveRun(worker, media)
        for identifier in IDS:
            row = {'movieId': identifier, 'outcome': 'fail'}; report['results'].append(row)
            try:
                run.check_movie(identifier, row)
            except Exception as error:
                row.update(errorType=type(error).__name__, error=run.clean(error))
            save_report(args.report, report)
            print(f'{identifier}: {row["outcome"]}', flush=True)
        snapshot = run.call('workspace.getSnapshot')
        require(not snapshot['recoveryWarnings'], 'Recovery warnings remain')
        outcomes = [row['outcome'] for row in report['results']]
        report['outcome'] = 'fully' if all(x == 'fully' for x in outcomes) else 'fail' if any(x == 'fail' for x in outcomes) else 'partial'
    except Exception as error:
        report['fatalError'] = {'type': type(error).__name__, 'message': run.clean(error) if run else str(error)[:500]}
    finally:
        if worker is not None:
            try:
                if run:
                    for operation_id in run.active:
                        worker.request('operations.cancel', {'operationId': operation_id})
                worker.close()
                report['worker']['exitCode'] = worker.proc.returncode
                report['worker']['normalShutdown'] = worker.proc.returncode == 0
                if worker.proc.returncode != 0:
                    report['outcome'] = 'fail'
            except Exception as error:
                report['worker']['shutdownError'] = type(error).__name__
                report['outcome'] = 'fail'
        report['finishedAt'] = now()
        save_report(args.report, report)
    print(f'LIVE CHECK: {report["outcome"]}; report={args.report}', flush=True)
    return 0 if report['outcome'] == 'fully' else 1


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (CheckFailure, OSError) as error:
        print('LIVE CHECK REFUSED: ' + str(error), file=sys.stderr)
        sys.exit(2)
