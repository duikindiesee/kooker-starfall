"""Package one already-built, verified character preview; never starts a player."""
import argparse
import hashlib
import html
import json
from pathlib import Path
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write_json(path, data):
    path.write_bytes((json.dumps(data, indent=2) + "\n").encode())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", required=True, type=Path)
    parser.add_argument("--runtime", required=True, type=Path)
    args = parser.parse_args()
    build = args.build.resolve()
    runtime = args.runtime.resolve()
    if not build.is_relative_to(ROOT / "Builds"):
        raise ValueError("Package only an existing build under this checkout.")
    manifest = json.loads((build / "BUILD-MANIFEST.json").read_text(encoding="utf-8-sig"))
    report = json.loads((runtime / "character-runtime.json").read_text())
    if manifest["status"] != "Succeeded" or report["status"] != "PASS":
        raise ValueError("Build and actual runtime verification must both pass.")
    if report["version"] != manifest["version"]:
        raise ValueError("Runtime and build versions differ.")
    output = ROOT / "Builds/Download" / (build.name + "-Windows.zip")
    output.parent.mkdir(parents=True, exist_ok=True)
    evidence = ROOT / "evidence/milestones/first-inhabitant" / build.name
    if output.exists() or evidence.exists():
        raise FileExistsError("Preserve the previous ZIP and evidence.")
    shutil.copytree(runtime, evidence)
    frames = sorted((evidence / "roam-frames").glob("*.png"))
    trace = report["observations"]
    rows = "".join("<tr><td>" + str(row["sequence"]) + "</td><td>" +
                   html.escape(row["observation"]) + "</td><td>" + html.escape(row["action"]) +
                   "</td><td>" + html.escape(row["outcome"]) + "</td></tr>" for row in trace)
    replay = """<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Starfall — First inhabitant replay</title>
<style>body{margin:0;background:#08111e;color:#ecf2f4;font:16px system-ui;max-width:1120px;margin:auto;padding:28px}
h1{font-size:28px;margin-bottom:8px}p{color:#b8cad4;line-height:1.6}img{width:100%;background:#111c28;border-radius:12px}
button{background:#84e2e2;color:#092630;border:0;border-radius:7px;padding:10px 18px;font:inherit;margin:12px 12px 12px 0;cursor:pointer}
input{width:60%;vertical-align:middle}table{width:100%;border-collapse:collapse;font-size:14px}td,th{padding:12px;border-bottom:1px solid #294253;text-align:left}
small{color:#b8cad4}a{color:#84e2e2}</style>
<h1>Starfall / First inhabitant</h1>
<p>Actual frames from the standalone Unity preview. An adult humanoid follows two authored goals, walks around the obstacle and delivers a crystal.
This is a recorded scripted demonstration. It contains no learned behavior. The wider Starfall landscape is a separate milestone.</p>
<img id="frame" alt="Actual recorded character expedition" src="roam-frames/frame-0000.png">
<div><button id="play">Play replay</button><button id="reset">Start again</button>
<input id="seek" aria-label="Replay frame" type="range" min="0" value="0"><small id="position"></small></div>
<p>Captured every six simulation frames; replayed at five frames per second. Timing is simulation time, not a native performance benchmark.
<a href="character-runtime.json">Inspect the runtime checks</a>.</p>
<h2>Observed sequence</h2><table><thead><tr><th>Step</th><th>Observation</th><th>Action</th><th>Outcome</th></tr></thead><tbody>ROWS</tbody></table>
<script>
const files=FILES;
const image=document.getElementById('frame'),seek=document.getElementById('seek'),play=document.getElementById('play'),position=document.getElementById('position');
let current=0,timer=null;seek.max=files.length-1;
function show(i){current=Math.max(0,Math.min(files.length-1,i));image.src=files[current];seek.value=current;position.textContent=' '+(current+1)+' / '+files.length}
function stop(){clearInterval(timer);timer=null;play.textContent='Play replay'}
play.onclick=()=>{if(timer){stop();return}if(current===files.length-1)show(0);play.textContent='Pause';timer=setInterval(()=>{if(current>=files.length-1)stop();else show(current+1)},200)};
document.getElementById('reset').onclick=()=>{stop();show(0)};
seek.oninput=()=>{stop();show(Number(seek.value))};image.onerror=()=>{stop();position.textContent='Frame unavailable — keep the roam-frames folder beside this file.'};show(0);
</script></html>"""
    replay = replay.replace("ROWS", rows).replace("FILES", json.dumps(["roam-frames/" + p.name for p in frames]))
    (evidence / "replay.html").write_bytes(replay.encode())
    readme = """Kooker Starfall — First inhabitant, 0.0.3-preview.1

Extract the entire folder and open KookerStarfallCharacter.exe. Keep its Data,
MonoBleedingEdge and DLL files beside the executable.

One short scripted expedition starts automatically: walk around the obstacle,
take a crystal, carry it and place it on the second plinth.
WASD takes manual control. Hold right mouse to turn the camera; wheel zooms.
R pauses/resumes an unfinished expedition. Escape stops it and releases the
pointer. Close the window to exit. Restart the preview to replay a completed trip.

The actual player passed automated idle/walk, deformed-mesh grounding, movement,
gravity, wall collision, camera obstruction and scripted delivery checks.
Native keyboard/mouse use and sustained performance remain unverified.

This is a separate character test courtyard. It does not load or change a saved
world, connect to a bot or implement learning. R06/R19 releases are preserved.
Stone Age clothing, hand IK, independent aiming, ragdoll and further world work
are outside this preview. No paid asset tier was acquired.

See BUILD-MANIFEST.json for the game source and local asset-library revision,
CHARACTER-RUNTIME.json for runtime evidence, and THIRD-PARTY-NOTICES.txt.
"""
    (build / "README.txt").write_bytes(readme.encode())
    art = ROOT / "Assets/CityLife/Characters/QuaterniusStandard"
    notices = "Quaternius free Standard characters and animations — CC0-1.0\n\n"
    notices += "https://quaternius.itch.io/universal-base-characters\nhttps://quaternius.itch.io/universal-animation-library\n"
    notices += "Models: Quaternius. Animation collaboration: Gonzalo Furnier.\n\n"
    notices += (art / "BASE-LICENSE.txt").read_text() + "\n" + (art / "ANIMATION-LICENSE.txt").read_text()
    notices += "\nOther project notices retained for provenance:\n" + (ROOT / "Assets/CityLife/Art/THIRD-PARTY-NOTICES.txt").read_text(encoding="utf-8-sig")
    (build / "THIRD-PARTY-NOTICES.txt").write_bytes(notices.encode())
    shutil.copy2(runtime / "character-runtime.json", build / "CHARACTER-RUNTIME.json")
    entries = []
    with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(build.rglob("*")):
            if not path.is_file() or any("BackUpThisFolder_ButDontShipItWithYourGame" in part for part in path.parts):
                continue
            if path.suffix.lower() in {".log", ".pdb"}:
                continue
            name = path.relative_to(build).as_posix()
            archive.write(path, name)
            entries.append(dict(path=name, bytes=path.stat().st_size, sha256=sha(path)))
    package = dict(schema="citylife.unity.package-evidence.v1", archive=output.name,
                   archiveBytes=output.stat().st_size, sha256=sha(output), verifiedEntryCount=len(entries), entries=entries)
    write_json(Path(str(output) + ".manifest.json"), package)
    release = dict(build=manifest, archive=output.relative_to(ROOT).as_posix(), archiveBytes=output.stat().st_size,
                   archiveSha256=package["sha256"], runtimeChecks=len(report["checks"]), runtimeStatus=report["status"],
                   runtimeEvidence=(evidence / "character-runtime.json").relative_to(ROOT).as_posix(),
                   replay=(evidence / "replay.html").relative_to(ROOT).as_posix(), recordedFrames=len(frames),
                   nativeInput="unverified; no desktop input performed", publication="local only; no push or merge")
    write_json(ROOT / "evidence/verified/first-inhabitant-release.json", release)
    print(json.dumps(release, indent=2))


if __name__ == "__main__":
    main()
