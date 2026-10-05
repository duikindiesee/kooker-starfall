using System.Collections.Generic;
using UnityEngine;

namespace CityLife.World
{
    /// <summary>
    /// Small reusable meshes, combined once into deterministic per-chunk scene meshes. Quiver branching
    /// proportions and palette derive from CityLife R3FQuiverTrees.tsx, not a generic pine asset.
    /// </summary>
    public static class IslandFloraGeometry
    {
        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> triangles = new List<int>();

            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
            {
                int start = vertices.Count;
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                normals.Add(n); normals.Add(n); normals.Add(n);
                colors.Add(color); colors.Add(color); colors.Add(color);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }

            public void TaperedBranch(Vector3 from, Vector3 to, float radiusBottom, float radiusTop, int sides, Color color)
            {
                Vector3 axis = (to - from).normalized;
                Vector3 right = Vector3.Cross(axis, Mathf.Abs(axis.y) > .95f ? Vector3.right : Vector3.up).normalized;
                Vector3 front = Vector3.Cross(axis, right).normalized;
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides, b = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 d0 = Mathf.Cos(a) * right + Mathf.Sin(a) * front;
                    Vector3 d1 = Mathf.Cos(b) * right + Mathf.Sin(b) * front;
                    Vector3 p0 = from + d0 * radiusBottom, p1 = from + d1 * radiusBottom;
                    Vector3 q0 = to + d0 * radiusTop, q1 = to + d1 * radiusTop;
                    Triangle(p0, p1, q0, color);
                    Triangle(p1, q1, q0, color);
                }
            }

            static readonly int[] IcoFaces = {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };

            public void Icosahedron(Vector3 position, Vector3 scale, Color color, bool weathered = false)
            {
                float t = (1f + Mathf.Sqrt(5)) * .5f;
                var points = new[] {
                    new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),
                    new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),
                    new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1) };
                for (int i = 0; i < points.Length; i++)
                {
                    float variation = weathered ? .83f + (IslandRenderer.Hash((uint)i + 41) & 255) / 255f * .3f : 1f;
                    points[i] = position + Vector3.Scale(points[i].normalized, scale) * variation;
                }
                for (int i = 0; i < IcoFaces.Length; i += 3)
                    Triangle(points[IcoFaces[i]], points[IcoFaces[i + 1]], points[IcoFaces[i + 2]], color);
            }

            public Mesh Finish(string label)
            {
                var mesh = new Mesh { name = label };
                mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
                // Keep these small source meshes readable for the one-time chunk combination.
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        public static Mesh QuiverTree()
        {
            var b = new Builder();
            Color bark = IslandField.Hex(0xd8c9a3), leaf = IslandField.Hex(0x6f9e84);
            b.TaperedBranch(Vector3.zero, Vector3.up * .5f, .13f, .055f, 7, bark);
            Fork(b, new Vector3(0,.5f,0), .42f, .16f, .26f, .052f, 2, bark, leaf);
            Fork(b, new Vector3(0,.5f,0), -.42f, -.16f, .26f, .052f, 2, bark, leaf);
            return b.Finish("CityLife kokerboom · tapered trunk, three dichotomous forks, eight rosettes");
        }

        static void Fork(Builder b, Vector3 from, float dx, float dz, float length, float radius, int depth, Color bark, Color leaf)
        {
            Vector3 to = from + new Vector3(dx * length * .95f, length * .58f, dz * length * .95f);
            b.TaperedBranch(from, to, radius, radius * .6f, 6, bark);
            if (depth > 0)
            {
                float px = -dz, pz = dx;
                Fork(b, to, dx * .5f + px * .95f, dz * .5f + pz * .95f, length * .78f, radius * .66f, depth - 1, bark, leaf);
                Fork(b, to, dx * .5f - px * .95f, dz * .5f - pz * .95f, length * .58f, radius * .62f, depth - 1, bark, leaf);
            }
            else
                b.Icosahedron(to + Vector3.up * radius * 1.4f, new Vector3(1.1f,.7f,1.1f) * radius * 2.3f, leaf);
        }

        public static Mesh Rock()
        {
            var b = new Builder();
            b.Icosahedron(Vector3.up * .35f, new Vector3(1f,.8f,.9f), IslandField.Hex(0x9c7b62), true);
            return b.Finish("Low-poly weathered desert stone");
        }

        public static Mesh NeonPlant()
        {
            var b = new Builder();
            Color stem = IslandField.Hex(0x49616c), cyan = IslandField.Hex(0x43d7bc), violet = IslandField.Hex(0xc585c5);
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 2.399963f;
                Vector3 end = new Vector3(Mathf.Cos(angle) * .32f, .65f + i * .23f, Mathf.Sin(angle) * .32f);
                b.TaperedBranch(Vector3.zero, end, .05f, .018f, 5, stem);
                b.Icosahedron(end, new Vector3(.25f,.14f,.25f), i == 3 ? violet : cyan);
            }
            return b.Finish("Sparse CityLife neon hollow flora");
        }

        public static Mesh DryGrass()
        {
            var b = new Builder();
            Color straw = IslandField.Hex(0xbca16a), tip = IslandField.Hex(0xcfb985);
            // Six bent tapered blades: open dry tufts, not a lawn or forest carpet.
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 2.399963f;
                Vector3 outwards = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 across = new Vector3(-outwards.z, 0, outwards.x);
                float height = .60f + (i % 3) * .20f;
                Vector3 root = outwards * .08f;
                Vector3 middle = outwards * .22f + Vector3.up * height * .58f;
                Vector3 end = outwards * .46f + Vector3.up * height;
                Vector3 a = root - across * .047f, c = root + across * .047f;
                Vector3 d = middle - across * .023f, e = middle + across * .023f;
                b.Triangle(a, d, c, straw);
                b.Triangle(c, d, e, straw);
                b.Triangle(d, end, e, tip);
            }
            return b.Finish("Procedural arid six-blade grass tuft");
        }
    }
}
