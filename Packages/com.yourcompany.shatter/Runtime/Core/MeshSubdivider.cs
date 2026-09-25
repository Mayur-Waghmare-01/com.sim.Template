using System.Collections.Generic;

namespace Shatter.Core
{
    /// <summary>
    /// Uniformly subdivides a ShatterMeshData by splitting every triangle into
    /// 4 (classic midpoint subdivision), interpolating position/normal/uv/color
    /// at each new edge midpoint. Used to add polygon detail to a source mesh
    /// BEFORE cutting, so the cut edge and resulting fragment silhouettes carry
    /// more triangle detail instead of a few large flat facets - this is what
    /// makes a break read as "shattering" rather than "cleanly sliced".
    ///
    /// Cost: each level multiplies triangle count by ~4 (1 level: x4, 2: x16,
    /// 3: x64). This runs once per call on the CPU, not per frame, but is still
    /// O(triangles) - keep levels low (0-2) on anything but very simple source
    /// meshes, and prefer applying it once rather than on every recursive cut.
    /// </summary>
    public static class MeshSubdivider
    {
        public static ShatterMeshData Subdivide(ShatterMeshData source, int levels)
        {
            if (levels <= 0) return source;

            var current = source;
            for (int i = 0; i < levels; i++)
                current = SubdivideOnce(current);

            return current;
        }

        private static ShatterMeshData SubdivideOnce(ShatterMeshData source)
        {
            var result = new ShatterMeshData
            {
                vertices = new List<ShatterVertex>(source.vertices)
            };

            // Shared cache so two triangles sharing an edge reuse the same new
            // midpoint vertex, instead of each creating its own -> which would
            // leave a visible seam/crack along every subdivided edge.
            var midpointCache = new Dictionary<long, int>();

            foreach (var srcSub in source.subMeshes)
            {
                var dstSub = result.GetOrCreateSubMesh(srcSub.material, srcSub.isInteriorFace);
                var tris = srcSub.triangles;

                for (int t = 0; t < tris.Count; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];

                    int ab = GetOrCreateMidpoint(result, midpointCache, a, b);
                    int bc = GetOrCreateMidpoint(result, midpointCache, b, c);
                    int ca = GetOrCreateMidpoint(result, midpointCache, c, a);

                    // 1 triangle -> 4: three corner triangles + one center triangle.
                    AddTri(dstSub, a, ab, ca);
                    AddTri(dstSub, b, bc, ab);
                    AddTri(dstSub, c, ca, bc);
                    AddTri(dstSub, ab, bc, ca);
                }
            }

            return result;
        }

        private static void AddTri(ShatterSubMesh sub, int a, int b, int c)
        {
            sub.triangles.Add(a);
            sub.triangles.Add(b);
            sub.triangles.Add(c);
        }

        private static int GetOrCreateMidpoint(ShatterMeshData mesh, Dictionary<long, int> cache, int i0, int i1)
        {
            long key = EdgeKey(i0, i1);
            if (cache.TryGetValue(key, out int existing))
                return existing;

            var mid = ShatterVertex.Lerp(mesh.vertices[i0], mesh.vertices[i1], 0.5f);
            int newIndex = mesh.AddVertex(mid);
            cache[key] = newIndex;
            return newIndex;
        }

        // Order-independent edge key so (a,b) and (b,a) resolve to the same midpoint.
        private static long EdgeKey(int a, int b)
        {
            if (a > b) { int tmp = a; a = b; b = tmp; }
            return ((long)a << 32) | (uint)b;
        }
    }
}
