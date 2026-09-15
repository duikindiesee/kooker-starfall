# Opening-world walkthrough delivery

Status: capture plan, not a completed recording. Final narration must be rewritten against the accepted candidate's actual evidence before recording.

## Deliverable

A roughly two-to-three-minute narrated MP4 showing the earlier component scene and the accepted opening-world candidate. Use actual Unity footage and readable in-game evidence. Label any build-time stills separately. Keep the final executable version, source commit, runtime report, ZIP checksum, and recording manifest linked from the release receipt.

## Shot order and evidence gates

| Segment | What the viewer should see | Required evidence before narration claims success |
|---|---|---|
| Before, 10–15 seconds | Preserved small one-tree coastal scene | Identify the historical build; explain its limited size and the user-reported blue-object stall |
| Arrival, 20–30 seconds | Eye-level canyon opening, river flowing toward sea, Kookerboom, blue giant and moons | Capture from the final player; visibly show the intended composition and movement scale |
| Exploration, 20–30 seconds | Walk from accessible terrain into the cave; show shelter interior | Real continuous traversal, collisions, visible entrance, no buried geometry or camera teleport presented as walking |
| Food, 20–25 seconds | Recognizable sourfig-inspired fruit plant, gathering/eating, freshwater interaction | Show actual interaction and changed state; distinguish player/scripted interaction from autonomous food choice; do not imply real-world edibility |
| Inhabitant, 20–30 seconds | Clothed inhabitant completes all three deliveries | Uninterrupted runtime evidence exists; edited highlights may shorten footage but must disclose that they are highlights |
| Memory and thought, 20–30 seconds | Remembered event identity and a real model response in the same world | Persisted event recovered after reload, correct world/actor scope, model admission within configured deadline |
| Controls and close, 15–20 seconds | Pause, possession/spectator, display settings; final vista | Tested controls and clear version label; state remaining limitations explicitly |

## Narration rules

- Explain what changed and why it matters to play. Use one narrator by default.
- Never describe deterministic scripted actions as learning or model-generated choices.
- Describe the demonstrated `Delivered amber` response as a model-generated,
  constrained event acknowledgement. The prompt explicitly requires those two
  words with the verified item substituted; it does not demonstrate an open-ended
  interpretation, planning, learning or conversational mind. Unity remains
  responsible for actions. Only narrate broader capability after separate proof.
- Do not describe service restart proof as a complete game-save implementation unless the combined game save/load flow also passed.
- Do not call the club accepted while its full validation fails. If deferred by the user, record that decision in the release limitations.
- Before/after comparisons must use clearly identified builds and equivalent viewpoints where possible.
- Do not include credentials, private service configuration, machine logs, or unrelated desktop windows in footage.
- Human visual/play approval remains a separate final requirement.

## Sustained-operation evidence boundary

The existing eight-second post-delivery dwell is a short regression check, not
proof of sustained autonomy. Before final acceptance, retain a bounded normal-play
soak of at least three minutes with periodic timestamps, brain ticks, phase,
failure count, held item and delivery count, plus visible control responsiveness.
Check the intervening samples, not just the first and last state. Preserve the
build identity and logs alongside that observation. Do not manufacture additional
deliveries or seed tasks to make an idle character appear busy: after the finite
three-item job, healthy idle is expected. This test establishes continued operation,
not autonomous foraging, learning or survival planning.

## Completion checklist

### Local assembly tooling (tested fixture, not a delivered walkthrough)

`tools/assemble-walkthrough.py` accepts explicit local `--ffmpeg`, `--video`,
`--narration`, `--output`, `--build-id` and `--source-commit` arguments. It copies
video frames unchanged and replaces original audio with reviewed narration,
padding only to the finite measured footage length. Narration longer than the
footage is rejected rather than truncated. Existing outputs are never replaced.
It hashes inputs/output and performs a complete audio/video decode; audible
replay, visual inspection, factual narration and footage/build identity still
require independent checks.

On 15 September the existing local FFmpeg7.1 encoder was verified without any
download. A three-second synthetic test-video plus one-second tone assembled to
exactly three seconds and fully decoded; a four-second narration was rejected.
An initial unbounded-padding experiment hung and was stopped at its exact owned
PID; the finite-padding repair passed. Test artifacts live under ignored
`evidence/local/walkthrough-tool-test-20260915/`. None is Starfall gameplay.

### Final delivery gates

- [ ] Final candidate identified by source commit and executable hash.
- [ ] Actual player gates pass, including full delivery cycle and cave access.
- [ ] Food placement/readability and clothing reviewed in the player.
- [ ] Same-world memory reload and genuine model response verified.
- [ ] Footage captured and checked for black frames, cropping, unreadable HUD, and accidental desktop content.
- [ ] Narration recorded from evidence-backed final text and synchronized with the footage.
- [ ] MP4 replayed with audible narration and intact ending.
- [ ] Executable, ZIP, install guide, known gaps, and recording linked in the final handoff.
- [ ] User play/visual acceptance recorded.
# Game-only frame encoding validation (15 September 2026)

`tools/encode-game-frames.py` accepts successful PNG samples plus measured
wall-clock timestamps, never desktop pixels. A five-frame synthetic sequence at
0, 120, 400, 1100 and 2300 ms with capture end 3000 ms exercised irregular timing.
The first variable-frame-rate output fully decoded but reported an incorrect
0.84-second duration: its initial decode-only receipt is rejected. Full decode
alone therefore does not establish correct presentation timing.

The corrected encoder samples the measured timeline at 30 fps, explicitly holding
previous frames across gaps without generating interpolated motion. The same
fixture now reports and fully decodes as 3.00 seconds. A duration discrepancy
greater than 0.05 seconds prevents a success receipt. Ordered-index validation
also rejected duplicate timestamps. Synthetic files remain local under
`evidence/local/walkthrough-tool-test-20260915`; they are not gameplay evidence.
Actual game capture, provenance, dropped-frame review and narrated replay remain
required before delivering the walkthrough.
