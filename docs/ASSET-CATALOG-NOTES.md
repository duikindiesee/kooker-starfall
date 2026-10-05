# Asset Catalog Tool Slice Notes

Verified local artifact catalog implementation under `tools/asset-catalog/`.

## Delivered Slice Components

1. **Reproducible Generator**: [`generate-catalog.py`](file:///C:/workstreams/starfall-agy-03/tasks/asset-catalog/checkout/tools/asset-catalog/generate-catalog.py)
   - Enumerates source files under `Assets/CityLife`.
   - Computes real SHA256 checksums and byte sizes.
   - Enforces default `UNVERIFIED` acceptance across all evidence tiers.
   - Retains historical notes only as attributed unverified references.
   - Outputs [`asset-catalog.json`](file:///C:/workstreams/starfall-agy-03/tasks/asset-catalog/checkout/tools/asset-catalog/asset-catalog.json).

2. **Safe Local Server**: [`serve.py`](file:///C:/workstreams/starfall-agy-03/tasks/asset-catalog/checkout/tools/asset-catalog/serve.py)
   - Strictly bound to `127.0.0.1` with `Host` header loopback enforcement.
   - Path-traversal guards denying backslashes, `%5c`, `%2e%2e`, and null bytes.
   - Reparse point / symlink denial.
   - Whitelisted file serving for catalog, source assets, and milestone images.

3. **Browser Interface & 3D Viewer**:
   - [`index.html`](file:///C:/workstreams/starfall-agy-03/tasks/asset-catalog/checkout/tools/asset-catalog/index.html)
   - [`app.js`](file:///C:/workstreams/starfall-agy-03/tasks/asset-catalog/checkout/tools/asset-catalog/app.js)
   - [`style.css`](file:///C:/workstreams/starfall-agy-03/tasks/asset-catalog/checkout/tools/asset-catalog/style.css)
   - [`viewer.js`](file:///C:/workstreams/starfall-agy-03/tasks/asset-catalog/checkout/tools/asset-catalog/viewer.js) (Zero-dependency WebGL viewer with orbit controls, zoom, and PNG snapshot export).
   - [`sample-refuge-stone.obj`](file:///C:/workstreams/starfall-agy-03/tasks/asset-catalog/checkout/tools/asset-catalog/sample-refuge-stone.obj) (Explicitly labeled `DERIVED_REFERENCE_PREVIEW_UNVERIFIED` reference fixture).

## Evidence Boundaries

- Acceptance: Defaults strictly to `UNVERIFIED`.
- Physical Properties: Nullable until measured source evidence with units is provided.
- Media: Real verified milestone screenshots linked where present; otherwise marked `Explicitly Missing`.
- Preview Gaps: FBX and Unity scenes truthfully display "Browser 3D Preview Unavailable" and provide direct source asset downloads.
