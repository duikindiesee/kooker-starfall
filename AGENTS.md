# CityLife Unity

## Integrated build retention (user decision, 15 September 2026)

Keep one current verified integrated build, not an archive of executable copies.
A temporary replacement may coexist during build and validation. On explicit
build failure, remove only that failed output; keep the current verified player.
After replacement runtime/content checks pass, promote it and remove superseded
integrated binaries and redundant ZIPs using the validated retention workflow.
Protect active processes, source, user saves and compact logs/provenance/screenshots.
Historical instructions to preserve previous build folders are superseded for
these integrated binaries; retain their evidence instead. See
`docs/BUILD-RETENTION.md`. Packaging/promotion is not permission to merge, release
or bypass protected review. Keep the real Task API owning record aligned; do not
invent an epic/task/workstream ID or claim an unverified queue receipt.

Storage reconciliation is separate from survival promotion: an open spring or
visual gate must not retain every superseded test binary. Keep an explicitly
identified best verified fallback and the one current candidate, protect live
processes, and remove only manifest-bound superseded outputs after path/content
checks. Record counts and free space before/after. This does not promote the
candidate or weaken any acceptance verifier.

Read `README.md` and `docs/WORLD-FOUNDATION.md` before changing the world. The current milestone is an explorable island foundation; road, plot, house-tool, shared-world and household integrations are separate future work.

- Preserve the versioned world definition and explicit migration boundary. Never silently regenerate a saved world using a changed algorithm/configuration.
- Terrain base and edits remain separate. Validate an entire edit candidate before applying it. Source browser saves are not Unity edit files.
- Determinism must not depend on render frames, UnityEngine.Random, clock time or chunk travel order.
- Keep rendering/input independent of the world definition and base field. Use source references for concepts, not as evidence that proposed integrations work.
- Build with the pinned Unity editor and run `CityLife.World.Editor.IslandValidation.Run` (also part of `CityLifeBuild.BuildWindows`). Retain actual player screenshots and timings for player-visible changes.
- Update documentation and `docs/VISUAL-MILESTONES.md` with meaningful visual milestones. Images must be real source/Unity observations, clearly labelled; preserve earlier captures.
- Maintain `docs/SYSTEM-ARCHITECTURE.md` with component, integration, capability/storage and installation changes. Update its Mermaid diagram and installation-state table with exact evidence; distinguish tested components, connections not live-wired, planned work and unverified outcomes. A configuration or package alone does not prove installation or runtime acceptance.
- This is a public repository. Never commit credentials, private runtime profiles, player saves, private archives, or raw local logs containing machine paths. Reviewed synthetic evidence and explicit source provenance are appropriate.
- Use branches and pull requests for implementation. `main` requires review. Do not deploy or alter the existing browser service as part of a Unity change.
