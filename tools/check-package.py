#!/usr/bin/env python3
"""Independently inspect a distribution ZIP and package.ps1 manifest without extraction."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import tempfile
import zipfile

REQUIRED = {'CityLife.exe', 'UnityPlayer.dll', 'CityLife_Data/globalgamemanagers', 'README.txt', 'THIRD-PARTY-NOTICES.txt'}
PRIVATE_SUFFIXES = {'.ulf', '.alf', '.pem', '.key', '.p12', '.pfx', '.jks', '.keystore', '.sqlite', '.sqlite3', '.db', '.log'}

def digest_file(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def check(archive_path, manifest):
    if manifest.get('schema') != 'citylife.unity.package-evidence.v1':
        raise ValueError('Unsupported package manifest schema')
    if manifest.get('archive') != archive_path.name or manifest.get('archiveBytes') != archive_path.stat().st_size or manifest.get('sha256') != digest_file(archive_path):
        raise ValueError('Package archive identity, size or checksum differs from its manifest')
    entries = manifest.get('entries')
    if not isinstance(entries, list) or len(entries) != manifest.get('verifiedEntryCount'):
        raise ValueError('Package entry inventory is missing or inconsistent')
    expected = {entry['path']: entry for entry in entries}
    if len(expected) != len(entries):
        raise ValueError('Duplicate manifest entry')
    seen = set()
    names = set()
    with zipfile.ZipFile(archive_path) as archive:
        for entry in archive.infolist():
            name = entry.filename.replace('\\', '/')
            p = PurePosixPath(name)
            if p.is_absolute() or '..' in p.parts or ':' in name or any(ord(c) < 32 for c in name):
                raise ValueError('Unsafe package path')
            if 'BackUpThisFolder_ButDontShipItWithYourGame' in name or any(part.lower() in {'.git', 'library', 'usersettings', 'worlds', 'sessions'} for part in p.parts):
                raise ValueError('Private runtime/editor state or Unity backup cannot ship')
            if p.suffix.lower() in PRIVATE_SUFFIXES or p.name.lower().startswith('.env'):
                raise ValueError('Private configuration or runtime data cannot ship')
            if entry.is_dir():
                continue
            if name.casefold() in seen:
                raise ValueError('Duplicate Windows package path')
            seen.add(name.casefold())
            names.add(name)
            row = expected.get(name)
            if row is None or row['bytes'] != entry.file_size or not re.fullmatch('[0-9a-f]{64}', row.get('sha256', '')):
                raise ValueError('Unexpected package file, size or hash metadata')
            with archive.open(entry) as stream:
                if hashlib.file_digest(stream, 'sha256').hexdigest() != row['sha256']:
                    raise ValueError('Package file bytes differ from the manifest')
    if names != set(expected) or not REQUIRED.issubset(names):
        raise ValueError('Package omits expected player files or portable controls/licence notices')
    return len(names)

def fixture(path, extras=None):
    files = {name: b'Synthetic archive-guard fixture; not a player build.' for name in REQUIRED}
    files.update(extras or {})
    with zipfile.ZipFile(path, 'w') as archive:
        for name, data in files.items():
            archive.writestr(name, data)
    return dict(schema='citylife.unity.package-evidence.v1', archive=path.name, archiveBytes=path.stat().st_size, sha256=digest_file(path), verifiedEntryCount=len(files), entries=[dict(path=name, bytes=len(data), sha256=hashlib.sha256(data).hexdigest()) for name, data in files.items()])

def self_test():
    with tempfile.TemporaryDirectory(prefix='citylife-package-guard-') as directory:
        path = Path(directory) / 'fixture.zip'
        good = fixture(path)
        check(path, good)
        bad = dict(good, sha256='0' * 64)
        try:
            check(path, bad)
        except ValueError:
            pass
        else:
            raise AssertionError('Changed archive checksum was accepted')
        for name in ('../escape.txt', 'CityLife_Data/Worlds/save.json', 'x/BackUpThisFolder_ButDontShipItWithYourGame/source.cs', 'identity.ulf', 'cache.sqlite3', '.ENV.production', 'citylife.exe'):
            manifest = fixture(path, {name: b'fixture'})
            try:
                check(path, manifest)
            except ValueError:
                continue
            raise AssertionError('Unsafe archive fixture was accepted')
    print('Package guard canaries passed: checksum tampering, path escape, private state (including sqlite3 and case-insensitive env files), Unity backup, licence file and case-insensitive duplicate rejected. Fixtures were temporary and are not builds.')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path)
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test:
        self_test()
    if args.archive:
        manifest_path = args.manifest or Path(str(args.archive) + '.manifest.json')
        manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
        count = check(args.archive, manifest)
        print(f'Package integrity passed: {count} files, complete archive and per-entry hashes, player controls and licence notices present. The player was not launched.')
    elif not args.self_test:
        parser.error('Supply --archive or --self-test')

if __name__ == '__main__':
    main()
