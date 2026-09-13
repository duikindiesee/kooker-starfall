"""Explicit local setup/client. Never prints capabilities or accepts a remote endpoint."""
import argparse
import csv
import http.client
import json
import os
from pathlib import Path
import re
import secrets
import subprocess

from core import canonical, identifier, parse_json, require, validate_event
from server import configuration


def initialize(folder, world, builds, actors):
    identifier(world)
    for value in [*builds, *actors]:
        identifier(value)
    require(builds and actors and len(set(actors)) == len(actors), 'explicit-unique-identities-required')
    folder = folder.resolve()
    require(not folder.exists(), 'use-new-private-config-directory')
    folder.mkdir(parents=True, mode=0o700)
    if os.name == 'nt':
        info = subprocess.check_output(['whoami', '/user', '/fo', 'csv', '/nh'], text=True)
        sid = next(csv.reader(info.strip().splitlines()))[1]
        require(re.fullmatch(r'S-1-\d+(?:-\d+)+', sid), 'invalid-user-sid')
        subprocess.run(['icacls', str(folder), '/inheritance:r', '/grant:r', f'*{sid}:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F'], check=True, stdout=subprocess.DEVNULL)
    cfg = {'schema': 'starfall.memory.config.v1', 'world_id': world, 'publisher_id': 'unity-local',
        'publisher_token': secrets.token_urlsafe(32), 'build_ids': builds,
        'inhabitants': [{'inhabitant_id': actor, 'token': secrets.token_urlsafe(32)} for actor in actors]}
    with (folder/'config.json').open('x', encoding='utf-8') as target:
        target.write(canonical(cfg))
    configuration(folder/'config.json')
    return {'status': 'created', 'config': str(folder/'config.json'), 'capabilities': 'stored privately; not printed'}


def main():
    os.umask(0o077)
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest='command', required=True)
    init = sub.add_parser('init')
    init.add_argument('--directory', type=Path, required=True)
    init.add_argument('--world-id', required=True)
    init.add_argument('--build-id', action='append', required=True)
    init.add_argument('--inhabitant', action='append', required=True)
    for name in ('publish', 'read', 'dream'):
        cmd = sub.add_parser(name)
        cmd.add_argument('--config', type=Path, required=True)
        cmd.add_argument('--port', type=int, default=17864)
        if name == 'publish':
            cmd.add_argument('--events', type=Path, required=True)
        else:
            cmd.add_argument('--inhabitant', required=True)
        if name == 'read':
            cmd.add_argument('--resource', choices=('identity', 'memories', 'facts', 'beliefs', 'dreams', 'wiki'), default='wiki')
            cmd.add_argument('--after', type=int, default=0)
    args = parser.parse_args()
    if args.command == 'init':
        print(canonical(initialize(args.directory, args.world_id, args.build_id, args.inhabitant)))
        return
    require(1 <= args.port <= 65535, 'invalid-port')
    cfg = configuration(args.config)
    token = cfg['publisher_token'] if args.command == 'publish' else next((x['token'] for x in cfg['inhabitants'] if x['inhabitant_id'] == args.inhabitant), None)
    require(token, 'unconfigured-inhabitant')
    def call(method, resource, data=None):
        client = http.client.HTTPConnection('127.0.0.1', args.port, timeout=5)
        try:
            client.request(method, '/v1/' + resource, canonical(data).encode() if data is not None else None,
                {'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'})
            response = client.getresponse()
            body = parse_json(response.read(1024 * 1024))
            require(response.status == 200, body.get('error', 'request-failed'), response.status)
            return body
        finally:
            client.close()
    if args.command == 'publish':
        require(args.events.stat().st_size <= 1024 * 1024, 'export-too-large')
        events = [parse_json(line) for line in args.events.read_bytes().splitlines()]
        require(0 < len(events) <= 1000, 'invalid-export-length')
        for event in events:
            validate_event(event, cfg['world_id'], cfg['publisher_id'], cfg['build_ids'])
        inserted = sum(call('POST', 'events', event)['inserted'] for event in events)
        print(canonical({'accepted': len(events), 'inserted': inserted, 'already_present': len(events) - inserted}))
    elif args.command == 'dream':
        print(json.dumps(call('POST', 'dreams', {'schema': 'starfall.dream.request.v1'}), indent=2))
    else:
        print(json.dumps(call('GET', args.resource + '?after=' + str(args.after)), indent=2))


if __name__ == '__main__':
    main()
