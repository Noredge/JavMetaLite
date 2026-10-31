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


def evaluate(app, manifest=None, distribution_policy='github-adhoc', checker=check):
    if distribution_policy not in ('github-adhoc', 'signed-only', 'notarized'):
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
    ready = {'github-adhoc': local_signature_verified,
             'signed-only': signature_ready,
             'notarized': notarized_ready}[distribution_policy]
    reasons = []
    if not local: reasons.append('Local package verification failed.')
    if signature['exitCode'] != 0: reasons.append('Signature details could not be established.')
    if mode not in ('adhoc', 'developer-id'):
        reasons.append('A supported local signature could not be established.')
    if distribution_policy != 'github-adhoc' and mode != 'developer-id':
        reasons.append('Artifact is not Developer ID Application signed.')
    if distribution_policy == 'notarized':
        if not notarization_verified: reasons.append('No valid stapled notarization ticket was established.')
        if not gatekeeper_accepted: reasons.append('Gatekeeper assessment did not pass.')
    return {'schemaVersion': 1, 'app': str(app), 'localPackageVerified': local,
              'localSignatureVerified': local_signature_verified, 'signMode': mode,
              'distributionPolicy': distribution_policy, 'signatureReady': signature_ready,
              'notarizationVerified': notarization_verified, 'gatekeeperAccepted': gatekeeper_accepted,
              'notarizedDistributionReady': notarized_ready,
              'distributionReady': ready, 'blockers': reasons, 'validCodeSigningIdentityCount': identity_count,
              'manualAcceptanceVerified': False,
              'notarizationCredentialsVerified': False, 'package': package, 'signature': signature,
              'stapledTicket': ticket, 'gatekeeper': gatekeeper, 'tools': tools,
              'policyScope': 'distributionReady means only that the selected automated distribution policy passed. github-adhoc permits GitHub stable or preview distribution with verified package integrity and a verified local ad-hoc or Developer ID signature; this project policy does not establish Apple identity verification, malware scanning or automatic launch acceptance. signatureReady retains its Developer ID requirement. signed-only requires signatureReady; notarized additionally requires a valid stapled ticket and Gatekeeper acceptance. Notarization verification here means stapled-ticket validation, not an Apple account or submission query.',
              'scope': 'Read-only automated policy checks on this Mac. manualAcceptanceVerified is always false because this tool does not verify fresh browser download, per-app Open Anyway, native workflows, physical IME/VoiceOver or another platform acceptance. Record those results separately.'}


def main(argv=None, checker=check):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('app', type=Path)
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--report', type=Path)
    parser.add_argument('--policy', choices=('github-adhoc', 'signed-only', 'notarized'), default='github-adhoc',
                        help='Automated distribution requirements: github-adhoc (default) requires local package/signature verification; signed-only requires Developer ID; notarized also requires a stapled ticket and Gatekeeper acceptance. Manual acceptance is separate.')
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
