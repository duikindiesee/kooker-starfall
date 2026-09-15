"""Bounded off-game exact delivery prompt comparison; never gameplay acceptance."""
import argparse
import http.client
import json
import time
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--models', nargs='+', default=['google/gemma-4-e4b', 'starfall-local-e4b'])
    parser.add_argument('--attempts', type=int, default=2)
    parser.add_argument('--gap-seconds', type=float, default=0)
    parser.add_argument('--max-tokens', type=int, default=8)
    args = parser.parse_args()
    if not 1 <= args.attempts <= 12 or not 0 <= args.gap_seconds <= 60 or args.max_tokens not in (8, 16, 24, 32):
        raise RuntimeError('Bounded attempts 1..12, gap 0..60 seconds, max tokens 8/16/24/32 required')
    if args.output.exists():
        raise RuntimeError('Refuse existing evidence output')
    rows = []
    for model in args.models:
        for attempt in range(args.attempts):
            if attempt and args.gap_seconds:
                time.sleep(args.gap_seconds)
            request = {'model': model, 'stream': False, 'temperature': 0, 'max_tokens': args.max_tokens, 'reasoning_effort': 'none', 'messages': [
                {'role': 'system', 'content': 'Output exactly two plain words and nothing else. First word Delivered. Second word is the delivered item named in verified memory. No quotes, braces, punctuation, explanation, goals, or commands.'},
                {'role': 'user', 'content': 'Verified delivery item: amber.'}]}
            row = {'model': model, 'attempt': attempt + 1, 'scope': 'off-game synthetic prompt; not a real event', 'request': request}
            connection = http.client.HTTPConnection('127.0.0.1', 1234, timeout=10)
            start = time.perf_counter()
            try:
                connection.connect()
                row['loopbackConnectMilliseconds'] = (time.perf_counter() - start) * 1000
                row['peer'] = '127.0.0.1:1234 (local LM Studio API; linked model may execute on another host)'
                connection.request('GET', '/api/v0/models')
                inventory_response = connection.getresponse()
                row['inventoryHeadersMilliseconds'] = (time.perf_counter() - start) * 1000
                inventory = json.loads(inventory_response.read())
                if inventory_response.status != 200 or not any(x.get('id') == model and x.get('state') == 'loaded' for x in inventory.get('data', [])):
                    raise RuntimeError('Exact instance not already loaded; no autoload')
                row['inventoryMilliseconds'] = (time.perf_counter() - start) * 1000
                connection.request('POST', '/v1/chat/completions', json.dumps(request), {'Content-Type': 'application/json'})
                row['completionRequestIssuedMilliseconds'] = (time.perf_counter() - start) * 1000
                response = connection.getresponse()
                row['completionHeadersMilliseconds'] = (time.perf_counter() - start) * 1000
                body = json.loads(response.read())
                row['milliseconds'] = (time.perf_counter() - start) * 1000
                row['completionBodyMilliseconds'] = row['milliseconds']
                row['response'] = body
                choice = body.get('choices', [{}])[0]
                text = choice.get('message', {}).get('content', '')
                row['valid'] = response.status == 200 and body.get('model') == model and choice.get('finish_reason') == 'stop' and text.strip().lower() == 'delivered amber'
                row['within1500ms'] = row['valid'] and row['milliseconds'] <= 1500
            except Exception as error:
                row.update(milliseconds=(time.perf_counter() - start) * 1000, valid=False, within1500ms=False, error=type(error).__name__)
            finally:
                connection.close()
            rows.append(row)
            print(json.dumps({k: v for k, v in row.items() if k not in ('request', 'response')}), flush=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({'scope': 'Off-game synthetic event; local loopback connection/header/body stages only, NOT linked-host server queue/inference telemetry. Token-budget variant is NOT game code; idle gap is deliberate; true cold state unknown; no deadline policy change', 'models': args.models, 'attempts': args.attempts, 'gapSeconds': args.gap_seconds, 'maxTokens': args.max_tokens, 'rows': rows}, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
