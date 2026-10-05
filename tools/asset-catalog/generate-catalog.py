#!/usr/bin/env python3
"""
CityLife / Starfall Asset Catalog Generator
Truthful, reproducible inventory generator scanning Assets/CityLife.
- Enumerates complete file inventory under Assets/CityLife (excluding .meta, caches, temp)
- Computes real SHA256 and byte length for every file
- All acceptance statuses default to UNVERIFIED (never unverified accepted badges)
- Physical properties (dimensions, mass, triangles) default to null unless measured with units
- Distinguishes generator source vs exported instance
- Labels sample mesh as DERIVED_REFERENCE_PREVIEW_UNVERIFIED
"""

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import sys

EXCLUDED_NAMES = {
    ".git", ".gitignore", ".gitattributes", ".DS_Store", "Thumbs.db"
}

EXCLUDED_DIR_PARTS = {
    "generatedpreview", "library", "temp", "obj", "builds", "usersettings"
}

CATEGORY_MAP = {
    "art/polyhaven": "Flora & Trees",
    "art/previews": "Visual Previews",
    "art/licenses": "Licenses & Notices",
    "art/resources": "Textures & Materials",
    "characters": "Characters & Rigging",
    "editor": "Editor Authoring & Build",
    "environment": "Environment Foundation",
    "environmentzones": "Environment Zones & Caves",
    "food": "Food & Ecology",
    "refuge": "Refuge & Shelters",
    "resources": "World Definitions & Vectors",
    "scenes": "Runtime Scenes",
    "scripts": "World Generation & Runtime Scripts",
    "shaders": "Shaders & HLSL"
}

HISTORICAL_NOTES_MAP = {
    "Assets/CityLife/Art/Resources/CityLifeArt/DryGround_1K.jpg": {
        "source": "docs/ASSET-CATALOGUE.md",
        "historical_claim": "Historical note: Photographed by Rob Tuytel, processed by Rico Cilliers. Selected ground detail for 9 Sept 2026 arid milestone. Passed offscreen walking route report with zero errors. Native presentation unverified."
    },
    "Assets/CityLife/Art/Previews/Kenney_rock_largeA.png": {
        "source": "docs/ASSET-CATALOGUE.md",
        "historical_claim": "Historical note: Unmodified isometric preview extracted from CC0 package. 3D model was not imported into Unity; arid material treatment unverified."
    },
    "Assets/CityLife/Art/Previews/Kenney_grass_large.png": {
        "source": "docs/ASSET-CATALOGUE.md",
        "historical_claim": "Historical note: Unmodified isometric preview from Nature Kit 2.1 archive. Model not imported; density, culling, and arid palette unverified."
    },
    "Assets/CityLife/Scripts/KokerboomGeometry.cs": {
        "source": "evidence/verified/starfall-tree-baseline.json",
        "historical_claim": "Historical note: Procedural Aloidendron dichotomum generator (citylife.aloidendron-dichotomum.v2-preview). Tested with historical 349-assertion numeric validation suite. Retained R06 visual reference was approved by user as baseline; critic scored R19 overall 4.75."
    },
    "Assets/CityLife/Refuge/RefugeStone.cs": {
        "source": "Assets/CityLife/Refuge/RefugeStone.cs",
        "historical_claim": "Historical note: Deterministic bevelled angular rock surface (7 rings x 12 sides) written for shared visual rendering and MeshCollider. Independent physical validation unverified."
    },
    "Assets/CityLife/EnvironmentZones/CaveEnvironmentZone.cs": {
        "source": "docs/CAVE-ENVIRONMENT-ZONES.md",
        "historical_claim": "Historical note: Pure SI-unit cave zone contract (starfall.environment-zone.v1) evaluating interior wind/rain occlusion, rock thermal buffer, and flood freeboard."
    },
    "Assets/CityLife/Food/EdenEcology.cs": {
        "source": "docs/FOOD-INTEGRATION.md",
        "historical_claim": "Historical note: Finite berry bush lifecycle and seed drop rules (food-eden.2) tested in editor-v015 loops."
    },
    "Assets/CityLife/Scripts/HunterClubCarry.cs": {
        "source": "docs/HUNTER-CLOTHING.md",
        "historical_claim": "Historical note: Cosmetic left-hand prop authoring in bind pose. 3184 frames evaluated for ground clearance."
    },
    "Assets/CityLife/Editor/HunterOutfitAuthoring.cs": {
        "source": "docs/HUNTER-CLOTHING.md",
        "historical_claim": "Historical note: Derived garment construction on CC0 male body. Continuous pelvis coverage reviewed in seated/crouch captures; user accepted exposed-shoulder silhouette."
    }
}

def compute_sha256(filepath):
    h = hashlib.sha256()
    with open(filepath, "rb") as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest()

def infer_category(rel_posix):
    lower = rel_posix.lower()
    for prefix, cat in CATEGORY_MAP.items():
        if f"assets/citylife/{prefix}" in lower:
            return cat
    return "CityLife General"

def infer_model_format(rel_posix):
    ext = Path(rel_posix).suffix.lower()
    if ext == ".fbx": return "fbx"
    if ext == ".obj": return "obj"
    if ext == ".glb": return "glb"
    if ext in (".png", ".jpg", ".jpeg", ".exr"): return "image"
    if ext == ".cs": return "cs_source"
    if ext in (".shader", ".hlsl"): return "shader"
    if ext == ".unity": return "unity_scene"
    if ext == ".json": return "json_definition"
    return "text"

def scan_assets(repo_root, source_commit, is_dirty=False):
    citylife_dir = repo_root / "Assets" / "CityLife"
    if not citylife_dir.exists():
        raise FileNotFoundError(f"Directory not found: {citylife_dir}")

    items = []
    
    # 1. Walk entire Assets/CityLife directory
    for root, dirs, files in os.walk(citylife_dir):
        # Filter excluded directories
        dirs[:] = [d for d in dirs if not any(part in d.lower() for part in EXCLUDED_DIR_PARTS)]
        
        for file in sorted(files):
            if file.endswith(".meta") or file in EXCLUDED_NAMES:
                continue
            
            full_path = Path(root) / file
            rel_path = full_path.relative_to(repo_root).as_posix()
            
            file_bytes = full_path.stat().st_size
            file_hash = compute_sha256(full_path)
            
            ext = full_path.suffix.lower()
            category = infer_category(rel_path)
            model_format = infer_model_format(rel_path)
            
            # Determine type & preview capability
            if ext == ".fbx":
                item_type = "imported_asset"
                preview_status = "unavailable_fbx"
                preview_file = None
                derived_export_status = "derived_missing_export"
            elif ext in (".png", ".jpg", ".jpeg"):
                item_type = "imported_asset" if "Art" in rel_path or "Characters" in rel_path else "source_file"
                preview_status = "available"
                preview_file = rel_path
                derived_export_status = "native_supported"
            elif ext == ".exr":
                item_type = "imported_asset"
                preview_status = "unavailable_exr"
                preview_file = None
                derived_export_status = "derived_missing_export"
            elif ext == ".cs":
                # Check if it's a procedural generator or runtime script
                if any(k in file for k in ("Geometry", "Rocks", "Stone", "Ecology", "Zone", "Outfit", "Carry")):
                    item_type = "procedural_system"
                else:
                    item_type = "source_file"
                preview_status = "unavailable_procedural_export_pending"
                preview_file = None
                derived_export_status = "derived_missing_export"
            elif ext in (".shader", ".hlsl"):
                item_type = "source_file"
                preview_status = "unavailable_shader"
                preview_file = None
                derived_export_status = "not_applicable"
            elif ext == ".unity":
                item_type = "scene_source"
                preview_status = "unavailable_unity_asset"
                preview_file = None
                derived_export_status = "derived_missing_export"
            else:
                item_type = "source_file"
                preview_status = "unavailable_text"
                preview_file = None
                derived_export_status = "not_applicable"

            # Stable relative ID
            clean_id = rel_path.lower().replace("assets/citylife/", "").replace("/", ".").replace("_", "-")
            if clean_id.endswith(ext):
                clean_id = clean_id[:-len(ext)]
            stable_id = f"starfall.{clean_id}"

            # All acceptance statuses default strictly to UNVERIFIED
            evidence_distinctions = {
                "source_preview": "UNVERIFIED",
                "unity_imported": "UNVERIFIED",
                "prefab_material_physics_validated": "UNVERIFIED",
                "runtime_proven": "UNVERIFIED",
                "user_accepted": "UNVERIFIED"
            }

            # Optional physical properties: strictly null unless measured source with units
            physical_properties = {
                "dimensions": None,
                "mass": None,
                "anchored": None,
                "triangles": None
            }

            # Media: strictly explicit missing unless real evidence file exists
            media = {
                "inhabitant_image": None,
                "interaction_video": None
            }

            # Link real milestone image if known
            if "DryGround" in file:
                evidence_img = "evidence/milestones/04c-arid-walking-2026-09-09.png"
                if (repo_root / evidence_img).exists():
                    media["inhabitant_image"] = evidence_img
            elif "IslandDefinition" in file:
                evidence_img = "evidence/milestones/04a-arid-overview-2026-09-09.png"
                if (repo_root / evidence_img).exists():
                    media["inhabitant_image"] = evidence_img
            elif "EdenEcology" in file or "FoodModel" in file:
                evidence_img = "evidence/milestones/food/editor-v015/01-editor-unknown.png"
                if (repo_root / evidence_img).exists():
                    media["inhabitant_image"] = evidence_img

            # Attributed historical note (never an accepted badge)
            hist_entry = HISTORICAL_NOTES_MAP.get(rel_path)
            historical_unverified_notes = hist_entry["historical_claim"] if hist_entry else None
            historical_source_reference = hist_entry["source"] if hist_entry else None

            # Provenance: default license and author to UNKNOWN_UNVERIFIED.
            # Retain candidate notice path only when file exists, with explicit unverified status;
            # do not claim generic license file establishes per-asset license.
            candidate_notice = None
            parent_dir = full_path.parent
            for cand_name in ("CC0-1.0.txt", "LICENSE", "LICENSE.txt", "License.txt", "Kenney-Nature-Kit-License.txt", "provenance.json"):
                cand_file = parent_dir / cand_name
                if cand_file.exists() and cand_file != full_path:
                    candidate_notice = cand_file.relative_to(repo_root).as_posix()
                    break

            if not candidate_notice:
                if "Kenney" in rel_path:
                    k_notice = repo_root / "Assets/CityLife/Art/Licenses/Kenney-Nature-Kit-License.txt"
                    if k_notice.exists():
                        candidate_notice = "Assets/CityLife/Art/Licenses/Kenney-Nature-Kit-License.txt"
                elif "QuiverTree01" in rel_path or "Quiver-Tree-01" in rel_path:
                    qt1_notice = repo_root / "Assets/CityLife/Art/Licenses/Poly-Haven-Quiver-Tree-01.txt"
                    if qt1_notice.exists():
                        candidate_notice = "Assets/CityLife/Art/Licenses/Poly-Haven-Quiver-Tree-01.txt"
                elif "QuiverTree02" in rel_path or "Quiver-Tree-02" in rel_path:
                    qt2_notice = repo_root / "Assets/CityLife/Art/Licenses/Poly-Haven-Quiver-Tree-02.txt"
                    if qt2_notice.exists():
                        candidate_notice = "Assets/CityLife/Art/Licenses/Poly-Haven-Quiver-Tree-02.txt"

            provenance = {
                "license": "UNKNOWN_UNVERIFIED",
                "author": "UNKNOWN_UNVERIFIED",
                "source_url": rel_path,
                "candidate_notice_path": candidate_notice,
                "license_notice_path": candidate_notice,
                "notice_status": "UNVERIFIED_CANDIDATE" if candidate_notice else "NONE",
                "provenance_notes": (
                    "Candidate notice file exists on disk but does not establish verified per-asset license; author and license remain UNKNOWN_UNVERIFIED."
                    if candidate_notice else
                    "No candidate license notice identified; license and author remain UNKNOWN_UNVERIFIED."
                )
            }

            item = {
                "id": stable_id,
                "name": file,
                "category": category,
                "type": item_type,
                "path": rel_path,
                "bytes": file_bytes,
                "sha256": file_hash,
                "source_commit": source_commit,
                "is_dirty": is_dirty,
                "derived_export_status": derived_export_status,
                "preview_status": preview_status,
                "preview_file": preview_file,
                "model_format": model_format,
                "acceptance_status": "UNVERIFIED",
                "evidence": evidence_distinctions,
                "physical_properties": physical_properties,
                "media": media,
                "provenance": provenance,
                "historical_unverified_notes": historical_unverified_notes,
                "historical_source_reference": historical_source_reference,
                "is_reference_fixture": False
            }
            items.append(item)

    # 2. Add the test reference preview fixture (clearly labeled DERIVED_REFERENCE_PREVIEW_UNVERIFIED)
    sample_obj_rel = "tools/asset-catalog/sample-refuge-stone.obj"
    sample_obj_path = repo_root / sample_obj_rel
    if sample_obj_path.exists():
        items.append({
            "id": "reference.fixture.refuge-stone-derived-preview",
            "name": "Refuge Stone (Derived Reference Preview)",
            "category": "Reference Fixtures",
            "type": "reference_preview_fixture",
            "path": sample_obj_rel,
            "bytes": sample_obj_path.stat().st_size,
            "sha256": compute_sha256(sample_obj_path),
            "source_commit": source_commit,
            "is_dirty": is_dirty,
            "derived_export_status": "DERIVED_REFERENCE_PREVIEW_UNVERIFIED",
            "preview_status": "available",
            "preview_file": sample_obj_rel,
            "model_format": "obj",
            "acceptance_status": "UNVERIFIED",
            "evidence": {
                "source_preview": "DERIVED_REFERENCE_PREVIEW_UNVERIFIED (Generated from procedural math in Assets/CityLife/Refuge/RefugeStone.cs)",
                "unity_imported": "UNVERIFIED (Not an exported Unity mesh)",
                "prefab_material_physics_validated": "UNVERIFIED (Not a runtime collider)",
                "runtime_proven": "UNVERIFIED (Scale and in-world behavior unverified)",
                "user_accepted": "UNVERIFIED (Reference preview fixture only; not an accepted game asset)"
            },
            "physical_properties": {
                "dimensions": None,
                "mass": None,
                "anchored": None,
                "triangles": None
            },
            "media": {
                "inhabitant_image": None,
                "interaction_video": None
            },
            "provenance": {
                "license": "UNKNOWN_UNVERIFIED",
                "author": "UNKNOWN_UNVERIFIED (Originating generator math: Assets/CityLife/Refuge/RefugeStone.cs)",
                "source_url": sample_obj_rel,
                "candidate_notice_path": None,
                "license_notice_path": None,
                "notice_status": "NONE",
                "provenance_notes": "Reference preview fixture only; not an accepted game asset."
            },
            "historical_unverified_notes": "Reference preview fixture only for browser WebGL orbit/rotate/zoom and PNG snapshot verification. Not an exported Unity mesh or accepted game asset.",
            "historical_source_reference": "Assets/CityLife/Refuge/RefugeStone.cs",
            "is_reference_fixture": True
        })

    return items

def emit_offline_html(repo_root, catalog, output_path):
    index_path = repo_root / "tools" / "asset-catalog" / "index.html"
    index_html = index_path.read_text(encoding="utf-8")

    # Safely escape JSON to prevent inline injection
    escaped_json = json.dumps(catalog).replace("<", "\\u003c").replace(">", "\\u003e").replace("&", "\\u0026")

    # Read sample OBJ to embed for offline mesh viewer without fetch
    sample_obj_path = repo_root / "tools" / "asset-catalog" / "sample-refuge-stone.obj"
    embedded_obj_script = ""
    if sample_obj_path.exists():
        obj_text = sample_obj_path.read_text(encoding="utf-8").replace("<", "\\u003c").replace(">", "\\u003e")
        embedded_obj_script = f'\n  <script type="text/plain" id="embedded-reference-obj">\n{obj_text}\n  </script>'

    payload_block = f"""
  <!-- Safely Escaped Offline Catalog Payload -->
  <script type="application/json" id="starfall-catalog-data">
{escaped_json}
  </script>{embedded_obj_script}
"""
    if '<script src="viewer.js"></script>' in index_html:
        offline_html = index_html.replace('<script src="viewer.js"></script>', f'{payload_block}\n  <script src="viewer.js"></script>')
    elif "</body>" in index_html:
        offline_html = index_html.replace("</body>", f"{payload_block}\n</body>")
    else:
        offline_html = index_html + payload_block

    output_path.write_text(offline_html, encoding="utf-8")
    print(f"Emitted standalone offline HTML: {output_path}")

def main():
    parser = argparse.ArgumentParser(description="Generate complete truthful CityLife asset inventory")
    parser.add_argument("--commit", default="ce1d467be2fffea7d4874e27ebe14dc58d0788b5", help="Source commit full hash")
    parser.add_argument("--dirty", action="store_true", default=True, help="Mark catalog commit as dirty")
    parser.add_argument("--output", default="tools/asset-catalog/asset-catalog.json", help="Output JSON path")
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parent.parent.parent
    items = scan_assets(repo_root, source_commit=args.commit, is_dirty=args.dirty)

    total_files = len(items)
    real_source_items = [x for x in items if not x["is_reference_fixture"]]
    imported_assets = [x for x in real_source_items if x["type"] == "imported_asset"]
    procedural_systems = [x for x in real_source_items if x["type"] == "procedural_system"]
    scene_sources = [x for x in real_source_items if x["type"] == "scene_source"]
    source_files = [x for x in real_source_items if x["type"] == "source_file"]
    previewable = [x for x in items if x["preview_status"] == "available"]
    export_pending = [x for x in items if x["preview_status"] != "available"]

    catalog = {
        "schema": "starfall.asset-catalog.v2",
        "generated_commit": args.commit,
        "is_dirty": args.dirty,
        "title": "Starfall / CityLife Complete Source Asset Inventory",
        "description": "Truthful enumerated inventory of source Assets/CityLife. All acceptance statuses default to UNVERIFIED.",
        "summary": {
            "total_enumerated_files": total_files,
            "real_citylife_source_assets": len(real_source_items),
            "imported_assets": len(imported_assets),
            "procedural_systems": len(procedural_systems),
            "scene_sources": len(scene_sources),
            "source_files": len(source_files),
            "reference_preview_fixtures": sum(1 for x in items if x["is_reference_fixture"]),
            "preview_available_in_browser": len(previewable),
            "preview_export_pending": len(export_pending),
            "acceptance_policy": "ALL_DEFAULT_UNVERIFIED"
        },
        "items": items
    }

    out_file = repo_root / args.output
    out_file.parent.mkdir(parents=True, exist_ok=True)
    with open(out_file, "w", encoding="utf-8") as f:
        json.dump(catalog, f, indent=2)

    print(f"Generated complete inventory of {total_files} files -> {out_file}")
    print(f"  Real source assets: {len(real_source_items)}")
    print(f"  Imported assets: {len(imported_assets)}")
    print(f"  Procedural systems: {len(procedural_systems)}")
    print(f"  Scene sources: {len(scene_sources)}")
    print(f"  All acceptance statuses: UNVERIFIED")

    # Emit standalone offline HTML beside index.html
    offline_html_path = repo_root / "tools" / "asset-catalog" / "catalog-offline.html"
    emit_offline_html(repo_root, catalog, offline_html_path)

if __name__ == "__main__":
    main()
