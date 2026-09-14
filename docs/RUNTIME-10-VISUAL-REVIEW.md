# Runtime-10 visual review

Status: failed visual acceptance, retained for diagnosis. Not a release receipt.

Evidence: `evidence/local/combined/runtime-10/01c-readable-berry-bush.png` and `integrated-runtime.json`.

The screenshot appeared to show a floating berry bush and torn terrain. Source inspection identifies a camera-placement problem rather than proven mesh corruption: the bush is at (138,4,-80), while its capture camera is (143,6.6,-86). The camera sits 23.77 metres from the activity terrace centre (120,-80), within the steep outer transition from the 18-metre flat terrace to the surrounding mesa. It can be below the terrain, exposing backface-culled surfaces and water. The mesh uses 32-bit indices; its 120,701 vertices do not imply index overflow.

Required repair and proof:

- Keep the resource and viewing approach sufficiently inside the accessible shelf.
- Ground-check the actual camera position and retain unobstructed collider line of sight to the bush.
- Raycast against the real terrain collider beneath the bush; do not certify placement using only the same analytic height formula used to create it.
- Inspect the replacement compiled-player screenshot and walk the approach in the actual player.
- Keep the existing screenshot; do not overwrite failed evidence.

Other findings: all three deliveries completed. Spectator camera visibly moved in reported coordinates, but the check failed on an 8-centimetre internal/camera discrepancy that requires frame-order diagnosis. Five principal screenshots were completely black and failed `tools/check-visual-evidence.py`. Neither numeric clearance nor a nonblank image alone proves readable food placement.
