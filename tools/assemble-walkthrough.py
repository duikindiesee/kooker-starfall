"""Mux reviewed narration onto continuous footage without truncating its ending.

This prepares a local artifact; successful decode is not visual/audio acceptance.
The encoder is supplied explicitly and is never downloaded by this script.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess


def digest(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def inspect_media(encoder, path):
    probe = subprocess.run([str(encoder), '-hide_banner', '-i', str(path)],
                           capture_output=True, text=True, timeout=30)
    # ffmpeg inspection intentionally has no output, so its exit code is not
    # a decode verdict. The final file receives a separate complete decode.
    match = re.search(r'Duration: (\d+):(\d+):(\d+(?:\.\d+)?)', probe.stderr)
    if not match:
        raise ValueError('A finite readable media duration is required.')
    hours, minutes, seconds = map(float, match.groups())
    return dict(seconds=hours * 3600 + minutes * 60 + seconds,
                video=bool(re.search(r'Stream .*Video:', probe.stderr)),
                audio=bool(re.search(r'Stream .*Audio:', probe.stderr)))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('ffmpeg', 'video', 'narration', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--build-id', required=True)
    parser.add_argument('--source-commit', required=True)
    args = parser.parse_args()
    if not re.fullmatch(r'[0-9a-f]{40}', args.source_commit):
        raise ValueError('Exact source commit required.')
    if not re.fullmatch(r'KookerStarfallIntegrated-[A-Za-z0-9.-]+', args.build_id):
        raise ValueError('Exact integrated build identifier required.')
    for path in (args.ffmpeg, args.video, args.narration):
        if path.is_symlink() or not path.is_file():
            raise ValueError('Inputs must be existing ordinary local files.')
    output = args.output.absolute()
    receipt = output.with_suffix(output.suffix + '.json')
    if output.suffix.lower() != '.mp4' or not output.parent.is_dir():
        raise ValueError('Choose an MP4 in an existing output directory.')
    if output.exists() or receipt.exists():
        raise ValueError('Existing footage/evidence must not be overwritten.')
    video = inspect_media(args.ffmpeg, args.video)
    voice = inspect_media(args.ffmpeg, args.narration)
    if not video['video'] or not voice['audio']:
        raise ValueError('Video stream and narration audio stream required.')
    if voice['seconds'] > video['seconds'] + .02:
        raise ValueError('Narration is longer than footage; revise timing, do not cut the ending.')
    before = {name: digest(path) for name, path in (
        ('video', args.video), ('narration', args.narration), ('encoder', args.ffmpeg))}
    subprocess.run([str(args.ffmpeg), '-hide_banner', '-v', 'error', '-n',
        '-i', str(args.video), '-i', str(args.narration), '-map', '0:v:0', '-map', '1:a:0',
        '-c:v', 'copy', '-c:a', 'aac', '-b:a', '160k', '-af',
        'apad=whole_dur=' + str(video['seconds']), '-t', str(video['seconds']),
        '-movflags', '+faststart', str(output)], check=True, timeout=600)
    final = inspect_media(args.ffmpeg, output)
    if not final['video'] or not final['audio'] or abs(final['seconds'] - video['seconds']) > .15:
        raise ValueError('Output duration/streams mismatch; preserve failed artifact for inspection.')
    subprocess.run([str(args.ffmpeg), '-v', 'error', '-i', str(output),
                    '-map', '0:v:0', '-map', '0:a:0', '-f', 'null', '-'],
                   check=True, capture_output=True, timeout=600)
    if before['video'] != digest(args.video) or before['narration'] != digest(args.narration):
        raise ValueError('Source media changed during assembly.')
    report = dict(schema='starfall.walkthrough.assembly.v1',
        status='MUX_AND_FULL_DECODE_PASS_REPLAY_PENDING', build=args.build_id,
        sourceCommit=args.source_commit, sourceHashes=before, outputSha256=digest(output),
        footageSeconds=video['seconds'], narrationSeconds=voice['seconds'],
        outputSeconds=final['seconds'], originalFootageAudioRetained=False,
        boundary='Reviewed narration replaces original audio; video frames copied unchanged. '
                 'Does not prove footage build identity, readable HUD, accurate narration, '
                 'audible playback or human acceptance. Check these separately.')
    receipt.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
