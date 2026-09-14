"""Replay an exact, verified Unity fixture into a separate offline local API process."""
import argparse
import concurrent.futures
import csv
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import sys

from core import canonical, parse_json

HERE = Path(__file__).resolve().parent


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def private_directory(path):
    path.mkdir(mode=0o700)
    if os.name == 'nt':
        identity = subprocess.check_output(['whoami', '/user', '/fo', 'csv', '/nh'], text=True)
        sid = next(csv.reader(identity.strip().splitlines()))[1]
        if not re.fullmatch(r'S-1-\d+(?:-\d+)+', sid):
            raise RuntimeError('Could not resolve current user SID')
        subprocess.run(['icacls', str(path), '/inheritance:r'], check=True, stdout=subprocess.DEVNULL)
        subprocess.run(['icacls', str(path), '/remove:g', '*S-1-3-4', '*S-1-5-32-544'], check=True, stdout=subprocess.DEVNULL)
        subprocess.run(['icacls', str(path), '/grant:r', f'*{sid}:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F'], check=True, stdout=subprocess.DEVNULL)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--unity-export', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    export = args.unity_export.resolve()
    output = args.output.resolve()
    if output.exists():
        raise RuntimeError('Use a new output directory; earlier evidence is immutable')
    output.mkdir(parents=True)
    checks = []
    def need(value, name):
        if not value:
            raise AssertionError(name)
        checks.append({'name': name, 'passed': True})
    validation = parse_json((export/'unity-memory-validation.json').read_bytes())
    need(validation['status'] == 'PASS' and len(validation['checks']) == 12, 'actual-unity-validation-passed')
    need(digest(export/'unity-events.jsonl') == validation['exportSha256'], 'unity-export-sha256-matches-actual-proof')
    events = [parse_json(line) for line in (export/'unity-events.jsonl').read_bytes().splitlines()]
    need(len(events) == 8 and all(e['source']['build_id'] == validation['sourceCommit'] for e in events), 'events-pin-the-verified-unity-source')
    cfg = {'schema': 'starfall.memory.config.v1', 'world_id': events[0]['world_id'], 'publisher_id': events[0]['source']['producer_id'],
        'publisher_token': secrets.token_urlsafe(32), 'build_ids': [validation['sourceCommit']],
        'inhabitants': [{'inhabitant_id': actor, 'token': secrets.token_urlsafe(32)} for actor in ('inhabitant-01', 'inhabitant-02')]}
    private = output/'private-runtime'
    private_directory(private)
    config_path = private/'config.json'
    config_path.write_text(canonical(cfg), encoding='utf-8')
    (private/'tmp').mkdir()
    process = None
    stderr = None
    child_env = {key: os.environ[key] for key in ('SYSTEMROOT', 'WINDIR') if key in os.environ}
    child_env.update({'TEMP': str(private/'tmp'), 'TMP': str(private/'tmp')})
    def start():
        nonlocal process, stderr
        stderr = (private/'service-stderr.log').open('ab')
        process = subprocess.Popen([sys.executable, '-s', '-E', str(HERE/'offline_guard.py'), '--config', str(config_path),
            '--data-dir', str(private/'data'), '--port', '0'], cwd=HERE, env=child_env, stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=stderr, text=True,
            creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
        with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
            first = pool.submit(process.stdout.readline)
            try:
                guard = parse_json(first.result(timeout=15))
                ready = parse_json(pool.submit(process.stdout.readline).result(timeout=15))
            except BaseException:
                process.terminate()
                process.wait(timeout=10)
                raise
        need(guard['offline_guard'] == 'outbound-connect-and-dns-denied' and ready['status'] == 'ready', 'isolated-offline-process-ready')
        return ready['port']
    def stop():
        nonlocal process, stderr
        if process:
            process.terminate()
            process.wait(timeout=10)
            process.stdout.close()
            process = None
        if stderr:
            stderr.close()
            stderr = None
    def call(method, route, data=None, role='inhabitant-01', headers=None):
        token = cfg['publisher_token'] if role == 'publisher' else next((entry['token'] for entry in cfg['inhabitants'] if entry['inhabitant_id'] == role), None)
        request_headers = {'Content-Type': 'application/json'}
        if token:
            request_headers['Authorization'] = 'Bearer ' + token
        request_headers.update(headers or {})
        client = http.client.HTTPConnection('127.0.0.1', port, timeout=5)
        try:
            client.request(method, route, canonical(data).encode() if data is not None else None, request_headers)
            response = client.getresponse()
            return response.status, parse_json(response.read())
        finally:
            client.close()
    def post(event):
        status, result = call('POST', '/v1/events', event, role='publisher')
        need(status == 200 and result['inserted'], f"actual-unity-event-{event['source']['sequence']}-ingested")
        need(result['event_id'] == hashlib.sha256(canonical(event).encode()).hexdigest(), f"event-{event['source']['sequence']}-content-address-verified")
        return result
    def cli(command, *extra):
        result = subprocess.run([sys.executable, '-s', '-E', str(HERE/'local.py'), command, '--config', str(config_path),
            '--port', str(port), *extra], env=child_env, cwd=HERE, stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=15,
            creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
        need(result.returncode == 0, 'local-cli-' + command + '-completed')
        return parse_json(result.stdout)
    try:
        port = start()
        need(call('GET', '/v1/health', role='none')[0] == 401, 'unauthenticated-access-rejected')
        need(call('POST', '/v1/events', events[0])[0] == 403, 'inhabitant-cannot-publish-events')
        receipts = [post(e) for e in events[:4]]
        need(call('POST', '/v1/events', events[3], role='publisher')[1]['inserted'] is False, 'transport-retry-is-idempotent')
        dream_request = {'schema': 'starfall.dream.request.v1'}
        need(call('POST', '/v1/dreams', dream_request)[0] == 409, 'awake-dream-rejected')
        receipts.extend(post(e) for e in events[4:6])
        before = call('GET', '/v1/health')[1]
        facts_before = call('GET', '/v1/facts')[1]
        status, dream = call('POST', '/v1/dreams', dream_request)
        need(status == 200 and dream['associations'][0]['epistemic_status'] == 'theory', 'sleep-produces-evidence-linked-tentative-association')
        need(call('POST', '/v1/dreams', dream_request)[1] == dream, 'same-sleep-dream-is-idempotent')
        status, other_dream = call('POST', '/v1/dreams', dream_request, role='inhabitant-02')
        need(status == 200 and other_dream['associations'] == [], 'other-inhabitant-dream-does-not-borrow-delivery')
        belief = {'schema': 'starfall.belief.v1', 'request_id': 'first-idea', 'label': 'theory',
            'text': 'The west depot might be a useful place to visit again.', 'evidence_ids': [receipts[3]['event_id']]}
        need(call('POST', '/v1/beliefs', belief)[0] == 200, 'cited-private-belief-recorded')
        need(call('POST', '/v1/beliefs', belief, role='inhabitant-02')[0] == 403, 'cross-inhabitant-evidence-rejected')
        need(call('POST', '/v1/beliefs', {**belief, 'label': 'confirmed'})[0] == 400, 'belief-cannot-promote-itself-to-fact')
        need(call('DELETE', '/v1/events', {})[0] != 200 and call('POST', '/v1/permissions', {})[0] != 200, 'no-event-edit-or-permission-route')
        need(call('GET', '/v1/health')[1] == before and call('GET', '/v1/facts')[1] == facts_before, 'dreams-and-beliefs-preserve-ledger-and-confirmed-facts')
        wiki = call('GET', '/v1/wiki')[1]
        other_wiki = call('GET', '/v1/wiki', role='inhabitant-02')[1]
        depot_page = next(page for page in wiki['pages'] if page['subject_id'] == 'depot-west')
        need(depot_page['confirmed_facts'][0]['evidence_ids'] == [receipts[3]['event_id']]
            and depot_page['dream_theories'][0]['epistemic_status'] == 'theory', 'subject-wiki-page-separates-confirmed-delivery-from-dream-theory')
        need(len(wiki['inhabitant_beliefs']['items']) == 1 and other_wiki['inhabitant_beliefs']['items'] == [], 'wiki-separates-shared-facts-from-private-beliefs')
        need(call('GET', '/v1/memories?inhabitant_id=inhabitant-02')[0] == 400, 'query-cannot-switch-memory-namespace')
        need(call('GET', '/v1/health', headers={'Origin': 'https://example.test'})[0] == 403, 'browser-origin-rejected')
        receipts.append(post(events[6]))
        need(call('POST', '/v1/dreams', dream_request)[0] == 409, 'verified-wake-closes-dream-window')
        receipts.append(post(events[7]))
        status, second_dream = call('POST', '/v1/dreams', dream_request)
        need(status == 200 and second_dream['sleep_event_id'] != dream['sleep_event_id'], 'new-sleep-creates-separate-derived-record')
        memories = call('GET', '/v1/memories')[1]
        other_memories = call('GET', '/v1/memories', role='inhabitant-02')[1]
        need(all(x['inhabitant_id'] == 'inhabitant-01' for x in memories['items'])
            and all(x['inhabitant_id'] == 'inhabitant-02' for x in other_memories['items']), 'episodic-memories-stay-in-identity-namespace')
        checkpoint = call('GET', '/v1/health')[1]
        stop()
        port = start()
        need(call('GET', '/v1/health')[1] == checkpoint, 'process-restart-preserves-verified-ledger')
        need(call('POST', '/v1/dreams', dream_request)[1] == second_dream, 'process-restart-preserves-derived-dream')
        need(call('GET', '/v1/memories')[1] == memories, 'process-restart-preserves-episodic-memory')
        need(cli('read', '--inhabitant', 'inhabitant-01', '--resource', 'memories') == memories, 'local-cli-reads-own-persisted-memory')
        replay = cli('publish', '--events', str(export/'unity-events.jsonl'))
        need(replay == {'accepted': 8, 'inserted': 0, 'already_present': 8}, 'local-cli-replays-export-idempotently')
        need(cli('dream', '--inhabitant', 'inhabitant-01') == second_dream, 'local-cli-uses-verified-sleep-window')
        artifacts = {'wiki.json': call('GET', '/v1/wiki')[1], 'dream.json': second_dream,
            'memories.json': memories, 'other-inhabitant-memories.json': other_memories, 'ledger-checkpoint.json': checkpoint}
        stop()
        config_path.unlink()
        need(not config_path.exists(), 'ephemeral-capability-file-removed-after-proof')
        for name, body in artifacts.items():
            (output/name).write_text(json.dumps(body, indent=2) + '\n', encoding='utf-8', newline='\n')
        for name in ('unity-events.jsonl', 'unity-memory-validation.json'):
            shutil.copyfile(export/name, output/name)
        report = {'schema': 'starfall.memory.integration-proof.v1', 'status': 'PASS', 'checks': checks,
            'unitySourceCommit': validation['sourceCommit'], 'unityExportSha256': validation['exportSha256'],
            'serviceSourceSha256': {name: digest(HERE/name) for name in ('core.py', 'server.py', 'offline_guard.py', 'prove_local.py', 'local.py')},
            'ledger': checkpoint, 'capabilityIsolation': 'New synthetic tokens; private runtime directory; filtered child environment; token file removed',
            'transport': 'Actual loopback HTTP process with outbound socket connections and DNS disabled by test guard',
            'limitations': ['Unity editor action fixture, not live player integration', 'Sleep/wake are explicit fixture transitions, not a finished game sleep controller',
                'Trusted publisher attests engine events; the service validates provenance and invariants but does not run Unity physics',
                'Offline rules produce dreams; no LLM or learning', 'Compose was not deployed; no personal archive, credential or volume was connected']}
        (output/'integration-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
        print(canonical({'status': 'PASS', 'checks': len(checks), 'events': checkpoint['events'], 'output': str(output)}))
    finally:
        stop()
        if config_path.exists():
            config_path.unlink()


if __name__ == '__main__':
    main()
