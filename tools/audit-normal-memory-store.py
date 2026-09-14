#!/usr/bin/env python3
"""Create a secret-free post-run ledger/restart/scope receipt for normal play."""
import argparse, hashlib, http.client, json, re, sys, threading
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


def main():
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
    cfg = configuration(cfg_path)
    if cfg['world_id'] != args.world_id or not any(row['inhabitant_id'] == args.inhabitant_id for row in cfg['inhabitants']):
        raise RuntimeError('private store scope mismatch')
    build = json.loads(build_path.read_text(encoding='utf-8-sig')); process = json.loads(process_path.read_text(encoding='utf-8-sig'))
    run = json.loads(run_path.read_text(encoding='utf-8-sig'))
    if build.get('status') != 'Succeeded' or not re.fullmatch('[0-9a-f]{40}', build.get('sourceCommit', '')) or process.get('build') != build.get('buildId'):
        raise RuntimeError('process/build/source provenance mismatch')
    if not process.get('normalFlagsVerified') or process.get('flags', {}).get('integratedSmoke') or process.get('flags', {}).get('npcSmoke'):
        raise RuntimeError('live process receipt does not prove normal flags')
    if not run.get('normalPlay') or not run.get('persisted') or run.get('world') != args.world_id or run.get('inhabitant') != args.inhabitant_id:
        raise RuntimeError('run evidence scope mismatch')
    store = MemoryStore(database, cfg['world_id'], cfg['publisher_id'], cfg['build_ids'])
    try:
        checkpoint = store.verify(); rows = store.db.execute('SELECT id,event_id,session,sequence,actor,tick,kind,previous_hash,chain_hash FROM events ORDER BY id').fetchall()
        events = [dict(id=row['id'], event_id=row['event_id'], session_pseudonym=hashlib.sha256(row['session'].encode()).hexdigest()[:16],
            sequence=row['sequence'], actor=row['actor'], tick=row['tick'], kind=row['kind'], previous_hash=row['previous_hash'], chain_hash=row['chain_hash']) for row in rows]
        rejected = scope_probe(store, cfg, args.inhabitant_id)
    finally: store.close()
    prior = json.loads(args.previous_snapshot.resolve().read_text(encoding='utf-8-sig')) if args.previous_snapshot else None
    continues = None if prior is None else checkpoint['events'] >= prior['ledger']['checkpoint']['events'] and events[:len(prior['ledger']['events'])] == prior['ledger']['events']
    result = {'schema': 'starfall.normal-memory.audit.v1', 'status': 'PASS' if continues is not False and rejected ==
        {'foreign_world_query_status': 400, 'foreign_actor_query_status': 400, 'unknown_capability_status': 401} else 'FAIL',
        'provenance': {'build_id': build['buildId'], 'source_commit': build['sourceCommit'], 'executable_sha256': process['executableSha256'],
            'build_manifest_sha256': digest(build_path), 'process_receipt_sha256': digest(process_path), 'run_evidence_sha256': digest(run_path)},
        'scope': {'world_id': args.world_id, 'inhabitant_id': args.inhabitant_id, 'store_pseudonym': hashlib.sha256(str(storage).encode()).hexdigest()[:16]},
        'ledger': {'checkpoint': checkpoint, 'database_sha256': digest(database), 'events': events},
        'restart': {'previous_snapshot': prior is not None, 'prior_chain_is_exact_prefix': continues},
        'foreign_scope': rejected, 'model_provenance': model_receipt(args.model_provenance.resolve() if args.model_provenance else None),
        'secrets_retained': False}
    new_json(output, result); print(json.dumps({'status': result['status'], 'output': str(output)}))


if __name__ == '__main__': main()
