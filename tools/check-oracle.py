#!/usr/bin/env python3
"""Compare all source-oracle values while explicitly validating LF/CRLF provenance."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import subprocess

def compare(expected, actual, source):
    expected = copy.deepcopy(expected)
    actual = copy.deepcopy(actual)
    if expected.get('sourceCommit') != actual.get('sourceCommit'):
        raise ValueError('Oracle source commit changed')
    if len(expected['sourceFiles']) != len(actual['sourceFiles']):
        raise ValueError('Oracle source file inventory changed')
    for left, right in zip(expected['sourceFiles'], actual['sourceFiles']):
        if left['path'] != right['path'] or left['gitBlob'] != right['gitBlob']:
            raise ValueError('Source oracle Git blob provenance changed')
        raw = subprocess.check_output(['git', '-C', str(source), 'show', expected['sourceCommit'] + ':' + left['path']])
        # These source files are UTF-8 text. Canonical Git blobs stay authoritative; checkout
        # conversion may produce CRLF on Windows or LF on GitHub's Linux runner.
        normalized = raw.replace(b'\r\n', b'\n')
        allowed = {hashlib.sha256(raw).hexdigest(), hashlib.sha256(normalized).hexdigest(), hashlib.sha256(normalized.replace(b'\n', b'\r\n')).hexdigest()}
        if left['sha256'] not in allowed or right['sha256'] not in allowed:
            raise ValueError('Source file checksum is not the recorded Git blob with allowed line endings: ' + left['path'])
        del left['sha256']
        del right['sha256']
    if expected != actual:
        raise ValueError('Source oracle values/settings/full-field hashes differ; regenerate and review the world contract deliberately')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--actual', required=True, type=Path)
    parser.add_argument('--expected', type=Path, default=Path(__file__).resolve().parents[1] / 'Assets/CityLife/Resources/SourceVectors.json')
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    expected = json.loads(args.expected.read_text(encoding='utf-8-sig'))
    actual = json.loads(args.actual.read_text(encoding='utf-8-sig'))
    compare(expected, actual, args.source)
    if args.self_test:
        for mutation in ('height', 'provenance'):
            bad = copy.deepcopy(actual)
            if mutation == 'height':
                bad['terrainVectors'][0]['height'] += 1
            else:
                bad['sourceFiles'][0]['sha256'] = '0' * 64
            try:
                compare(expected, bad, args.source)
            except ValueError:
                continue
            raise AssertionError('Oracle tampering canary accepted: ' + mutation)
    print('Original-source oracle passed: all numerical values, settings and field hashes match; source Git blobs and LF/CRLF file hashes verified. C# execution is a separate licensed Unity check.')

if __name__ == '__main__':
    main()
