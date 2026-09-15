"""Encode private game-only PNG captures at their measured wall-clock timing.

No desktop capture, frame generation, time acceleration, or publication occurs.
Full decode is technical evidence only; review the resulting footage separately.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import subprocess
import tempfile


def number(value):
    if type(value) not in (int, float) or not math.isfinite(value):
        raise ValueError('Finite JSON number required')
    return value


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def validate(index, end):
    if index.is_symlink() or not index.is_file():
        raise ValueError('Regular frame index required')
    rows = [json.loads(line) for line in index.read_text().splitlines() if line.strip()]
    if len(rows) < 2:
        raise ValueError('At least two measured frames required')
    previous_id, previous_time, identity = 0, -1, None
    frames = []
    for row in rows:
        frame = row['frame']
        elapsed = number(row['utcElapsedMs'])
        number(row['unityTick'])
        if type(frame) is not int or frame <= previous_id or elapsed <= previous_time:
            raise ValueError('Frame IDs and measured timestamps must increase')
        if elapsed < 0 or row['readbackStatus'] != 'ok':
            raise ValueError('Only successful nonnegative-time captures are encodable')
        dimensions = (row['width'], row['height'], row['sourceCamera'])
        if any(type(v) is not int or v <= 0 or v % 2 for v in dimensions[:2]):
            raise ValueError('Positive even dimensions required for yuv420p')
        if not isinstance(dimensions[2], str) or not dimensions[2]:
            raise ValueError('Source camera required')
        if identity is not None and dimensions != identity:
            raise ValueError('Capture dimensions/camera changed')
        identity = dimensions
        path = index.parent / f'frame-{frame:06d}.png'
        if path.is_symlink() or not path.is_file() or path.resolve().parent != index.parent.resolve():
            raise ValueError('Missing or redirected frame')
        frames.append((path, elapsed, sha(path)))
        previous_id, previous_time = frame, elapsed
    if number(end) <= previous_time:
        raise ValueError('Capture end must follow the last measured frame')
    return frames


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--frames', type=Path, required=True)
    parser.add_argument('--capture-end-ms', type=float, required=True)
    parser.add_argument('--ffmpeg', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    frames = validate(args.frames, args.capture_end_ms)
    receipt = args.output.with_suffix('.capture.json')
    if args.output.exists() or receipt.exists() or not args.ffmpeg.is_file():
        raise ValueError('Existing output/receipt or missing explicit encoder')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    duration = (args.capture_end_ms - frames[0][1]) / 1000
    index_hash = sha(args.frames)
    # FFconcat has its own escaping, independent of shell syntax. No shell runs.
    def quote(path):
        return "'" + path.resolve().as_posix().replace("'", "'\\''") + "'"
    lines = ['ffconcat version 1.0']
    for i, (path, elapsed, _) in enumerate(frames):
        following = frames[i + 1][1] if i + 1 < len(frames) else args.capture_end_ms
        lines += ['file ' + quote(path), f'duration {(following-elapsed)/1000:.9f}']
    lines.append('file ' + quote(frames[-1][0]))
    with tempfile.TemporaryDirectory(prefix='starfall-game-frames-') as temporary:
        listing = Path(temporary) / 'frames.ffconcat'
        listing.write_text('\n'.join(lines) + '\n', encoding='utf-8')
        subprocess.run([str(args.ffmpeg), '-hide_banner', '-loglevel', 'error', '-n',
                        '-f', 'concat', '-safe', '0', '-i', str(listing),
                        '-fps_mode', 'vfr', '-c:v', 'libx264', '-pix_fmt', 'yuv420p',
                        '-t', str(duration), str(args.output)], check=True, timeout=600)
    subprocess.run([str(args.ffmpeg), '-hide_banner', '-loglevel', 'error', '-xerror',
                    '-i', str(args.output), '-f', 'null', '-'], check=True, timeout=600)
    if sha(args.frames) != index_hash or any(sha(p) != h for p, _, h in frames):
        raise ValueError('Capture inputs changed during encoding')
    result = dict(status='GAME_FRAME_ENCODE_DECODE_PASS_VISUAL_REVIEW_PENDING',
                  frameCount=len(frames), measuredDurationSeconds=duration,
                  timing='Variable frame durations from measured wall-clock timestamps; gaps hold prior frame',
                  indexSha256=index_hash, outputSha256=sha(args.output),
                  boundary='Does not independently prove game provenance, absence of frame drops, or visual acceptance')
    receipt.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
