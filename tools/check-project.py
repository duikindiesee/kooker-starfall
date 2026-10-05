#!/usr/bin/env python3
"""Offline public-repository, Unity metadata and imported-art checks; no Unity execution."""
import argparse
import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import re
import subprocess
import tempfile
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parents[1]
PRIVATE_SUFFIXES = {'.pem', '.key', '.p12', '.pfx', '.jks', '.keystore', '.ulf', '.alf', '.db', '.sqlite', '.sqlite3', '.log'}
ART_SUFFIXES = {'.png', '.jpg', '.jpeg', '.tga', '.tif', '.tiff', '.exr', '.hdr', '.psd', '.blend', '.fbx', '.obj', '.glb', '.gltf', '.wav', '.mp3', '.ogg', '.ttf', '.otf'}
PRIVATE_ROOTS = {'library', 'temp', 'logs', 'obj', 'builds', 'usersettings', 'node_modules', '.vs'}

def require(condition, message):
    if not condition:
        raise ValueError(message)

def candidates():
    result = subprocess.run(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=ROOT, check=True, stdout=subprocess.PIPE)
    return sorted(set(name for name in result.stdout.decode('utf-8').split('\0') if name))

def safe_relative(value):
    require(isinstance(value, str) and value and '\\' not in value, 'Asset path must be a repository-relative forward-slash path')
    p = PurePosixPath(value)
    require(not p.is_absolute() and '..' not in p.parts and ':' not in value, 'Asset path must stay inside the repository')
    return value

def https(value):
    require(isinstance(value, str), 'Provenance URL must be text')
    p = urlparse(value)
    require(p.scheme == 'https' and p.netloc and not p.username and not p.password, 'Provenance URL must be public HTTPS without credentials')

def check_public_path(relative):
    p = PurePosixPath(relative)
    require(not any(ord(c) < 32 for c in relative), 'Control characters are not permitted in publishable file names')
    require(p.parts[0].lower() not in PRIVATE_ROOTS and not relative.lower().startswith('evidence/local/'), 'Generated/private state cannot be committed: ' + relative)
    require(not p.name.lower().startswith('.env') and p.suffix.lower() not in PRIVATE_SUFFIXES, 'Private configuration, signing material, database or raw log cannot be committed: ' + relative)
    require(not any(part.lower() in {'worlds', 'sessions', 'archives'} for part in p.parts), 'Player/runtime state cannot be committed: ' + relative)
    require('BackUpThisFolder_ButDontShipItWithYourGame' not in relative, 'Unity build backup cannot be committed')

def validate_provenance(entry, root, visible):
    require(isinstance(entry, dict), 'Asset provenance entry must be an object')
    for key in ('id', 'name', 'publisher', 'downloadedUtc', 'modifications'):
        require(isinstance(entry.get(key), str) and entry[key].strip(), 'Missing asset provenance field: ' + key)
    require(entry.get('license') == 'CC0-1.0', 'This milestone admits imported CC0-1.0 art only; review another licence explicitly')
    require(isinstance(entry.get('authors'), list) and entry['authors'] and all(isinstance(a, str) and a.strip() for a in entry['authors']), 'Asset authors must be recorded')
    for key in ('sourceUrl', 'downloadUrl', 'licenseUrl', 'publisherLicenseUrl'):
        https(entry.get(key))
    relative = safe_relative(entry.get('localPath'))
    notice = safe_relative(entry.get('licenseNoticePath'))
    require(relative.startswith('Assets/') and relative in visible, 'Imported art must be in the publishable Assets inventory')
    require(notice in visible and (root / notice).is_file(), 'Missing committed asset licence notice')
    require(len((root / notice).read_text(encoding='utf-8-sig').strip()) > 80, 'Asset licence notice is empty or incomplete')
    digest = entry.get('sha256', '')
    require(isinstance(digest, str) and re.fullmatch('[0-9a-f]{64}', digest), 'Asset SHA-256 must be explicit lower-case hex')
    data = (root / relative).read_bytes()
    require(type(entry.get('bytes')) is int and entry['bytes'] == len(data), 'Imported asset byte length changed: ' + relative)
    require(hashlib.sha256(data).hexdigest() == digest, 'Imported asset checksum changed: ' + relative)
    require(len(data) <= 25 * 1024 * 1024, 'Imported asset exceeds the first-milestone 25 MiB per-file review limit')
    return relative

def check_assets(files):
    visible = set(files)
    imported = set()
    manifests = [p for p in files if p.startswith('Assets/') and p.endswith('/provenance.json')]
    for relative in manifests:
        document = json.loads((ROOT / relative).read_text(encoding='utf-8-sig'))
        entries = document.get('assets', [document]) if isinstance(document, dict) else document
        require(isinstance(entries, list) and entries, 'Asset provenance manifest must contain records')
        for entry in entries:
            item = validate_provenance(entry, ROOT, visible)
            require(item not in imported, 'Duplicate provenance for imported art: ' + item)
            imported.add(item)
    binary_art = {p for p in files if p.startswith('Assets/') and PurePosixPath(p).suffix.lower() in ART_SUFFIXES}
    require(binary_art == imported, 'Every imported model/texture/audio/font needs checked provenance; missing: ' + ', '.join(sorted(binary_art - imported)))
    return len(imported)

def check_unity(files):
    version = (ROOT / 'ProjectSettings/ProjectVersion.txt').read_text(encoding='utf-8-sig')
    require(re.search(r'^m_EditorVersion: 6000\.6\.0f1$', version, re.M), 'Unity editor baseline changed; update checks and build evidence deliberately')
    manifest = json.loads((ROOT / 'Packages/manifest.json').read_text())['dependencies']
    lock = json.loads((ROOT / 'Packages/packages-lock.json').read_text())['dependencies']
    require(manifest.get('com.unity.render-pipelines.universal') == '17.6.0', 'Expected pinned URP 17.6.0')
    require(manifest.get('com.unity.inputsystem') == '1.20.0', 'Expected pinned Input System 1.20.0')
    for package, value in manifest.items():
        require(re.fullmatch(r'\d+\.\d+\.\d+', value), 'Pin package versions; do not import a private/local registry: ' + package)
        require(package in lock and lock[package]['version'] == value, 'Package lock does not match manifest: ' + package)
    guids = {}
    asset_paths = set()
    for relative in files:
        if not relative.startswith('Assets/'):
            continue
        path = ROOT / relative
        if relative.endswith('.meta'):
            match = re.search(r'^guid: ([0-9a-f]{32})$', path.read_text(encoding='utf-8-sig'), re.M)
            require(match is not None, 'Malformed Unity metadata GUID: ' + relative)
            guid = match.group(1)
            require(guid not in guids, 'Duplicate Unity GUID: ' + relative)
            guids[guid] = relative
            require(path.with_suffix('').exists(), 'Orphan Unity metadata: ' + relative)
        else:
            asset_paths.add(relative)
            for parent in PurePosixPath(relative).parents:
                if str(parent) not in {'.', 'Assets'}:
                    asset_paths.add(str(parent))
    for relative in sorted(asset_paths):
        require(relative + '.meta' in files, 'Missing committed Unity metadata; import in the editor: ' + relative)
    scene = 'Assets/CityLife/Scenes/Island.unity'
    require(scene in files, 'Playable scene is missing')
    require(scene in (ROOT / 'ProjectSettings/EditorBuildSettings.asset').read_text(), 'Playable scene is absent from build settings')
    settings = (ROOT / 'ProjectSettings/ProjectSettings.asset').read_text()
    for name in ('AndroidKeystoreName', 'metroCertificatePassword', 'cloudProjectId', 'organizationId'):
        match = re.search(r'^  ' + name + r':[^\S\r\n]*(.*)$', settings, re.M)
        require(match is None or not match.group(1).strip(), 'Do not publish private signing/cloud linkage: ' + name)
    definition = json.loads((ROOT / 'Assets/CityLife/Resources/IslandDefinition.json').read_text())
    oracle = json.loads((ROOT / 'Assets/CityLife/Resources/SourceVectors.json').read_text())
    require(definition['sourceCommit'] == oracle['sourceCommit'], 'World source commit and oracle disagree')
    require(definition['schema'] == 'citylife.unity.world.v1' and definition['generator'] == 'citylife.desert-island.v1', 'Unexpected world format; migration review required')
    require(definition['cells'] == oracle['settings']['cells'] and definition['heightScale'] == oracle['settings']['heightScale'], 'World dimensions and source oracle disagree')
    require(len(oracle['terrainVectors']) == 420 and len(oracle['noiseVectors']) == 42 and sum(len(v['values']) for v in oracle['rngVectors']) == 96, 'Incomplete source oracle')
    require(all(math.isfinite(v['height']) for v in oracle['terrainVectors']), 'Non-finite source height')
    return len(guids)

def self_test():
    for path in ('Library/a', 'Builds/Windows/game.exe', 'evidence/local/run.log', '.env.production', 'key.pfx', 'identity.ulf', 'Worlds/save.json'):
        try:
            check_public_path(path)
        except ValueError:
            continue
        raise AssertionError('Private path canary was accepted')
    with tempfile.TemporaryDirectory(prefix='citylife-art-guard-') as directory:
        root = Path(directory)
        (root / 'Assets').mkdir()
        (root / 'Assets/a.jpg').write_bytes(b'fixture-image')
        (root / 'NOTICE.md').write_text('CC0 test fixture notice. ' * 8)
        entry = dict(id='fixture', name='Fixture', publisher='Fixture', downloadedUtc='2026-09-09T00:00:00Z', modifications='none', authors=['Fixture'], license='CC0-1.0', sourceUrl='https://example.com/source', downloadUrl='https://example.com/asset', licenseUrl='https://creativecommons.org/publicdomain/zero/1.0/', publisherLicenseUrl='https://example.com/license', localPath='Assets/a.jpg', licenseNoticePath='NOTICE.md', sha256=hashlib.sha256(b'fixture-image').hexdigest(), bytes=13)
        visible = {'Assets/a.jpg', 'NOTICE.md'}
        validate_provenance(entry, root, visible)
        for field, value in (('sha256', '0' * 64), ('license', 'unverified'), ('localPath', '../private'), ('licenseNoticePath', 'missing.txt')):
            bad = dict(entry, **{field: value})
            try:
                validate_provenance(bad, root, visible)
            except ValueError:
                continue
            raise AssertionError('Invalid art canary was accepted: ' + field)
    print('Guard canaries passed: private paths, asset tampering, missing notice, unapproved licence and path escape rejected.')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test:
        self_test()
    files = [p for p in candidates() if (ROOT / p).exists()]
    for relative in files:
        check_public_path(relative)
        require(not (ROOT / relative).is_symlink(), 'Do not publish symlinked local content: ' + relative)
    art = check_assets(files)
    guids = check_unity(files)
    print(f'Project safeguards passed: {len(files)} publishable files, {guids} unique Unity GUIDs, {art} verified CC0 art records. This is not a Unity compile or runtime test.')

if __name__ == '__main__':
    main()
