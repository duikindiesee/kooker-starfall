"""One isolated real-player memory proof; private capabilities never appear in output."""
import argparse
import hashlib
import http.client
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]

def build_content(directory):
    entries = []
    for path in directory.rglob('*'):
        if path.is_symlink():
            raise RuntimeError('Linked build content rejected')
        if not path.is_file():
            continue
        relative = path.relative_to(directory).as_posix()
        if any(c in relative for c in '\r\n\t'):
            raise RuntimeError('Unsupported filename in build fingerprint')
        entries.append(f'{relative}\t{path.stat().st_size}\t{hashlib.sha256(path.read_bytes()).hexdigest()}')
    entries.sort()
    digest = hashlib.sha256(('\n'.join(entries) + '\n').encode('utf-8')).hexdigest()
    return {'schema': 'starfall.build-content.v1', 'sha256': digest, 'files': len(entries)}

def compiled_source(exe):
    matches = []
    for manifest in (ROOT / 'evidence' / 'milestones' / 'coastal').glob('round-*/preview-build.json'):
        try:
            data = json.loads(manifest.read_text(encoding='utf-8'))
            output = (ROOT / data['output']).resolve()
        except (KeyError, OSError, ValueError):
            continue
        if output == exe:
            matches.append((manifest, data.get('sourceCommit', '')))
    if len(matches) != 1 or len(matches[0][1]) != 40:
        raise RuntimeError('Require one exact build manifest/source for compiled player')
    return matches[0]
sys.path.insert(0, str(ROOT / 'services' / 'starfall-memory'))
from local import initialize
from core import MemoryStore


def save(path, value):
    path.write_text(json.dumps(value, indent=2), encoding='utf-8')


def main():
    parser = argparse.ArgumentParser()
    host = parser.add_mutually_exclusive_group(required=True)
    host.add_argument('--exe', type=Path)
    host.add_argument('--editor', type=Path)
    parser.add_argument('--scene')
    parser.add_argument('--integrated', action='store_true')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    exe, output = (args.editor or args.exe).resolve(), args.output.resolve()
    if not exe.is_file() or output.exists():
        raise RuntimeError('Require existing separate executable and new evidence directory')
    build_manifest = None
    if args.editor:
        source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    else:
        build_manifest, source = compiled_source(exe)
        subprocess.check_call(['git', 'cat-file', '-e', source + '^{commit}'], cwd=ROOT)
    if args.editor:
        if exe != Path(r'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe'):
            raise RuntimeError('Require pinned installed Unity Editor')
        if subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip():
            raise RuntimeError('Commit exact Editor source before acceptance')
        if not args.scene or not args.scene.startswith('Assets/CityLife/GeneratedPreview-Character-') or '..' in args.scene or not (ROOT / args.scene).is_file():
            raise RuntimeError('Require preserved generated scene')
    output.mkdir(parents=True)
    inventory_client = http.client.HTTPConnection('127.0.0.1', 1234, timeout=3)
    try:
        inventory_client.request('GET', '/api/v1/models')
        inventory_response = inventory_client.getresponse()
        inventory = json.loads(inventory_response.read(65537))
        if inventory_response.status != 200:
            raise RuntimeError('Model inventory unavailable')
    finally:
        inventory_client.close()
    candidate = [m for m in inventory['models'] if m['key'] == 'google/gemma-4-e4b' and m['loaded_instances']]
    if len(candidate) != 1 or len(candidate[0]['loaded_instances']) != 1 or candidate[0]['format'] != 'mlx':
        raise RuntimeError('Require exactly one already-loaded linked MLX E4B; no automatic loading')
    devices = subprocess.check_output([str(Path.home() / '.lmstudio/bin/lms.exe'), 'ps'], text=True)
    selected_lines = [line for line in devices.splitlines() if 'google/gemma-4-e4b' in line]
    if len(selected_lines) != 1 or 'Irwins-Mac-mini-2.local' not in selected_lines[0]:
        raise RuntimeError('Loaded E4B device differs from reviewed linked Mac')
    save(output / 'model-inventory-before.json', inventory)
    (output / 'model-devices.txt').write_text(devices, encoding='utf-8')
    build = 'editor-' + source if args.editor else exe.parent.name
    world_id = 'starfall.integrated-coastal.v1' if args.integrated else 'starfall.npc-courtyard.v1'
    initialize(output / 'private', world_id, [build], ['inhabitant-01', 'inhabitant-02'])
    config_path = output / 'private' / 'config.json'
    config = json.loads(config_path.read_text())
    with socket.socket() as port_socket:
        port_socket.bind(('127.0.0.1', 0))
        port = port_socket.getsockname()[1]
    client_path = output / 'private' / 'player-client.json'
    save(client_path, dict(world_id=config['world_id'], inhabitant_id='inhabitant-01', publisher_id=config['publisher_id'],
        build_id=build, memory_endpoint=f'http://127.0.0.1:{port}', publisher_token=config['publisher_token'], reader_token=config['inhabitants'][0]['token']))
    # The memory process receives a filtered environment and cannot initiate network connections.
    env = {k: v for k, v in os.environ.items() if k.upper() in ('SYSTEMROOT', 'WINDIR', 'TEMP', 'TMP', 'PATH')}
    flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
    service_log = (output / 'memory-process.log').open('w')
    service = subprocess.Popen([sys.executable, str(ROOT / 'services/starfall-memory/offline_guard.py'), '--config', str(config_path),
        '--data-dir', str(output / 'private/data'), '--port', str(port)], stdout=service_log, stderr=service_log, env=env, creationflags=flags)
    player = None
    settings = {ROOT / 'ProjectSettings' / name: (ROOT / 'ProjectSettings' / name).read_bytes()
                for name in ('GraphicsSettings.asset', 'QualitySettings.asset', 'ProjectSettings.asset')} if args.editor else {}
    def call(resource, actor=0):
        client = http.client.HTTPConnection('127.0.0.1', port, timeout=2)
        try:
            client.request('GET', resource, headers={'Authorization': 'Bearer ' + config['inhabitants'][actor]['token']})
            response = client.getresponse()
            return response.status, json.loads(response.read(16385))
        finally:
            client.close()
    try:
        for _ in range(40):
            if service.poll() is not None:
                raise RuntimeError('Isolated memory process exited')
            try:
                if call('/v1/health')[0] == 200:
                    break
            except OSError:
                time.sleep(.1)
        else:
            raise RuntimeError('Memory readiness timeout')
        command = [str(exe), '-batchmode', '-force-d3d11']
        if args.editor:
            command += ['-projectPath', str(ROOT), '-executeMethod', 'CityLife.World.Editor.StarfallMemoryPlayMode.Run',
                '-npcEditorRuntime', build, '-npcEditorScene', args.scene]
        if args.integrated:
            if args.editor: raise RuntimeError('Integrated runner currently requires the separate compiled player')
            command += ['-integratedSmoke', '-integratedEvidence', str(output / 'runtime')]
        else:
            command += ['-npcSmoke', '-npcEvidence', str(output / 'runtime')]
        command += ['-npcLivingMemory', '-npcMemoryClient', str(client_path),
            '-npcLocalEndpoint', 'http://127.0.0.1:1234', '-npcLocalModel', 'google/gemma-4-e4b',
            '-logFile', str(output / 'player.log')]
        content_before = build_content(exe.parent)
        launch = dict(build=build, sha256=hashlib.sha256(exe.read_bytes()).hexdigest(),
            memory='isolated SQLite HTTP service; no archive mounts', model='google/gemma-4-e4b', deadline_ms=1500,
            execution_host='Unity Editor Play Mode; not standalone acceptance' if args.editor else 'standalone', source_commit=source,
            scene=args.scene, scene_sha256=hashlib.sha256((ROOT / args.scene).read_bytes()).hexdigest() if args.editor else None,
            buildContentSchema=content_before['schema'], buildContentSha256=content_before['sha256'], buildContentFiles=content_before['files'],
            buildManifest=None if build_manifest is None else str(build_manifest.relative_to(ROOT)).replace('\\','/'))
        save(output / 'launch.json', launch)
        print(json.dumps({'checkpoint': 'memory-ready', 'build': build}), flush=True)
        runtime_env = env
        if args.editor:
            # Installed Editor licensing/UPM needs Windows profile paths; memory service remains restricted.
            runtime_env = {k: v for k, v in os.environ.items() if k.upper() in (
                'SYSTEMROOT', 'WINDIR', 'TEMP', 'TMP', 'PATH', 'USERPROFILE', 'APPDATA', 'LOCALAPPDATA',
                'PROGRAMDATA', 'ALLUSERSPROFILE', 'PROGRAMFILES', 'PROGRAMFILES(X86)', 'PROGRAMW6432',
                'HOMEDRIVE', 'HOMEPATH', 'USERNAME', 'USERDOMAIN', 'COMPUTERNAME', 'COMSPEC', 'PATHEXT')}
        player = subprocess.Popen(command, creationflags=flags, env=runtime_env, cwd=ROOT)
        try:
            exit_code = player.wait(timeout=600 if args.editor else 420 if args.integrated else 240)
        except subprocess.TimeoutExpired:
            player.kill(); player.wait(); raise RuntimeError('Player watchdog expired')
        content_after = build_content(exe.parent)
        launch['buildContentSha256AfterRun'] = content_after['sha256']
        launch['buildContentFilesAfterRun'] = content_after['files']
        launch['buildContentUnchangedAfterRun'] = content_after == content_before
        save(output / 'launch.json', launch)
        health_status, health = call('/v1/health')
        _, own = call('/v1/memories?limit=4')
        other_status, other = call('/v1/memories?limit=4', 1)
        cross_status, _ = call('/v1/memories?world_id=foreign')
        save(output / 'memory-http-evidence.json', dict(health=health, own=own, other_items=other.get('items'),
            health_status=health_status, other_status=other_status, foreign_query_status=cross_status))
        if health_status != 200 or other_status != 200 or other['items'] or cross_status != 400:
            raise RuntimeError('Memory isolation gate failed')
    finally:
        if player and player.poll() is None:
            player.kill(); player.wait()
        service.terminate(); service.wait(timeout=10); service_log.close()
        for path, original in settings.items():
            path.write_bytes(original)
        failed_report = output / 'runtime/integrated-runtime.json'
        if args.integrated and not args.editor and failed_report.is_file():
            if json.loads(failed_report.read_text(encoding='utf-8'))['status'] == 'FAIL':
                subprocess.run(['pwsh', '-NoProfile', '-File',
                    str(ROOT / 'tools/remove-failed-integrated-build.ps1'),
                    '-Manifest', str(build_manifest), '-FailedRuntimeDirectory', str(output),
                    '-Execute'], cwd=ROOT, check=True)
    # Reopen the real database after process shutdown to verify persisted chain and namespace.
    store = MemoryStore(output / 'private/data/starfall-memory.sqlite3', config['world_id'], config['publisher_id'], config['build_ids'])
    checkpoint = store.verify()
    own_restart = store.records('inhabitant-01', ('episode',), limit=4)
    assert store.records('inhabitant-02', ('episode',), limit=4)['items'] == []
    live_evidence = {ref for item in own.get('items', []) for ref in item.get('evidence_ids', [])}
    restarted_evidence = {ref for item in own_restart.get('items', []) for ref in item.get('evidence_ids', [])}
    assert own_restart['items'] and live_evidence and live_evidence == restarted_evidence
    store.close()
    save(output / 'restart-persistence.json', dict(checkpoint=checkpoint, own=own_restart, real_sqlite=True, service_stopped=True))
    print(json.dumps({'checkpoint': 'player-finished', 'exit_code': exit_code, 'persisted_events': checkpoint['events']}), flush=True)
    if exit_code:
        raise SystemExit(exit_code)
    if args.integrated and not args.editor:
        # Promote only after the compiled runtime, isolation and restart checks.
        # The retention helper independently rechecks every required runtime gate
        # and the complete build bytes; packaging is not required for cleanup.
        subprocess.check_call(['pwsh', '-NoProfile', '-File',
            str(ROOT / 'tools/retain-current-integrated-build.ps1'),
            '-BuildManifest', str(build_manifest), '-RuntimeDirectory', str(output),
            '-Execute'], cwd=ROOT)


if __name__ == '__main__':
    main()
