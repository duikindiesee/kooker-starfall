using UnityEngine;

namespace CityLife.World
{
    // Authoring and compiled acceptance share this exact PhysX site search.
    // A clear eye above an underwater bed is NOT a playable dry viewpoint.
    public static class CoastalSkyViewSites
    {
        public const int SolidMask = (1 << 8) | (1 << 10);
        private static readonly float[] WestOffsets = { 0, -12, -24, -36, -48, -60, -72 };

        public static bool TryDryEyeNear(float proposedX, float z, float nearClip,
            out Vector3 eye, out string evidence)
        {
            eye = Vector3.zero; evidence = "No dry, solid-clear viewpoint west of proposed x=" + proposedX + ",z=" + z;
            if (float.IsNaN(proposedX) || float.IsInfinity(proposedX) ||
                float.IsNaN(z) || float.IsInfinity(z) ||
                float.IsNaN(nearClip) || float.IsInfinity(nearClip) || nearClip <= 0)
                return false;
            Physics.SyncTransforms();
            foreach (float west in WestOffsets)
            {
                float x = proposedX + west;
                if (x < CoastalTerrain.MinX + 5 || x > CoastalTerrain.MaxX - 5 ||
                    z < CoastalTerrain.MinZ + 5 || z > CoastalTerrain.MaxZ - 5) continue;
                if (!Physics.Raycast(new Vector3(x, 300, z), Vector3.down, out var hit, 400,
                    SolidMask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.point.y <= CoastalWater.Level + 1f) continue;
                Vector3 candidate = hit.point + Vector3.up * 2.1f;
                if (Physics.CheckSphere(candidate, Mathf.Max(.55f, nearClip + .25f),
                    SolidMask, QueryTriggerInteraction.Ignore)) continue;
                eye = candidate;
                evidence = "proposed=" + proposedX + "," + z + "; selected=" + x + "," + z +
                    "; collider=" + hit.collider.name + "; floor=" + hit.point + "; eye=" + eye +
                    "; water=" + CoastalWater.Level + "; dryFreeboard=" + (hit.point.y-CoastalWater.Level);
                return true;
            }
            return false;
        }
    }
}
