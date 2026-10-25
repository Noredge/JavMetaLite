#!/usr/bin/env python3
"""Read-only local/distribution checks; never disables Gatekeeper or changes quarantine."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import sys

HERE = Path(__file__).resolve().parent


def check(arguments):
    try:
        result = subprocess.run([str(x) for x in arguments], capture_output=True, text=True, timeout=60)
        return {'exitCode': result.returncode, 'stdout': result.stdout[-8000:], 'stderr': result.stderr[-8000:]}
    except (OSError, subprocess.TimeoutExpired) as error:
        return {'exitCode': -1, 'stdout': '', 'stderr': str(error)}


def evaluate(app, manifest=None, distribution_policy='signed-only', checker=check):
    if distribution_policy not in ('signed-only', 'notarized'):
        raise ValueError('Unknown distribution policy: ' + distribution_policy)
    package_command = [HERE / 'verify-package.sh', app]
    if manifest: package_command += ['--manifest', manifest]
    signature = checker(['/usr/bin/codesign', '-d', '--verbose=4', app])
    text = signature['stdout'] + signature['stderr']
    mode = 'developer-id' if 'Authority=Developer ID Application:' in text else ('adhoc' if 'Signature=adhoc' in text else 'unknown')
    identities = checker(['/usr/bin/security', 'find-identity', '-v', '-p', 'codesigning'])
    count_match = re.search(r'(\d+) valid identities found', identities['stdout'])
    identity_count = int(count_match[1]) if count_match else None
    package = checker(package_command)
    ticket = checker(['/usr/bin/xcrun', 'stapler', 'validate', app])
    gatekeeper = checker(['/usr/sbin/spctl', '--assess', '--type', 'execute', '--verbose=4', app])
    tools = {tool: checker(['/usr/bin/xcrun', '--find', tool]) for tool in ('notarytool', 'stapler')}
    local = package['exitCode'] == 0
    local_signature_verified = local and signature['exitCode'] == 0 and mode in ('adhoc', 'developer-id')
    signature_ready = local_signature_verified and mode == 'developer-id'
    notarization_verified = ticket['exitCode'] == 0
    gatekeeper_accepted = gatekeeper['exitCode'] == 0
    notarized_ready = signature_ready and notarization_verified and gatekeeper_accepted
    ready = signature_ready if distribution_policy == 'signed-only' else notarized_ready
    reasons = []
    if not local: reasons.append('Local package verification failed.')
    if signature['exitCode'] != 0: reasons.append('Signature details could not be established.')
    if mode != 'developer-id': reasons.append('Artifact is not Developer ID Application signed.')
    if distribution_policy == 'notarized':
        if not notarization_verified: reasons.append('No valid stapled notarization ticket was established.')
        if not gatekeeper_accepted: reasons.append('Gatekeeper assessment did not pass.')
    return {'schemaVersion': 1, 'app': str(app), 'localPackageVerified': local,
              'localSignatureVerified': local_signature_verified, 'signMode': mode,
              'distributionPolicy': distribution_policy, 'signatureReady': signature_ready,
              'notarizationVerified': notarization_verified, 'gatekeeperAccepted': gatekeeper_accepted,
              'notarizedDistributionReady': notarized_ready,
              'distributionReady': ready, 'blockers': reasons, 'validCodeSigningIdentityCount': identity_count,
              'notarizationCredentialsVerified': False, 'package': package, 'signature': signature,
              'stapledTicket': ticket, 'gatekeeper': gatekeeper, 'tools': tools,
              'policyScope': 'localSignatureVerified includes verified local ad-hoc signing. signatureReady and signed-only distribution readiness require a verified Developer ID package; they do not imply notarization or Gatekeeper acceptance. notarized additionally requires both checks to pass. Notarization verification here means stapled-ticket validation, not an Apple account or submission query.',
              'scope': 'Read-only automated policy checks on this Mac; not a fresh browser download, physical IME/VoiceOver or another platform acceptance.'}


def main(argv=None, checker=check):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('app', type=Path)
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--report', type=Path)
    parser.add_argument('--policy', choices=('signed-only', 'notarized'), default='signed-only',
                        help='Distribution requirements: signed-only (default) requires Developer ID; notarized also requires a stapled ticket and Gatekeeper acceptance.')
    parser.add_argument('--require-distribution', action='store_true',
                        help='Return nonzero unless the selected distribution policy passes. Without this flag, success only means local package verification passed.')
    args = parser.parse_args(argv)
    app = args.app.resolve(strict=True)
    if args.report and args.report.resolve().is_relative_to(app):
        parser.error('Report must be outside the app; a signed bundle is never modified by these checks.')
    report = evaluate(app, args.manifest, args.policy, checker)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))
    return 0 if report['distributionReady' if args.require_distribution else 'localPackageVerified'] else 1


if __name__ == '__main__':
    sys.exit(main())
