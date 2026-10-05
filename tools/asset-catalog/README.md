# Starfall / CityLife Local Asset Catalog

Local, reproducible asset catalog slice for CityLife/Starfall source assets beneath `Assets/CityLife`. Includes a standalone reproducible inventory generator, offline browser frontend, bounded loopback HTTP server, and zero-dependency WebGL 3D inspection.

## Quick Start & Viewing Modes

### 1. Standalone Offline Viewing (Zero-Server / Sibling File Dependent)
Offline artifact support is **source complete**. Note that direct automated visual review was blocked by browser execution policy and restricted server launch permissions.

To view the catalog without running an HTTP server or background process:
- Open `tools/asset-catalog/catalog-offline.html` directly in any standard desktop browser via `file://`.

**Important Sibling Dependency Notice**:
`catalog-offline.html` is **not** a single self-contained monolithic HTML file. While it embeds the complete JSON catalog payload and the derived 3D reference mesh inline (bypassing browser `fetch()` CORS blocks under `file://`), it depends on relative sibling assets in the repository tree:
- Sibling stylesheet: `style.css` (in same directory)
- Sibling scripts: `viewer.js` and `app.js` (in same directory)
- Relative source textures, images, and model assets: `../../Assets/CityLife/...`
- Relative milestone evidence captures: `../../evidence/milestones/...`

Moving `catalog-offline.html` outside `tools/asset-catalog/` will break these relative sibling references.

### 2. Optional Local Loopback Server (Hosted Viewing)
To view via HTTP using the standard `index.html` interface:
Start the local server bound strictly to loopback (`127.0.0.1`):
```bash
python tools/asset-catalog/serve.py --port 8080
```
Then navigate in your browser to:
[http://127.0.0.1:8080/tools/asset-catalog/index.html](http://127.0.0.1:8080/tools/asset-catalog/index.html)

Alternatively, standard Python server can be bound strictly to loopback:
```bash
python -m http.server 8080 --bind 127.0.0.1
```

### 3. Generating or Refreshing the Catalog
Run the generator script using standard Python 3 (no external pip dependencies):
```bash
python tools/asset-catalog/generate-catalog.py --commit <commit-40-hex>
```
Optional flags:
- `--dirty`: Marks working tree as containing uncommitted changes.
- `--output <path>`: Specifies output JSON manifest (defaults to `tools/asset-catalog/asset-catalog.json`).

The generator scans `Assets/CityLife`, computes exact SHA256 hashes and file lengths, associates candidate provenance records, outputs `asset-catalog.json`, and automatically emits `catalog-offline.html` beside `index.html`.

---

## Security and Boundary Safeguards

The local serving script `tools/asset-catalog/serve.py` enforces strict boundary rules:
1. **Loopback Binding**: Strictly binds to `127.0.0.1` and validates `Host` headers to prevent DNS rebinding or non-local access.
2. **Path Traversal Denial**: Rejects backslashes (`\`), null bytes (`\0`), encoded traversal tokens (`%5c`, `%2e%2e`), and `..` segments.
3. **Reparse & Symlink Rejection**: Explicitly checks and denies Windows reparse points, junctions, and symlinks.
4. **Strict Path Whitelist**: Requests must target `tools/asset-catalog/`, `Assets/CityLife/`, or approved `evidence/milestones/` images.
5. **Private Path Blocklist**: Denies access to `.git/`, `Library/`, `Builds/`, `UserSettings/`, private logs, player saves, `.env*`, and cryptographic keys/credentials (`.key`, `.pem`, `.pfx`, `.db`).
6. **Read-Only / No Directory Listing**: Only `GET` and `HEAD` methods are permitted; directory browsing is disabled.

---

## Truthful Verification and Evidence Policies

1. **Default UNVERIFIED**: All asset records default strictly to `UNVERIFIED` across all five evidence tiers (Source Preview, Unity Imported, Prefab/Physics Validated, Runtime Proven, User Accepted). No fabricated accepted badges are claimed.
2. **Provenance & Licensing**: Author and license default to `UNKNOWN_UNVERIFIED`. Candidate license notices on disk are retained only when the file physically exists on disk, with an explicit `UNVERIFIED_CANDIDATE` status. Generic repository or directory license files do not establish verified per-asset licensing.
3. **Scene Sources vs Runtime Instances**: `.unity` files are categorized as `scene_source` (with summary key `scene_sources`), not runtime instances.
4. **Attributed Historical Claims**: Historical claims from earlier documentation rounds are retained exclusively as attributed unverified notes with their origin source path; they never grant acceptance badges.
5. **Nullable Physical Dimensions**: Dimensions, mass, anchored states, and triangle counts remain `null` until measured source evidence with SI units is supplied.
6. **Truthful Preview Coverage & Export Gaps**:
   - Supported formats (OBJ, 2D images) provide live browser rendering.
   - Proprietary formats (FBX, Unity scenes, C# procedural generators) display a clear, honest "Browser 3D Preview Unavailable" state and direct source download links without claiming automatic browser conversion.
7. **Reference Preview Fixtures**:
   - `sample-refuge-stone.obj` is derived from procedural math in `RefugeStone.cs` for testing the WebGL viewer, orbit controls, and PNG export.
   - It is explicitly labeled `DERIVED_REFERENCE_PREVIEW_UNVERIFIED`. It is **not** an exported actual Unity mesh, runtime collider, or accepted game asset.
   - The frontend checkbox "Exclude reference preview fixtures" is enabled by default to ensure only real project source assets are displayed.
