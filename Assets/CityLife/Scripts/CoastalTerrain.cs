using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityLife.World
{
    /// <summary>A finite, new coastal study. Never reads or changes an IslandDefinition or saved edits.</summary>
    public static class CoastalTerrain
    {
        public const string DefinitionId = "starfall.coastal-world.v1";
        public const string ContentRevision = "fish-river-canyon-r1";
        public const int Seed = 1904242;
        public const float MinX = -600f, MaxX = 600f, MinZ = -700f, MaxZ = 900f;
        public const float SeaLevel = CoastalWater.Level;
        public const int CellsX = 300, CellsZ = 400;
        public const float HeroPadRadius = 8.5f;
        public static readonly Vector2 ActivityCentre = new Vector2(120, -80);

        private static readonly Vector2[] Feed = Curve(new[] {
            new Vector2(-90,-700), new Vector2(-145,-540), new Vector2(-85,-390),
            new Vector2(25,-245), new Vector2(-40,-95), new Vector2(0,-22)
        });
        private static readonly Vector2[] Outlet = Curve(new[] {
            new Vector2(0,22), new Vector2(70,145), new Vector2(-35,275),
            new Vector2(90,410), new Vector2(25,555), new Vector2(0,690)
        });

        /// <summary>Metre-space surface height; outside this finite patch, returns the nearest edge height.</summary>
        public static float Height(float x, float z)
        {
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z))
                throw new ArgumentOutOfRangeException("Coastal coordinates must be finite.");
            x = Mathf.Clamp(x, MinX, MaxX); z = Mathf.Clamp(z, MinZ, MaxZ);
            float broad = Noise(x * .0045f, z * .0045f);
            float ground = 2.2f + broad * 4.8f + Noise(x * .018f + 17, z * .018f) * .8f;

            // Distinct asymmetric mesa masses frame the channel; their shoulders are
            // several metres deep, rather than noise displacing an otherwise flat plane.
            float mesa = Mesa(x,z,-390,-490,250,300,118,7);
            mesa = Mathf.Max(mesa,Mesa(x,z,365,-430,235,325,142,31));
            mesa = Mathf.Max(mesa,Mesa(x,z,-405,-80,245,270,156,63));
            mesa = Mathf.Max(mesa,Mesa(x,z,410,20,250,310,132,97));
            mesa = Mathf.Max(mesa,Mesa(x,z,-390,350,250,310,124,151));
            mesa = Mathf.Max(mesa,Mesa(x,z,410,430,260,300,112,181));
            ground += mesa;

            // Near-ring meander: a west-facing land neck keeps this an outcrop connected
            // to the bank, rather than claiming a recreation of the old island outline.
            float radius = Mathf.Sqrt(x * x + z * z);
            float ringRadius = Mathf.Sqrt(x * x + z * z * .88f);
            float ringOffset = 21f + Noise(x * .085f + 13, z * .085f) * .75f;
            float ringDistance = Mathf.Abs(ringRadius - ringOffset) - 5.6f;
            float westNeck = Smooth(.78f, .98f, -x / Mathf.Max(radius, .001f)) * (1f - Smooth(3f, 7f, Mathf.Abs(z)));
            float ringCut = (1f - Smooth(-.6f, 4.2f, ringDistance)) * (1f - westNeck);
            float feedWidth = Mathf.Lerp(28f, 11f, Smooth(-700f, -22f, z));
            float feedCut = 1f - Smooth(-2f, 12f, DistanceToCurve(x,z,Feed) - feedWidth);
            float outletWidth = Mathf.Lerp(12f, 105f, Smooth(30f, 700f, z));
            float outletCut = 1f - Smooth(-2f, 18f, DistanceToCurve(x,z,Outlet) - outletWidth);
            float channelCut = Mathf.Max(ringCut, Mathf.Max(feedCut, outletCut));
            float riverBed = -4.5f + Noise(x * .075f + 9, z * .075f) * .4f;
            riverBed -= Smooth(120f, 700f, z) * 11.5f;
            ground = Mathf.Lerp(ground, Mathf.Min(ground, riverBed), channelCut);

            // One continuous underwater heightfield extends into the broad genuine sea.
            float coast = 680f + Mathf.Sin(x * .011f) * 34f + Noise(x * .009f, 31) * 22f;
            float seaCut = Smooth(coast - 45f, coast + 55f, z);
            float seabed = Mathf.Lerp(-10f, -28f, Smooth(650f, 900f, z)) + Noise(x * .009f + 5,z * .009f) * 1.3f;
            ground = Mathf.Lerp(ground, seabed, seaCut);

            // Exact, broad 0 m pad protects the frozen hero tree's existing root placement.
            // The shoulder transitions to the carved shore between 8.5 and 13 metres.
            float pad = 1f - Smooth(HeroPadRadius, 13f, radius);
            ground = Mathf.Lerp(ground, 0f, pad);
            // A second modest, dry terrace separates inhabitant activities from
            // the hero tree's dense rock bank while remaining part of the terrain.
            float activityRadius = Vector2.Distance(new Vector2(x, z), ActivityCentre);
            ground = Mathf.Lerp(ground, 4f, 1f - Smooth(18f, 24f, activityRadius));
            return ground;
        }

        public static GameObject Create(Transform parent)
        {
            Shader shader = Shader.Find("CityLife/CoastalTerrain");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Required CoastalTerrain shader is unavailable.");
            int stride = CellsX + 1;
            var vertices = new Vector3[stride * (CellsZ + 1)];
            var uv = new Vector2[vertices.Length];
            var indices = new int[CellsX * CellsZ * 6];
            int cursor = 0;
            for (int z = 0; z <= CellsZ; z++) for (int x = 0; x <= CellsX; x++)
            {
                float wx = Mathf.Lerp(MinX,MaxX,x/(float)CellsX), wz = Mathf.Lerp(MinZ,MaxZ,z/(float)CellsZ);
                int at = z * stride + x;
                vertices[at] = new Vector3(wx,Height(wx,wz),wz); uv[at] = new Vector2(wx,wz) * .25f;
                if (x == CellsX || z == CellsZ) continue;
                indices[cursor++] = at; indices[cursor++] = at + stride; indices[cursor++] = at + 1;
                indices[cursor++] = at + 1; indices[cursor++] = at + stride; indices[cursor++] = at + stride + 1;
            }
            var mesh = new Mesh { name = DefinitionId + " " + ContentRevision + " seed " + Seed + " continuous land and seabed", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = indices;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var material = new Material(shader) { name = "Coastal ochre strata and sandy shelves - procedural" };
            material.SetFloat("_SeaLevel",SeaLevel);
            var root = new GameObject("Coastal terrain " + DefinitionId + " seed " + Seed);
            root.transform.SetParent(parent,false);
            root.layer = 8;
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = material;
            // The identical mesh is the static collision surface. No hidden flat proxy.
            root.AddComponent<MeshCollider>().sharedMesh = mesh;
            return root;
        }

        private static float Mesa(float x,float z,float cx,float cz,float rx,float rz,float height,float salt)
        {
            float warpX = Noise(x*.041f+salt,z*.041f)*2.5f;
            float warpZ = Noise(x*.039f,z*.039f+salt)*2.2f;
            float dx=(x-cx+warpX)/rx,dz=(z-cz+warpZ)/rz;
            float radial = Mathf.Sqrt(dx*dx+dz*dz);
            float nx=dx/Mathf.Max(radial,.001f),nz=dz/Mathf.Max(radial,.001f);
            // Directional erosion makes broad buttresses/gullies continuous around the
            // mass. Unlike an angle lookup it has no +/-pi seam in the heightfield.
            float buttress = Noise(nx*3.8f+salt,nz*3.8f);
            float erosion = Noise(nx*7.3f+salt*.37f,nz*7.3f+salt);
            float shoulder = 1f-radial + buttress*.085f + Noise(x*.12f+salt,z*.12f)*.025f;
            float rise = Smooth(-.20f,.30f,shoulder);
            float body = rise*.84f + Smooth(.37f,.60f,rise)*.10f + Smooth(.77f,.96f,rise)*.06f;
            float talus = Smooth(-.41f,-.16f,shoulder)*(1f-Smooth(-.02f,.27f,shoulder))*height*.095f;
            float gully = Smooth(.10f,.65f,erosion)*Smooth(-.19f,.04f,shoulder)*(1f-Smooth(.27f,.48f,shoulder))*2.4f;
            float top = height + Noise(x*.078f+salt,z*.078f)*2.2f + Noise(x*.24f,z*.24f+salt)*.22f;
            return Mathf.Max(0,body*top + talus - gully);
        }

        private static float Smooth(float low,float high,float value)
        {
            float t=Mathf.Clamp01((value-low)/(high-low)); return t*t*(3f-2f*t);
        }
        private static float Noise(float x,float z)
        {
            int ix=Mathf.FloorToInt(x),iz=Mathf.FloorToInt(z);
            float tx=x-ix,tz=z-iz;tx=tx*tx*(3-2*tx);tz=tz*tz*(3-2*tz);
            return Mathf.Lerp(Mathf.Lerp(Hash(ix,iz),Hash(ix+1,iz),tx),Mathf.Lerp(Hash(ix,iz+1),Hash(ix+1,iz+1),tx),tz);
        }
        private static float Hash(int x,int z)
        {
            unchecked { uint h=(uint)x*374761393u+(uint)z*668265263u+(uint)Seed;h=(h^(h>>13))*1274126177u;h^=h>>16;return (h&0xffffffu)/8388607.5f-1f; }
        }
        private static Vector2[] Curve(Vector2[] knots)
        {
            const int subdivisions=8;
            var points=new Vector2[(knots.Length-1)*subdivisions+1];int at=0;
            for(int i=0;i<knots.Length-1;i++) for(int j=0;j<subdivisions;j++)
            {
                float t=j/(float)subdivisions,t2=t*t,t3=t2*t;
                Vector2 a=knots[Mathf.Max(0,i-1)],b=knots[i],c=knots[i+1],d=knots[Mathf.Min(knots.Length-1,i+2)];
                points[at++]=.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t2+(-a+3*b-3*c+d)*t3);
            }
            points[at]=knots[knots.Length-1];return points;
        }
        private static float DistanceToCurve(float x,float z,Vector2[] points)
        {
            var p=new Vector2(x,z);float best=float.MaxValue;
            for(int i=0;i<points.Length-1;i++)
            {
                Vector2 a=points[i],delta=points[i+1]-a;
                float t=Mathf.Clamp01(Vector2.Dot(p-a,delta)/Mathf.Max(delta.sqrMagnitude,.000001f));
                best=Mathf.Min(best,(p-a-delta*t).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }
    }
}
