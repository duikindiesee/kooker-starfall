#!/usr/bin/env python3
"""Create a secret-free post-run ledger/restart/scope receipt for normal play."""
import argparse, hashlib, http.client, json, re, sys, tempfile, threading, unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'services' / 'starfall-memory'))
from core import MemoryStore
from server import Server, configuration


def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()


def new_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, indent=2, sort_keys=True) + '\n')


def scoped_get(server, path, token):
    client = http.client.HTTPConnection('127.0.0.1', server.server_port, timeout=3)
    try:
        client.request('GET', path, headers={'Authorization': 'Bearer ' + token})
        response = client.getresponse(); response.read(16385)
        return response.status
    finally: client.close()


def scope_probe(store, cfg, actor):
    server = Server(('127.0.0.1', 0), store, cfg)
    thread = threading.Thread(target=server.serve_forever, kwargs={'poll_interval': .01}, daemon=True); thread.start()
    token = next(row['token'] for row in cfg['inhabitants'] if row['inhabitant_id'] == actor)
    try:
        return {'foreign_world_query_status': scoped_get(server, '/v1/memories?world_id=foreign', token),
            'foreign_actor_query_status': scoped_get(server, '/v1/memories?inhabitant_id=foreign', token),
            'unknown_capability_status': scoped_get(server, '/v1/memories', 'not-a-valid-capability')}
    finally:
        server.shutdown(); thread.join(5); server.server_close()


def model_receipt(path):
    if path is None:
        return {'status': 'unavailable', 'reason': 'normal runtime retained no raw request/response, finish_reason, or inventory receipt'}
    value = json.loads(path.read_text(encoding='utf-8-sig'))
    allowed = {'model', 'finish_reason', 'request_sha256', 'response_sha256', 'inventory_sha256', 'device', 'provider'}
    if set(value) - allowed or not all(isinstance(value.get(key), str) for key in ('model', 'finish_reason', 'request_sha256', 'response_sha256')):
        raise RuntimeError('model provenance receipt is not sanitized or complete')
    for key in ('request_sha256', 'response_sha256', 'inventory_sha256'):
        if key in value and not re.fullmatch('[0-9a-fA-F]{64}', value[key]): raise RuntimeError('invalid model provenance hash')
    return {'status': 'provided', 'receipt_sha256': digest(path), **value}


def validate_receipts(build, process, run, world_id, inhabitant_id):
    if build.get('status') != 'Succeeded' or not re.fullmatch('[0-9a-f]{40}', build.get('sourceCommit', '')):
        raise RuntimeError('build/source provenance mismatch')
    build_id = build.get('buildId')
    if not build_id or process.get('build') != build_id or run.get('build') != build_id:
        raise RuntimeError('process/run/build provenance mismatch')
    if not process.get('normalFlagsVerified') or process.get('flags', {}).get('integratedSmoke') or process.get('flags', {}).get('npcSmoke'):
        raise RuntimeError('live process receipt does not prove normal flags')
    if not run.get('normalPlay') or not run.get('persisted') or run.get('world') != world_id or run.get('inhabitant') != inhabitant_id:
        raise RuntimeError('run evidence scope mismatch')


def compare_restart(prior, checkpoint, events, scope):
    if prior is None:
        return None
    prior_scope = prior.get('scope', {})
    if any(prior_scope.get(key) != scope[key] for key in ('world_id', 'inhabitant_id', 'store_pseudonym')):
        raise RuntimeError('previous snapshot scope/store mismatch')
    prior_ledger = prior.get('ledger', {})
    prior_checkpoint, prior_events = prior_ledger.get('checkpoint', {}), prior_ledger.get('events')
    if not isinstance(prior_events, list) or not isinstance(prior_checkpoint.get('events'), int):
        raise RuntimeError('previous snapshot ledger is incomplete')
    return checkpoint['events'] >= prior_checkpoint['events'] and events[:len(prior_events)] == prior_events


class FailClosedTests(unittest.TestCase):
    def fixtures(self):
        build = {'status': 'Succeeded', 'sourceCommit': 'a' * 40, 'buildId': 'b1'}
        process = {'build': 'b1', 'normalFlagsVerified': True, 'flags': {}}
        run = {'build': 'b1', 'normalPlay': True, 'persisted': True, 'world': 'w', 'inhabitant': 'i'}
        return build, process, run

    def test_rejects_run_from_other_build(self):
        build, process, run = self.fixtures(); run['build'] = 'old'
        with self.assertRaisesRegex(RuntimeError, 'process/run/build'): validate_receipts(build, process, run, 'w', 'i')

    def test_rejects_prior_from_other_store_even_when_empty(self):
        prior = {'scope': {'world_id': 'w', 'inhabitant_id': 'i', 'store_pseudonym': 'other'},
            'ledger': {'checkpoint': {'events': 0}, 'events': []}}
        with self.assertRaisesRegex(RuntimeError, 'scope/store'): compare_restart(prior, {'events': 0}, [],
            {'world_id': 'w', 'inhabitant_id': 'i', 'store_pseudonym': 'current'})

    def test_missing_database_check_does_not_create_file(self):
        with tempfile.TemporaryDirectory() as folder:
            database = Path(folder) / 'missing.sqlite3'
            self.assertFalse(database.is_file())
            with self.assertRaisesRegex(RuntimeError, 'database does not exist'): require_database(database)
            self.assertFalse(database.exists())


def require_database(database):
    if not database.is_file():
        raise RuntimeError('memory database does not exist; refusing to create audit input')


def main():
    if '--self-test' in sys.argv[1:]:
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(FailClosedTests)
        result = unittest.TextTestRunner(verbosity=2).run(suite)
        raise SystemExit(0 if result.wasSuccessful() else 1)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--storage', type=Path, required=True)
    parser.add_argument('--world-id', required=True)
    parser.add_argument('--inhabitant-id', required=True)
    parser.add_argument('--build-manifest', type=Path, required=True)
    parser.add_argument('--process-receipt', type=Path, required=True)
    parser.add_argument('--run-evidence', type=Path, required=True)
    parser.add_argument('--previous-snapshot', type=Path)
    parser.add_argument('--model-provenance', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    storage, build_path, process_path, run_path, output = (x.resolve() for x in
        (args.storage, args.build_manifest, args.process_receipt, args.run_evidence, args.output))
    if output.exists() or not all(x.is_file() for x in (build_path, process_path, run_path)):
        raise RuntimeError('require existing receipts and a new output path')
    cfg_path = storage / 'private' / 'config.json'; database = storage / 'private' / 'data' / 'starfall-memory.sqlite3'
    require_database(database)
    cfg = configuration(cfg_path)
    if cfg['world_id'] != args.world_id or not any(row['inhabitant_id'] == args.inhabitant_id for row in cfg['inhabitants']):
        raise RuntimeError('private store scope mismatch')
    build = json.loads(build_path.read_text(encoding='utf-8-sig')); process = json.loads(process_path.read_text(encoding='utf-8-sig'))
    run = json.loads(run_path.read_text(encoding='utf-8-sig'))
    validate_receipts(build, process, run, args.world_id, args.inhabitant_id)
    store = MemoryStore(database, cfg['world_id'], cfg['publisher_id'], cfg['build_ids'])
    try:
        checkpoint = store.verify(); rows = store.db.execute('SELECT id,event_id,session,sequence,actor,tick,kind,previous_hash,chain_hash FROM events ORDER BY id').fetchall()
        events = [dict(id=row['id'], event_id=row['event_id'], session_pseudonym=hashlib.sha256(row['session'].encode()).hexdigest()[:16],
            sequence=row['sequence'], actor=row['actor'], tick=row['tick'], kind=row['kind'], previous_hash=row['previous_hash'], chain_hash=row['chain_hash']) for row in rows]
        rejected = scope_probe(store, cfg, args.inhabitant_id)
    finally: store.close()
    scope = {'world_id': args.world_id, 'inhabitant_id': args.inhabitant_id,
        'store_pseudonym': hashlib.sha256(str(storage).encode()).hexdigest()[:16]}
    prior = json.loads(args.previous_snapshot.resolve().read_text(encoding='utf-8-sig')) if args.previous_snapshot else None
    continues = compare_restart(prior, checkpoint, events, scope)
    model = model_receipt(args.model_provenance.resolve() if args.model_provenance else None)
    ledger_ok = continues is not False and rejected == {'foreign_world_query_status': 400,
        'foreign_actor_query_status': 400, 'unknown_capability_status': 401}
    result = {'schema': 'starfall.normal-memory.audit.v1',
        'ledger_scope_status': 'PASS' if ledger_ok else 'FAIL',
        'model_provenance_status': 'RECORDED' if model['status'] == 'provided' else 'UNAVAILABLE',
        'provenance': {'build_id': build['buildId'], 'source_commit': build['sourceCommit'], 'executable_sha256': process['executableSha256'],
            'build_manifest_sha256': digest(build_path), 'process_receipt_sha256': digest(process_path), 'run_evidence_sha256': digest(run_path)},
        'scope': scope,
        'ledger': {'checkpoint': checkpoint, 'database_sha256': digest(database), 'events': events},
        'restart': {'previous_snapshot': prior is not None, 'prior_chain_is_exact_prefix': continues},
        'foreign_scope': rejected, 'model_provenance': model,
        'secrets_retained': False}
    new_json(output, result); print(json.dumps({'ledger_scope_status': result['ledger_scope_status'],
        'model_provenance_status': result['model_provenance_status'], 'output': str(output)}))


if __name__ == '__main__': main()
