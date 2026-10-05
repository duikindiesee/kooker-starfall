"""Local API with a separate publisher capability and per-inhabitant capabilities."""
import argparse
import hmac
import json
import os
import re
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlsplit

from core import MemoryStore, Rejected, canonical, exact, identifier, parse_json, require


def configuration(path):
    config = parse_json(Path(path).read_bytes())
    exact(config, 'schema world_id publisher_id publisher_token build_ids inhabitants')
    require(config['schema'] == 'starfall.memory.config.v1', 'unsupported-config')
    identifier(config['world_id'])
    identifier(config['publisher_id'])
    require(type(config['inhabitants']) is list and 1 <= len(config['inhabitants']) <= 64, 'invalid-inhabitants')
    tokens = [config['publisher_token']]
    actors = []
    for entry in config['inhabitants']:
        exact(entry, 'inhabitant_id token')
        actors.append(identifier(entry['inhabitant_id']))
        tokens.append(entry['token'])
    require(len(set(actors)) == len(actors) and len(set(tokens)) == len(tokens), 'duplicate-capability')
    require(all(type(t) is str and re.fullmatch('[a-zA-Z0-9_-]{40,128}', t) for t in tokens), 'invalid-capability')
    return config


class Server(ThreadingHTTPServer):
    daemon_threads = True
    block_on_close = True
    allow_reuse_address = False
    def __init__(self, address, store, config):
        self.store, self.config = store, config
        self.slots = threading.BoundedSemaphore(8)
        super().__init__(address, Handler)

    def process_request(self, request, address):
        if not self.slots.acquire(blocking=False):
            request.close()
            return
        super().process_request(request, address)

    def process_request_thread(self, request, address):
        try:
            super().process_request_thread(request, address)
        finally:
            self.slots.release()


class Handler(BaseHTTPRequestHandler):
    server_version = 'StarfallMemory/1'
    def setup(self):
        super().setup()
        self.connection.settimeout(5)

    def log_message(self, *_):
        pass  # No tokens, request bodies, paths or personal logs.

    def authenticate(self):
        require(self.headers.get('Origin') is None, 'browser-origin-disabled', 403)
        require(len(self.headers.get_all('Authorization', [])) == 1, 'capability-required', 401)
        token = self.headers['Authorization']
        require(token.startswith('Bearer ') and len(token) <= 140, 'capability-required', 401)
        token = token[7:]
        if hmac.compare_digest(token, self.server.config['publisher_token']):
            return 'publisher', None
        for entry in self.server.config['inhabitants']:
            if hmac.compare_digest(token, entry['token']):
                return 'inhabitant', entry['inhabitant_id']
        raise Rejected('invalid-capability', 401)

    def body(self):
        if hasattr(self, '_request_body'):
            return self._request_body
        require(self.headers.get('Transfer-Encoding') is None, 'transfer-encoding-disabled')
        require(self.headers.get('Content-Type', '').split(';')[0] == 'application/json', 'json-required', 415)
        sizes = self.headers.get_all('Content-Length', [])
        require(len(sizes) == 1 and sizes[0].isdigit(), 'content-length-required', 411)
        size = int(sizes[0])
        require(0 < size <= 65536, 'body-too-large', 413)
        raw = self.rfile.read(size)
        require(len(raw) == size, 'incomplete-body')
        require(size <= 16384, 'body-too-large', 413)
        return parse_json(raw)

    def dispatch(self):
        role, actor = self.authenticate()
        require(len(self.path) <= 2048, 'path-too-long', 414)
        url = urlsplit(self.path)
        require(not url.scheme and not url.netloc and not url.fragment, 'invalid-path')
        query = parse_qs(url.query, keep_blank_values=True)
        require(set(query) <= {'after', 'limit'} and all(len(v) == 1 and v[0].isdigit() and len(v[0]) <= 10 for v in query.values()), 'invalid-query')
        after, limit = int(query.get('after', ['0'])[0]), int(query.get('limit', ['50'])[0])
        store = self.server.store
        if self.command == 'GET' and url.path == '/v1/health':
            return {'schema': 'starfall.health.v1', 'status': 'ok', 'world_id': store.world, **store.verify()}
        if self.command == 'POST' and url.path == '/v1/events':
            require(role == 'publisher', 'publisher-capability-required', 403)
            return store.append(self.body())
        require(role == 'inhabitant', 'inhabitant-capability-required', 403)
        if self.command == 'GET' and url.path == '/v1/identity':
            return store.records(actor, ('identity',), after, limit)
        if self.command == 'GET' and url.path == '/v1/memories':
            return store.records(actor, ('episode',), after, limit)
        if self.command == 'GET' and url.path == '/v1/facts':
            return store.records(actor, ('fact',), after, limit, shared=True)
        if self.command == 'GET' and url.path == '/v1/beliefs':
            return store.records(actor, ('belief',), after, limit)
        if self.command == 'GET' and url.path == '/v1/dreams':
            return store.records(actor, ('dream',), after, limit)
        if self.command == 'GET' and url.path == '/v1/wiki':
            return store.wiki(actor)
        if self.command == 'POST' and url.path == '/v1/beliefs':
            return store.belief(actor, self.body())
        if self.command == 'POST' and url.path == '/v1/dreams':
            request = self.body()
            exact(request, 'schema')
            require(request['schema'] == 'starfall.dream.request.v1', 'unsupported-schema')
            return store.dream(actor)
        raise Rejected('no-such-route', 404)

    def respond(self):
        try:
            # Consume bounded writes before a role/route rejection. Closing an unread
            # request body can reset the TCP connection and hide a 403 on Windows.
            if self.command in ('POST', 'PUT', 'PATCH', 'DELETE'):
                self._request_body = self.body()
            payload, status = self.dispatch(), 200
        except Rejected as error:
            payload, status = {'error': error.code}, error.status
        except (OSError, ValueError, TypeError, KeyError):
            payload, status = {'error': 'invalid-request'}, 400
        except Exception:
            payload, status = {'error': 'internal-error'}, 500
        raw = canonical(payload).encode()
        self.send_response(status)
        self.send_header('Content-Type', 'application/json; charset=utf-8')
        self.send_header('Content-Length', str(len(raw)))
        self.send_header('Cache-Control', 'no-store')
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.send_header('Connection', 'close')
        self.end_headers()
        self.wfile.write(raw)

    do_GET = do_POST = do_PUT = do_PATCH = do_DELETE = do_OPTIONS = respond


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--config', required=True)
    parser.add_argument('--data-dir', required=True)
    parser.add_argument('--port', type=int, default=17864)
    parser.add_argument('--container', action='store_true', help='Use only behind the Compose loopback published port')
    args = parser.parse_args()
    os.umask(0o077)
    config = configuration(args.config)
    data = Path(args.data_dir).resolve()
    data.mkdir(parents=True, exist_ok=True)
    store = MemoryStore(data/'starfall-memory.sqlite3', config['world_id'], config['publisher_id'], config['build_ids'])
    service = Server(('0.0.0.0' if args.container else '127.0.0.1', args.port), store, config)
    print(canonical({'status': 'ready', 'port': service.server_port, 'schema': 'starfall.memory.api.v1'}), flush=True)
    try:
        service.serve_forever(poll_interval=0.2)
    finally:
        service.server_close()
        store.close()


if __name__ == '__main__':
    main()
