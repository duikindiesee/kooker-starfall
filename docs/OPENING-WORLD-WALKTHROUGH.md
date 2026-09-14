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
| Food, 20–25 seconds | Recognizable berry bush, gathering/eating, freshwater interaction | Show actual interaction and changed state; distinguish player/scripted interaction from autonomous food choice |
| Inhabitant, 20–30 seconds | Clothed inhabitant completes all three deliveries | Uninterrupted runtime evidence exists; edited highlights may shorten footage but must disclose that they are highlights |
| Memory and thought, 20–30 seconds | Remembered event identity and a real model response in the same world | Persisted event recovered after reload, correct world/actor scope, model admission within configured deadline |
| Controls and close, 15–20 seconds | Pause, possession/spectator, display settings; final vista | Tested controls and clear version label; state remaining limitations explicitly |

## Narration rules

- Explain what changed and why it matters to play. Use one narrator by default.
- Never describe deterministic scripted actions as learning or model-generated choices.
- A real thought should be described as model-generated interpretation grounded in a verified event; Unity remains responsible for actions.
- Do not describe service restart proof as a complete game-save implementation unless the combined game save/load flow also passed.
- Do not call the club accepted while its full validation fails. If deferred by the user, record that decision in the release limitations.
- Before/after comparisons must use clearly identified builds and equivalent viewpoints where possible.
- Do not include credentials, private service configuration, machine logs, or unrelated desktop windows in footage.
- Human visual/play approval remains a separate final requirement.

## Completion checklist

- [ ] Final candidate identified by source commit and executable hash.
- [ ] Actual player gates pass, including full delivery cycle and cave access.
- [ ] Food placement/readability and clothing reviewed in the player.
- [ ] Same-world memory reload and genuine model response verified.
- [ ] Footage captured and checked for black frames, cropping, unreadable HUD, and accidental desktop content.
- [ ] Narration recorded from evidence-backed final text and synchronized with the footage.
- [ ] MP4 replayed with audible narration and intact ending.
- [ ] Executable, ZIP, install guide, known gaps, and recording linked in the final handoff.
- [ ] User play/visual acceptance recorded.
