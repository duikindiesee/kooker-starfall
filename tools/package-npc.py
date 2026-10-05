"""Package an already verified NPC/hybrid player without launching or replacing a release."""
import argparse
import hashlib
import html
import json
from pathlib import Path
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--runtime', type=Path, required=True)
    args = parser.parse_args()
    build, runtime = args.build.resolve(), args.runtime.resolve()
    if not build.is_relative_to(ROOT / 'Builds'):
        raise ValueError('Build must be under this checkout.')
    manifest = json.loads((build / 'BUILD-MANIFEST.json').read_text(encoding='utf-8-sig'))
    report = json.loads((runtime / 'npc-runtime.json').read_text())
    if manifest['status'] != 'Succeeded' or report['status'] != 'PASS' or not all(x['passed'] for x in report['checks']):
        raise ValueError('Successful build and complete actual-player acceptance are required.')
    if report['version'] != manifest['version'] or report['buildId'] != manifest['buildId'] or build.name != manifest['buildId']:
        raise ValueError('Exact build identity must match runtime evidence.')
    output = ROOT / 'Builds/Download' / (build.name + '-Windows.zip')
    evidence = ROOT / 'evidence/milestones/hybrid-npc' / build.name
    if output.exists() or evidence.exists():
        raise FileExistsError('Preserve earlier release and evidence.')
    output.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(runtime, evidence)
    pictures = report['captures']
    figures = ''.join('<figure><a href="' + html.escape(name, quote=True) + '"><img loading="lazy" src="' +
                      html.escape(name, quote=True) + '" alt="Actual Unity player checkpoint"></a><figcaption>' +
                      html.escape(name.removesuffix('.png').replace('-', ' ')) + '</figcaption></figure>' for name in pictures)
    rows = ''.join('<tr><td>' + html.escape(x['name']) + '</td><td>PASS</td><td>' + html.escape(x['evidence']) + '</td><td>This build only; native physical input and performance remain separate.</td></tr>' for x in report['checks'])
    review = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>Starfall / Optional local thoughts</title><style>body{max-width:1200px;margin:0 auto;padding:28px;background:#071521;color:#e6f2f3;font:16px system-ui;line-height:1.5}a{color:#88dfdf}figure{margin:30px 0}img{width:100%;border-radius:8px}table{border-collapse:collapse;width:100%;font-size:14px}td{border-bottom:1px solid #345;padding:10px}figcaption{color:#adcad2}</style>
<h1>Starfall / Optional local thoughts</h1><p>Actual standalone Unity player observations. Optional model proposals can select high-level goals, offer a short advisory plan and generate fictional dialogue/reflection. Deterministic physics, perception, navigation, permissions and action APIs remain in charge. No learning is implemented.</p>
<p>Acceptance uses a labelled fake provider and an unavailable local HTTP endpoint. A separate real-inference record, when present, identifies its actual outcome. These captures prove Unity Input System handlers, not physical keyboard/mouse routing or sustained performance.</p>
<p><a href="npc-runtime.json">Full runtime evidence and proposal audit</a> · <a href="real-local-probe.json">Real local probe status</a></p>''' + figures + '<table><tr><th>Claim</th><th>Status</th><th>Evidence</th><th>Remaining gap</th></tr>' + rows + '</table></html>'
    (evidence / 'review.html').write_text(review, encoding='utf-8')
    executable = Path(manifest['output']).name
    readme = f'''Kooker Starfall / Optional local thoughts / {manifest['version']}

Extract the whole folder, then run {executable}. Keep all DLL/Data folders beside it.
Autonomous NPC starts with deterministic rules. Local thoughts are OFF by default.

P: pause/options and resume. Escape: submenu back, root resume, outside menu pause.
Tab: explicitly possess/release the SAME NPC. Movement never selects possession.
F: follow/free spectator. WASD or arrows: move possessed NPC or spectator camera.
Spectator Q/E: down/up; Shift: faster. Hold right mouse: look. R: autonomy pause/resume.
L: detailed decisions and optional thoughts. F11: fullscreen/windowed shortcut.
Options > Graphics retains the display button. Display changes preserve current state.

Optional local setup (no credentials): launch with -npcLocalEndpoint http://127.0.0.1:1234
and -npcLocalModel followed by the explicit already-loaded LM Studio identifier.
Then P > Controls > Local thoughts > Turn local thoughts on. Configuration alone
does not enable requests. No model is chosen, loaded, unloaded or downloaded by this app.
Only bounded local proposals are allowed; all world actions use deterministic APIs.
Timeout/unavailable turns thoughts off until explicit retry. Default deadline 1500ms,
at most 12 requests per session. Pause, possession and display changes cancel requests.
Plan and reflection are generated advisory text, not learned behavior or executable code.

This is a separate test courtyard. It does not change world saves or earlier releases.
The larger desert/galaxy/living-sea environment remains a separate world milestone.
See BUILD-MANIFEST.json, NPC-RUNTIME.json and THIRD-PARTY-NOTICES.txt.
Actual Input System acceptance is recorded; native physical input and performance
remain separate acceptance work. This local package has not been published.
'''
    (build / 'README.txt').write_text(readme, encoding='utf-8')
    art = ROOT / 'Assets/CityLife/Characters/QuaterniusStandard'
    notices = 'Quaternius free Standard characters/animations; CC0-1.0. Models: Quaternius. Animation collaboration: Gonzalo Furnier.\n'
    notices += 'https://quaternius.itch.io/universal-base-characters\nhttps://quaternius.itch.io/universal-animation-library\n\n'
    notices += (art / 'BASE-LICENSE.txt').read_text() + '\n' + (art / 'ANIMATION-LICENSE.txt').read_text()
    notices += '\nOther project provenance:\n' + (ROOT / 'Assets/CityLife/Art/THIRD-PARTY-NOTICES.txt').read_text(encoding='utf-8-sig')
    (build / 'THIRD-PARTY-NOTICES.txt').write_text(notices, encoding='utf-8')
    shutil.copy2(runtime / 'npc-runtime.json', build / 'NPC-RUNTIME.json')
    entries = []
    with zipfile.ZipFile(output, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(build.rglob('*')):
            if not path.is_file() or any('BackUpThisFolder_ButDontShipItWithYourGame' in p for p in path.parts) or path.suffix.lower() in {'.log', '.pdb'}:
                continue
            name = path.relative_to(build).as_posix()
            archive.write(path, name)
            entries.append(dict(path=name, bytes=path.stat().st_size, sha256=sha(path)))
    package = dict(schema='citylife.unity.package-evidence.v1', archive=output.name, archiveBytes=output.stat().st_size,
                   sha256=sha(output), verifiedEntryCount=len(entries), entries=entries)
    write_json(Path(str(output) + '.manifest.json'), package)
    release = dict(build=manifest, archive=output.relative_to(ROOT).as_posix(), archiveBytes=output.stat().st_size,
                   archiveSha256=package['sha256'], runtimeChecks=len(report['checks']), runtimeStatus=report['status'],
                   evidence=evidence.relative_to(ROOT).as_posix(), review=(evidence / 'review.html').relative_to(ROOT).as_posix(),
                   nativeInput='Unity input handlers passed; physical keyboard/mouse routing unverified', publication='local only')
    write_json(ROOT / 'evidence/verified/hybrid-npc-release.json', release)
    print(json.dumps(release, indent=2))


if __name__ == '__main__':
    main()
