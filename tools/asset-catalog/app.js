/**
 * Starfall Asset Catalog Frontend Application
 * Offline-first support: reads inline payload first, then fallback to fetch.
 * Relative asset paths for file:// and http:// support without network/CDN.
 */

(function () {
  let catalogData = null;
  let activeCategory = 'ALL';
  let activeType = 'ALL';
  let searchQuery = '';
  let excludeFixtures = true;
  let currentViewer = null;

  function resolveAssetPath(p) {
    if (!p) return '';
    if (p.startsWith('http://') || p.startsWith('https://') || p.startsWith('data:')) return p;
    const clean = p.replace(/^\/+/, '');
    if (window.location.protocol === 'file:') {
      if (clean.startsWith('tools/asset-catalog/')) {
        return clean.replace('tools/asset-catalog/', '');
      }
      return '../../' + clean;
    }
    return '/' + clean;
  }

  async function init() {
    try {
      // 1. Read inline payload if present (offline under file:// with zero network calls)
      const inlineScript = document.getElementById('starfall-catalog-data');
      if (inlineScript && inlineScript.textContent && inlineScript.textContent.trim()) {
        try {
          catalogData = JSON.parse(inlineScript.textContent);
        } catch (e) {
          console.warn('Failed parsing inline catalog payload, trying fetch fallback:', e);
        }
      }

      // 2. If no inline payload, fallback to fetch (for hosted HTTP mode)
      if (!catalogData) {
        const url = window.location.protocol === 'file:' ? 'asset-catalog.json' : '/tools/asset-catalog/asset-catalog.json';
        const response = await fetch(url);
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        catalogData = await response.json();
      }

      renderStats();
      renderFilters();
      renderCatalog();
      bindEvents();
    } catch (err) {
      console.error('Failed to load asset catalog:', err);
      document.getElementById('catalog-grid').innerHTML = `
        <div style="grid-column: 1/-1; padding: 40px; background:#fff; border:1px solid #d9ddcc; border-radius:12px; text-align:center;">
          <h3 style="color:#91633d; margin-bottom:8px;">Offline Data Standalone Mode</h3>
          <p style="color:#59665b; font-size:14px;">Could not load dynamic JSON over <code>file://</code> due to browser fetch restrictions (${err.message}).</p>
          <p style="color:#59665b; font-size:13px; margin-top:8px;">Open <b><code>catalog-offline.html</code></b> directly in your browser for the full self-contained offline catalog.</p>
        </div>
      `;
    }
  }

  function renderStats() {
    const s = catalogData.summary || {};
    document.getElementById('stat-total').textContent = s.total_enumerated_files || catalogData.items.length;
    document.getElementById('stat-imported').textContent = s.imported_assets || 0;
    document.getElementById('stat-procedural').textContent = s.procedural_systems || 0;
    document.getElementById('stat-previewable').textContent = s.preview_available_in_browser || 0;
    document.getElementById('stat-pending').textContent = s.preview_export_pending || 0;
    document.getElementById('stat-commit').textContent = catalogData.generated_commit || 'ce1d467';
  }

  function renderFilters() {
    const categories = new Set();
    const types = new Set();

    catalogData.items.forEach(item => {
      if (item.category) categories.add(item.category);
      if (item.type) types.add(item.type);
    });

    const catContainer = document.getElementById('category-pills');
    catContainer.innerHTML = `<button class="filter-btn active" data-category="ALL">All Categories</button>`;
    Array.from(categories).sort().forEach(cat => {
      const btn = document.createElement('button');
      btn.className = 'filter-btn';
      btn.dataset.category = cat;
      btn.textContent = cat;
      catContainer.appendChild(btn);
    });

    const typeContainer = document.getElementById('type-pills');
    typeContainer.innerHTML = `<button class="filter-btn active" data-type="ALL">All Types</button>`;
    Array.from(types).sort().forEach(t => {
      const btn = document.createElement('button');
      btn.className = 'filter-btn';
      btn.dataset.type = t;
      btn.textContent = t.replace('_', ' ');
      typeContainer.appendChild(btn);
    });
  }

  function filterItems() {
    return catalogData.items.filter(item => {
      if (excludeFixtures && item.is_reference_fixture) return false;

      if (activeCategory !== 'ALL' && item.category !== activeCategory) return false;
      if (activeType !== 'ALL' && item.type !== activeType) return false;

      if (searchQuery) {
        const q = searchQuery.toLowerCase();
        const hay = [
          item.id,
          item.name,
          item.path,
          item.category,
          (item.provenance && item.provenance.author) || ''
        ].join(' ').toLowerCase();
        if (!hay.includes(q)) return false;
      }
      return true;
    });
  }

  function renderCatalog() {
    const grid = document.getElementById('catalog-grid');
    const items = filterItems();

    if (items.length === 0) {
      grid.innerHTML = `
        <div style="grid-column: 1/-1; padding: 40px; background:#fff; border:1px solid #d9ddcc; border-radius:12px; text-align:center;">
          <p style="color:#59665b; font-size:14px;">No items match current filter criteria.</p>
        </div>
      `;
      return;
    }

    grid.innerHTML = items.map(item => {
      const sizeKB = (item.bytes / 1024).toFixed(1);
      const isFixture = item.is_reference_fixture;
      const statusBadge = isFixture
        ? `<span class="badge badge-fixture">REFERENCE FIXTURE</span>`
        : `<span class="badge badge-unverified">STATUS: UNVERIFIED</span>`;

      return `
        <article class="card">
          <div>
            <div class="card-header">
              <span class="badge badge-category">${escapeHtml(item.category)}</span>
              ${statusBadge}
            </div>
            <div class="card-body" style="margin-top:10px;">
              <h3>${escapeHtml(item.name)}</h3>
              <div class="card-id">${escapeHtml(item.id)}</div>
              <div class="card-meta">
                <span><b>Path:</b> ${escapeHtml(item.path)}</span>
                <span><b>Size:</b> ${sizeKB} KB (${item.bytes.toLocaleString()} bytes)</span>
                <span><b>Export:</b> ${escapeHtml(item.derived_export_status)}</span>
              </div>
            </div>
          </div>
          <div class="card-footer">
            <span style="font-size:12px; color:var(--muted); text-transform:uppercase; font-weight:600;">
              ${escapeHtml(item.type.replace('_', ' '))}
            </span>
            <button class="inspect-btn" data-item-id="${escapeHtml(item.id)}">Inspect Detail & 3D</button>
          </div>
        </article>
      `;
    }).join('');

    grid.querySelectorAll('.inspect-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        const id = btn.dataset.itemId;
        const item = catalogData.items.find(x => x.id === id);
        if (item) openDetailModal(item);
      });
    });
  }

  function openDetailModal(item) {
    const modal = document.getElementById('detail-modal');
    document.getElementById('modal-title').textContent = item.name;
    document.getElementById('modal-id').textContent = `${item.id} · ${item.path}`;

    const previewContainer = document.getElementById('modal-preview-area');
    previewContainer.innerHTML = '';

    const downloadPath = resolveAssetPath(item.path);

    // 1. Preview section
    if (item.model_format === 'obj' && item.preview_file) {
      // Real WebGL 3D Preview
      previewContainer.innerHTML = `
        <div class="preview-container">
          <div class="canvas-wrapper">
            <canvas id="mesh-canvas"></canvas>
          </div>
          <div class="viewer-bar">
            <span><b>3D Orbit Controls:</b> Left-drag to rotate | Wheel to zoom</span>
            <div class="viewer-actions">
              <button id="btn-export-png" class="action-btn">📷 Export PNG</button>
              <a href="${downloadPath}" download class="action-btn" id="btn-download-mesh">⬇ Download Model (.obj)</a>
              <button id="btn-reset-cam" class="action-btn">Reset View</button>
            </div>
          </div>
        </div>
      `;
      const canvas = document.getElementById('mesh-canvas');
      currentViewer = new window.StarfallMeshViewer(canvas);

      // Check for embedded OBJ data (avoids fetch under file://)
      let embeddedObj = item.embedded_obj_data;
      if (!embeddedObj) {
        const embedEl = document.getElementById('embedded-reference-obj');
        if (embedEl && embedEl.textContent) {
          embeddedObj = embedEl.textContent.trim();
        }
      }

      if (embeddedObj) {
        currentViewer.loadOBJ(embeddedObj);
        if (window.location.protocol === 'file:') {
          const blob = new Blob([embeddedObj], { type: 'text/plain;charset=utf-8' });
          document.getElementById('btn-download-mesh').href = URL.createObjectURL(blob);
        }
      } else {
        // Fallback to fetch for hosted mode
        fetch(resolveAssetPath(item.preview_file))
          .then(r => r.text())
          .then(objText => {
            currentViewer.loadOBJ(objText);
          })
          .catch(err => {
            console.error('Failed to load OBJ:', err);
          });
      }

      document.getElementById('btn-export-png').onclick = () => {
        if (currentViewer) currentViewer.exportPNG(`${item.name}_snapshot.png`);
      };
      document.getElementById('btn-reset-cam').onclick = () => {
        if (currentViewer) { currentViewer.resetCamera(); currentViewer.render(); }
      };

    } else if (item.model_format === 'image' && item.preview_file) {
      // 2D Image / Texture preview
      const imgPath = resolveAssetPath(item.preview_file);
      previewContainer.innerHTML = `
        <div class="preview-container">
          <img class="image-preview" src="${imgPath}" alt="${escapeHtml(item.name)}" />
          <div class="viewer-bar">
            <span><b>Image Preview:</b> 2D Source Texture / Unmodified Asset</span>
            <div class="viewer-actions">
              <a href="${downloadPath}" download class="action-btn">⬇ Download Image File</a>
            </div>
          </div>
        </div>
      `;
    } else {
      // Graceful, honest unavailable state
      let extraImageHtml = '';
      if (item.media && item.media.inhabitant_image) {
        const milestoneImg = resolveAssetPath(item.media.inhabitant_image);
        extraImageHtml = `
          <div style="margin-top:16px; border-top:1px solid #e3decb; padding-top:14px; text-align:left;">
            <div style="font-size:12px; font-weight:700; color:var(--green); margin-bottom:6px;">
              VERIFIED MILESTONE EVIDENCE CAPTURE (Historical Reference, Not Live 3D Render)
            </div>
            <img src="${milestoneImg}" style="max-height:220px; border-radius:6px; border:1px solid var(--line);" />
            <div style="font-size:11px; color:var(--muted); margin-top:4px;">Source: ${escapeHtml(item.media.inhabitant_image)}</div>
          </div>
        `;
      }

      previewContainer.innerHTML = `
        <div class="preview-container">
          <div class="unavailable-box">
            <h4>Browser 3D Preview Unavailable</h4>
            <p>
              Format: <b>${escapeHtml(item.model_format.toUpperCase())}</b> (${escapeHtml(item.type.replace('_', ' '))}).<br>
              Browser WebGL does not decode FBX or Unity scene binaries natively without an authorized serialization export slice.
              Model export is deferred to a future serialized author slice.
              Source download and truthful checksums are provided below.
            </p>
            <div class="viewer-actions" style="justify-content:center;">
              <a href="${downloadPath}" download class="action-btn" style="background:var(--green); color:#fff; border-color:var(--green);">
                ⬇ Download Source Asset (${escapeHtml(item.name)})
              </a>
            </div>
            ${extraImageHtml}
          </div>
        </div>
      `;
    }

    // 2. Technical & Identity Details
    const metaArea = document.getElementById('modal-metadata-area');
    const prov = item.provenance || {};
    const ev = item.evidence || {};
    const phys = item.physical_properties || {};
    const med = item.media || {};

    let histHtml = '';
    if (item.historical_unverified_notes) {
      histHtml = `
        <div class="hist-box">
          <strong>Attributed Historical Claim (Unverified):</strong>
          <p style="margin-top:4px;">${escapeHtml(item.historical_unverified_notes)}</p>
          <div class="hist-source">Source Reference: ${escapeHtml(item.historical_source_reference || 'Repository documentation')}</div>
        </div>
      `;
    }

    metaArea.innerHTML = `
      <div class="detail-grid">
        <div class="detail-section">
          <h4>Technical Identity</h4>
          <table class="kv-table">
            <tr><td class="kv-key">Relative Path</td><td class="kv-val kv-code">${escapeHtml(item.path)}</td></tr>
            <tr><td class="kv-key">Byte Size</td><td class="kv-val">${item.bytes.toLocaleString()} bytes</td></tr>
            <tr><td class="kv-key">SHA256</td><td class="kv-val kv-code">${escapeHtml(item.sha256 || 'null')}</td></tr>
            <tr><td class="kv-key">Commit</td><td class="kv-val kv-code">${escapeHtml(item.source_commit)}${item.is_dirty ? ' (dirty)' : ''}</td></tr>
            <tr><td class="kv-key">Derived Export</td><td class="kv-val">${escapeHtml(item.derived_export_status)}</td></tr>
          </table>
        </div>

        <div class="detail-section">
          <h4>Provenance & Licensing</h4>
          <table class="kv-table">
            <tr><td class="kv-key">Author / Publisher</td><td class="kv-val">${escapeHtml(prov.author || 'Unknown')}</td></tr>
            <tr><td class="kv-key">License</td><td class="kv-val"><b>${escapeHtml(prov.license || 'Unknown')}</b></td></tr>
            <tr><td class="kv-key">Notice Path</td><td class="kv-val kv-code">${escapeHtml(prov.license_notice_path || 'None')}</td></tr>
            <tr><td class="kv-key">Source URL</td><td class="kv-val kv-code">${escapeHtml(prov.source_url || item.path)}</td></tr>
          </table>
        </div>
      </div>

      <div class="detail-section" style="margin-bottom:20px;">
        <h4>Evidence Tiers (Default UNVERIFIED)</h4>
        <table class="evidence-table">
          <thead>
            <tr><th>Evidence Tier</th><th>Verification Status</th></tr>
          </thead>
          <tbody>
            <tr><td>1. Source Preview</td><td>${escapeHtml(ev.source_preview || 'UNVERIFIED')}</td></tr>
            <tr><td>2. Unity Imported</td><td>${escapeHtml(ev.unity_imported || 'UNVERIFIED')}</td></tr>
            <tr><td>3. Prefab / Material / Physics Validated</td><td>${escapeHtml(ev.prefab_material_physics_validated || 'UNVERIFIED')}</td></tr>
            <tr><td>4. Runtime Proven</td><td>${escapeHtml(ev.runtime_proven || 'UNVERIFIED')}</td></tr>
            <tr><td>5. User Accepted</td><td>${escapeHtml(ev.user_accepted || 'UNVERIFIED')}</td></tr>
          </tbody>
        </table>
      </div>

      <div class="detail-grid">
        <div class="detail-section">
          <h4>Physical Properties (Nullable until measured)</h4>
          <table class="kv-table">
            <tr><td class="kv-key">Dimensions</td><td class="kv-val">${phys.dimensions ? escapeHtml(phys.dimensions) : 'null (unmeasured)'}</td></tr>
            <tr><td class="kv-key">Mass</td><td class="kv-val">${phys.mass ? escapeHtml(phys.mass) : 'null (unmeasured)'}</td></tr>
            <tr><td class="kv-key">Anchored</td><td class="kv-val">${phys.anchored !== null ? escapeHtml(String(phys.anchored)) : 'null (unmeasured)'}</td></tr>
            <tr><td class="kv-key">Triangles</td><td class="kv-val">${phys.triangles ? escapeHtml(String(phys.triangles)) : 'null (unmeasured)'}</td></tr>
          </table>
        </div>

        <div class="detail-section">
          <h4>Media Verification</h4>
          <table class="kv-table">
            <tr><td class="kv-key">Inhabitant Image</td><td class="kv-val">${med.inhabitant_image ? escapeHtml(med.inhabitant_image) : 'Explicitly Missing'}</td></tr>
            <tr><td class="kv-key">Interaction Video</td><td class="kv-val">${med.interaction_video ? escapeHtml(med.interaction_video) : 'Explicitly Missing'}</td></tr>
          </table>
        </div>
      </div>

      ${histHtml}
    `;

    modal.classList.add('open');
  }

  function closeModal() {
    const modal = document.getElementById('detail-modal');
    modal.classList.remove('open');
    if (currentViewer) {
      currentViewer = null;
    }
  }

  function bindEvents() {
    document.getElementById('search-input').addEventListener('input', (e) => {
      searchQuery = e.target.value.trim();
      renderCatalog();
    });

    document.getElementById('exclude-fixtures-cb').addEventListener('change', (e) => {
      excludeFixtures = e.target.checked;
      renderCatalog();
    });

    document.getElementById('category-pills').addEventListener('click', (e) => {
      if (e.target.classList.contains('filter-btn')) {
        document.querySelectorAll('#category-pills .filter-btn').forEach(b => b.classList.remove('active'));
        e.target.classList.add('active');
        activeCategory = e.target.dataset.category;
        renderCatalog();
      }
    });

    document.getElementById('type-pills').addEventListener('click', (e) => {
      if (e.target.classList.contains('filter-btn')) {
        document.querySelectorAll('#type-pills .filter-btn').forEach(b => b.classList.remove('active'));
        e.target.classList.add('active');
        activeType = e.target.dataset.type;
        renderCatalog();
      }
    });

    document.getElementById('modal-close-btn').addEventListener('click', closeModal);
    document.getElementById('detail-modal').addEventListener('click', (e) => {
      if (e.target.id === 'detail-modal') closeModal();
    });
    window.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') closeModal();
    });
  }

  function escapeHtml(str) {
    if (!str) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  window.addEventListener('DOMContentLoaded', init);
})();
