# Immediate E4B reflection latency, September 15

The real compiled immediate-reflection gate remains **1500 ms** including
inventory and completion. Round 212/215/216/221 changed candidates all timed
out around 1502–1503 ms with no raw response; their failed binaries were removed.
This is independent of normal survival action choices, which use a distinct
five-second Windows GGUF request and have genuine food/exploration evidence.

Off-game exact synthetic amber prompt sampled the loaded Mac MLX instance
through `127.0.0.1:1234`, not a real gameplay event. Bounded stage receipt
`evidence/local/model-bench/delivery-mac-stages-20260915-1423.json` has three
spaced attempts: total 3152/423/458 ms, all valid final `Delivered amber`.
Loopback connect was 3.5/0.8/21.1 ms; inventory body ended 15.4/11.6/32.2 ms.
The late sample spent roughly 3137 ms waiting for completion response headers;
after headers, body took under 0.1 ms. Therefore the loopback connect,
inventory, and body-transfer stages are not the main measured tail. Because
nonstreaming completion withholds headers until a result, this **does not**
distinguish local API queueing, linked-Mac transport, model execution, or
host scheduling. The three samples are not a reliability estimate.

Direct Mac API comparison is unavailable from this Windows host by the known
name: `Resolve-DnsName Irwins-Mac-mini-2.local` returns no address and a
three-second GET to `http://Irwins-Mac-mini-2.local:1234/api/v1/models`
returns `No such host is known`. The Windows LM Studio logs directory exists
but had no files at inspection. `lms log stream` can expose unrelated shared
model requests, so it was not dumped as server telemetry. No private endpoint,
secret, or linked-host queue log was obtained. Avoid attributing the tail to
power/cold state or rerolling identical builds; a host-side scoped timing
receipt or supported direct endpoint would be needed to isolate the stage.
