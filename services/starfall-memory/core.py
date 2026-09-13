"""Starfall-owned local memory. Standard library only; no model, shell or game client."""
import hashlib
import json
import re
import sqlite3
import threading
from pathlib import Path

EVENT_SCHEMA = 'starfall.event.v1'
RECORD_SCHEMA = 'starfall.memory.record.v1'
ID = re.compile(r'[a-zA-Z0-9][a-zA-Z0-9._-]{0,95}\Z')


class Rejected(Exception):
    def __init__(self, code, status=400):
        self.code, self.status = code, status
        super().__init__(code)


def require(condition, code, status=400):
    if not condition:
        raise Rejected(code, status)


def exact(value, keys):
    require(type(value) is dict and set(value) == set(keys.split()), 'unexpected-fields')


def identifier(value):
    require(type(value) is str and ID.fullmatch(value), 'invalid-identifier')
    return value


def bounded_text(value, limit):
    require(type(value) is str and 0 < len(value.strip()) <= limit
            and all(ord(c) >= 32 and ord(c) != 127 for c in value), 'invalid-text')
    return value


def canonical(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=True, separators=(',', ':'), allow_nan=False)


def parse_json(raw):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, 'duplicate-json-key')
            result[key] = value
        return result
    def invalid(_):
        raise Rejected('nonfinite-json-number')
    try:
        return json.loads(raw, object_pairs_hook=pairs, parse_constant=invalid)
    except (UnicodeError, ValueError, RecursionError) as error:
        raise Rejected('invalid-json') from error


def validate_event(event, world, publisher, builds):
    exact(event, 'schema world_id inhabitant_id source tick kind data')
    require(event['schema'] == EVENT_SCHEMA, 'unsupported-schema')
    require(event['world_id'] == world, 'world-mismatch', 403)
    identifier(event['inhabitant_id'])
    source = event['source']
    exact(source, 'producer_id session_id build_id sequence')
    require(source['producer_id'] == publisher, 'publisher-mismatch', 403)
    identifier(source['session_id'])
    require(source['build_id'] in builds, 'untrusted-build', 403)
    require(type(source['sequence']) is int and 1 <= source['sequence'] <= 2147483647, 'invalid-sequence')
    require(type(event['tick']) is int and 0 <= event['tick'] <= 2147483647, 'invalid-tick')
    kind, data = event['kind'], event['data']
    if kind == 'identity_registered':
        exact(data, 'display_name')
        bounded_text(data['display_name'], 80)
    elif kind == 'action_completed':
        exact(data, 'action target_id item_id request_id outcome')
        require(data['action'] in ('pickup', 'deliver'), 'unsupported-action')
        require(data['outcome'] == {'pickup': 'picked-up', 'deliver': 'delivered'}[data['action']], 'invalid-outcome')
        identifier(data['target_id'])
        identifier(data['item_id'])
        require(type(data['request_id']) is int and 0 < data['request_id'] <= 2147483647, 'invalid-request-id')
        if data['action'] == 'pickup':
            require(data['target_id'] == data['item_id'], 'pickup-item-mismatch')
    elif kind in ('sleep_started', 'sleep_ended'):
        exact(data, '')
    else:
        raise Rejected('unsupported-event-kind')
    return event


class MemoryStore:
    """One configured world per database. Serialized transactions; immutable evidence."""
    def __init__(self, path, world, publisher, builds):
        self.world, self.publisher, self.builds = identifier(world), identifier(publisher), builds
        require(type(builds) is list and builds and all(ID.fullmatch(x) for x in builds), 'invalid-build-list')
        self.lock = threading.RLock()
        self.db = sqlite3.connect(path, check_same_thread=False, isolation_level=None, timeout=5)
        self.db.row_factory = sqlite3.Row
        self.db.execute('PRAGMA foreign_keys=ON')
        self.db.execute('PRAGMA journal_mode=WAL')
        self.db.execute('PRAGMA synchronous=FULL')
        version = self.db.execute('PRAGMA user_version').fetchone()[0]
        require(version in (0, 1), 'unsupported-database-version', 503)
        if version == 0:
            require(not self.db.execute("SELECT name FROM sqlite_master WHERE type='table'").fetchall(), 'nonempty-unversioned-database', 503)
            self.db.executescript('''
                BEGIN IMMEDIATE;
                CREATE TABLE metadata (world TEXT NOT NULL, publisher TEXT NOT NULL);
                CREATE TABLE events (
                    id INTEGER PRIMARY KEY, event_id TEXT NOT NULL UNIQUE,
                    session TEXT NOT NULL, sequence INTEGER NOT NULL,
                    actor TEXT NOT NULL, tick INTEGER NOT NULL, kind TEXT NOT NULL,
                    body TEXT NOT NULL, previous_hash TEXT NOT NULL, chain_hash TEXT NOT NULL,
                    UNIQUE(session, sequence));
                CREATE TABLE records (
                    id INTEGER PRIMARY KEY, kind TEXT NOT NULL, actor TEXT NOT NULL,
                    record_key TEXT NOT NULL, body TEXT NOT NULL,
                    UNIQUE(kind, record_key));
                CREATE INDEX records_namespace ON records(actor, kind, id);
                CREATE INDEX events_namespace ON events(actor, kind, id);
                PRAGMA user_version=1;
                COMMIT;
            ''')
            self.db.execute('INSERT INTO metadata VALUES (?, ?)', (world, publisher))
        require(tuple(self.db.execute('SELECT world, publisher FROM metadata').fetchone()) == (world, publisher), 'database-namespace-mismatch', 503)
        for table in ('metadata', 'events', 'records'):
            for operation in ('UPDATE', 'DELETE'):
                self.db.execute(f"CREATE TRIGGER IF NOT EXISTS {table}_no_{operation.lower()} BEFORE {operation} ON {table} BEGIN SELECT RAISE(ABORT, 'append-only'); END")
        self.verify()

    def close(self):
        self.db.close()

    def verify(self):
        with self.lock:
            require(self.db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok', 'database-corrupt', 503)
            previous = '0' * 64
            count = 0
            for row in self.db.execute('SELECT * FROM events ORDER BY id'):
                event = parse_json(row['body'])
                source = event['source']
                require(row['id'] == count + 1 and row['previous_hash'] == previous
                        and canonical(event) == row['body']
                        and row['event_id'] == hashlib.sha256(row['body'].encode()).hexdigest()
                        and row['session'] == source['session_id'] and row['sequence'] == source['sequence']
                        and row['actor'] == event['inhabitant_id'] and row['tick'] == event['tick']
                        and row['kind'] == event['kind'], 'ledger-integrity-failed', 503)
                expected = hashlib.sha256((previous + '\n' + row['body']).encode()).hexdigest()
                require(row['chain_hash'] == expected, 'ledger-integrity-failed', 503)
                previous, count = expected, count + 1
            return {'events': count, 'head': previous}

    def _last_event(self, actor, kinds):
        placeholders = ','.join('?' for _ in kinds)
        row = self.db.execute(f'SELECT * FROM events WHERE actor=? AND kind IN ({placeholders}) ORDER BY id DESC LIMIT 1', (actor, *kinds)).fetchone()
        return row

    def _record(self, kind, actor, key, payload):
        body = {'schema': RECORD_SCHEMA, 'world_id': self.world, 'inhabitant_id': actor, **payload}
        self.db.execute('INSERT INTO records(kind,actor,record_key,body) VALUES(?,?,?,?)', (kind, actor, key, canonical(body)))
        return body

    def append(self, event):
        validate_event(event, self.world, self.publisher, self.builds)
        body = canonical(event)
        require(len(body.encode()) <= 8192, 'event-too-large', 413)
        event_id = hashlib.sha256(body.encode()).hexdigest()
        source, actor, kind, data = event['source'], event['inhabitant_id'], event['kind'], event['data']
        with self.lock:
            self.db.execute('BEGIN IMMEDIATE')
            try:
                prior = self.db.execute('SELECT event_id,chain_hash FROM events WHERE session=? AND sequence=?', (source['session_id'], source['sequence'])).fetchone()
                if prior:
                    require(prior['event_id'] == event_id, 'source-sequence-conflict', 409)
                    self.db.execute('ROLLBACK')
                    return {'inserted': False, **dict(prior)}
                last = self.db.execute('SELECT sequence,tick FROM events WHERE session=? ORDER BY sequence DESC LIMIT 1', (source['session_id'],)).fetchone()
                require(source['sequence'] == (last['sequence'] + 1 if last else 1), 'source-sequence-gap', 409)
                require(not last or event['tick'] >= last['tick'], 'tick-regression', 409)
                identity = self._last_event(actor, ('identity_registered',))
                require((identity is None) == (kind == 'identity_registered'), 'identity-required-or-already-registered', 409)
                sleeping = self._last_event(actor, ('sleep_started', 'sleep_ended'))
                is_asleep = bool(sleeping and sleeping['kind'] == 'sleep_started')
                if kind == 'sleep_started':
                    require(not is_asleep, 'already-asleep', 409)
                if kind == 'sleep_ended':
                    require(is_asleep, 'not-asleep', 409)
                if kind == 'action_completed':
                    require(not is_asleep, 'action-while-asleep', 409)
                    last_action = self._last_event(actor, ('action_completed',))
                    last_data = parse_json(last_action['body'])['data'] if last_action else None
                    held = last_data['item_id'] if last_data and last_data['action'] == 'pickup' else None
                    require((data['action'] == 'pickup' and held is None) or (data['action'] == 'deliver' and held == data['item_id']), 'cargo-trace-mismatch', 409)
                    for old in self.db.execute("SELECT body FROM events WHERE actor=? AND session=? AND kind='action_completed'", (actor, source['session_id'])):
                        require(parse_json(old['body'])['data']['request_id'] != data['request_id'], 'action-receipt-replayed', 409)
                last_hash = self.db.execute('SELECT chain_hash FROM events ORDER BY id DESC LIMIT 1').fetchone()
                previous = last_hash[0] if last_hash else '0' * 64
                chain_hash = hashlib.sha256((previous + '\n' + body).encode()).hexdigest()
                self.db.execute('INSERT INTO events(event_id,session,sequence,actor,tick,kind,body,previous_hash,chain_hash) VALUES(?,?,?,?,?,?,?,?,?)', (event_id, source['session_id'], source['sequence'], actor, event['tick'], kind, body, previous, chain_hash))
                summary = self.describe(event)
                refs = [event_id]
                self._record('episode', actor, event_id, {'kind': 'episode', 'epistemic_status': 'confirmed_event', 'summary': summary, 'evidence_ids': refs, 'tick': event['tick']})
                if kind in ('identity_registered', 'action_completed'):
                    self._record('fact', actor, event_id, {'kind': 'wiki_fact', 'epistemic_status': 'confirmed_event', 'summary': summary, 'evidence_ids': refs, 'temporal_scope': 'past_event_only'})
                if kind == 'identity_registered':
                    self._record('identity', actor, actor, {'kind': 'identity', 'display_name': data['display_name'], 'evidence_ids': refs})
                self.db.execute('COMMIT')
                return {'inserted': True, 'event_id': event_id, 'chain_hash': chain_hash}
            except BaseException:
                if self.db.in_transaction:
                    self.db.execute('ROLLBACK')
                raise

    @staticmethod
    def describe(event):
        actor, kind, data = event['inhabitant_id'], event['kind'], event['data']
        if kind == 'identity_registered':
            return f"{actor} was registered as {data['display_name']}."
        if kind == 'action_completed':
            if data['action'] == 'pickup':
                return f"{actor} picked up {data['item_id']} at tick {event['tick']}."
            return f"{actor} delivered {data['item_id']} to {data['target_id']} at tick {event['tick']}."
        return f"{actor} {'started sleeping' if kind == 'sleep_started' else 'woke'} at tick {event['tick']}."

    def _owned_refs(self, actor, refs):
        require(type(refs) is list and 1 <= len(refs) <= 16 and len(set(refs)) == len(refs), 'invalid-evidence-list')
        for ref in refs:
            require(type(ref) is str and re.fullmatch('[0-9a-f]{64}', ref), 'invalid-evidence-id')
            require(self.db.execute('SELECT 1 FROM events WHERE event_id=? AND actor=?', (ref, actor)).fetchone(), 'evidence-not-in-namespace', 403)

    def belief(self, actor, request):
        exact(request, 'schema request_id label text evidence_ids')
        require(request['schema'] == 'starfall.belief.v1', 'unsupported-schema')
        identifier(request['request_id'])
        require(request['label'] in ('belief', 'theory'), 'belief-cannot-be-confirmed')
        bounded_text(request['text'], 500)
        with self.lock:
            self._owned_refs(actor, request['evidence_ids'])
            key = actor + ':' + request['request_id']
            payload = {'kind': 'belief', 'epistemic_status': request['label'], 'text': request['text'], 'evidence_ids': request['evidence_ids'], 'request_id': request['request_id']}
            old = self.db.execute("SELECT body FROM records WHERE kind='belief' AND record_key=?", (key,)).fetchone()
            if old:
                result = parse_json(old['body'])
                require(all(result[k] == v for k, v in payload.items()), 'belief-request-conflict', 409)
                return result
            return self._record('belief', actor, key, payload)

    def dream(self, actor):
        with self.lock:
            self.db.execute('BEGIN IMMEDIATE')
            try:
                sleep = self._last_event(actor, ('sleep_started', 'sleep_ended'))
                require(sleep and sleep['kind'] == 'sleep_started', 'dream-requires-verified-sleep', 409)
                key = sleep['event_id']
                old = self.db.execute("SELECT body FROM records WHERE kind='dream' AND record_key=?", (key,)).fetchone()
                if old:
                    self.db.execute('ROLLBACK')
                    return parse_json(old['body'])
                rows = self.db.execute('SELECT event_id,body FROM events WHERE actor=? AND id<=? ORDER BY id DESC LIMIT 32', (actor, sleep['id'])).fetchall()[::-1]
                events = [parse_json(row['body']) for row in rows]
                associations = []
                for row, event in zip(rows, events):
                    if event['kind'] == 'action_completed' and event['data']['action'] == 'deliver':
                        associations.append({'epistemic_status': 'theory', 'text': f"Perhaps {event['data']['target_id']} could be useful to visit again.", 'evidence_ids': [row['event_id']]})
                payload = {'kind': 'dream', 'epistemic_status': 'derived_summary_and_theories', 'processor': 'starfall.dream.rules.v1', 'sleep_event_id': key, 'summary': [self.describe(event) for event in events], 'associations': associations, 'evidence_ids': [row['event_id'] for row in rows], 'window_limit': 32, 'authority': 'derived_only'}
                result = self._record('dream', actor, key, payload)
                self.db.execute('COMMIT')
                return result
            except BaseException:
                if self.db.in_transaction:
                    self.db.execute('ROLLBACK')
                raise

    def records(self, actor, kinds, after=0, limit=50, shared=False):
        require(type(after) is int and after >= 0 and type(limit) is int and 1 <= limit <= 100, 'invalid-page')
        with self.lock:
            placeholders = ','.join('?' for _ in kinds)
            args = (*kinds, after, limit + 1) if shared else (*kinds, after, actor, limit + 1)
            rows = self.db.execute(f"SELECT id,body FROM records WHERE kind IN ({placeholders}) AND id>? {' ' if shared else 'AND actor=?'} ORDER BY id LIMIT ?", args).fetchall()
            return {'items': [{'cursor': row['id'], **parse_json(row['body'])} for row in rows[:limit]], 'has_more': len(rows) > limit, 'next_after': rows[min(len(rows), limit)-1]['id'] if rows else after}

    def wiki(self, actor):
        return {'schema': 'starfall.wiki.v1', 'world_id': self.world,
                'confirmed_facts': self.records(actor, ('fact',), limit=100, shared=True),
                'inhabitant_beliefs': self.records(actor, ('belief',), limit=100),
                'inhabitant_dreams': self.records(actor, ('dream',), limit=100),
                'note': 'Confirmed entries describe past verified events. Beliefs and dream theories never become confirmed facts automatically.'}
