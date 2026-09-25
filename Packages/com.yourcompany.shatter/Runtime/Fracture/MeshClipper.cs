using System;
using System.Collections.Generic;
using UnityEngine;
using Shatter.Core;

namespace Shatter.Fracture
{
    /// <summary>
    /// Clips a ShatterMeshData against a single plane, producing two closed,
    /// capped meshes (one per side). This is the single most important piece
    /// of the whole toolkit - both VoronoiFracture (repeated clipping against
    /// bisector planes) and RuntimeSlicer (one clip) are built directly on top
    /// of this.
    ///
    /// v1 approach / known limitations (see design doc "Key Risks"):
    /// - Vertices exactly on the plane are bucketed to the positive side via a
    ///   deterministic tie-break (d >= 0 => front). This avoids a large amount
    ///   of special-case branching for a measure-zero case (exact coplanarity)
    ///   at the cost of not specially optimizing it. Revisit if pathological
    ///   input meshes (e.g. procedurally axis-aligned grids sliced on-axis)
    ///   turn out to be common.
    /// - Cap triangulation assumes each clip produces a single simple polygon
    ///   loop per resulting piece. Concave source meshes CAN produce multiple
    ///   loops (e.g. slicing a donut through the hole) - BuildLoops already
    ///   returns multiple loops correctly, but TriangulateCap does not yet
    ///   handle nested loops (holes-within-a-face). Flagged as a follow-up.
    /// </summary>
    public static class MeshClipper
    {
        public struct ClipResult
        {
            public ShatterMeshData positive;
            public ShatterMeshData negative;
            public bool wasClipped; // false if the plane didn't intersect the mesh at all
        }

        private const float PlaneEpsilon = 1e-6f;
        private const float WeldEpsilon = 1e-5f;

        /// <summary>
        /// Clips source against plane. Everything on the side the plane normal
        /// points to goes into `positive`; everything else into `negative`.
        /// Cut (interior) faces are capped and assigned interiorMaterial.
        /// </summary>
        public static ClipResult Clip(ShatterMeshData source, Plane plane, Material interiorMaterial)
        {
            var positive = new ShatterMeshData();
            var negative = new ShatterMeshData();

            // Maps an original source vertex index -> its index in the output mesh,
            // so shared vertices aren't duplicated for triangles that pass through untouched.
            var posMap = new Dictionary<int, int>();
            var negMap = new Dictionary<int, int>();

            // Raw cut segments collected while clipping triangles; stitched into
            // closed loops afterwards and capped.
            var cutSegments = new List<(Vector3 a, Vector3 b)>();

            bool anyFront = false, anyBack = false;

            foreach (var srcSubMesh in source.subMeshes)
            {
                var posSub = positive.GetOrCreateSubMesh(srcSubMesh.material, false);
                var negSub = negative.GetOrCreateSubMesh(srcSubMesh.material, false);

                var tris = srcSubMesh.triangles;
                for (int t = 0; t < tris.Count; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    ClipTriangle(
                        source, i0, i1, i2,
                        plane,
                        positive, posSub, posMap,
                        negative, negSub, negMap,
                        cutSegments,
                        ref anyFront, ref anyBack);
                }
            }

            // If every triangle landed on one side only, the plane never
            // actually intersected the mesh - nothing to cap, and the caller
            // should treat this as "no-op" (e.g. skip spawning an empty fragment).
            if (!(anyFront && anyBack))
            {
                return new ClipResult
                {
                    positive = anyFront ? positive : null,
                    negative = anyBack ? negative : null,
                    wasClipped = false
                };
            }

            CapCutFaces(cutSegments, plane, interiorMaterial, positive, negative);

            return new ClipResult { positive = positive, negative = negative, wasClipped = true };
        }

        // -----------------------------------------------------------------
        // Per-triangle clip
        // -----------------------------------------------------------------
        private static void ClipTriangle(
            ShatterMeshData source, int i0, int i1, int i2,
            Plane plane,
            ShatterMeshData posMesh, ShatterSubMesh posSub, Dictionary<int, int> posMap,
            ShatterMeshData negMesh, ShatterSubMesh negSub, Dictionary<int, int> negMap,
            List<(Vector3, Vector3)> cutSegments,
            ref bool anyFront, ref bool anyBack)
        {
            Span<int> idx = stackalloc int[3] { i0, i1, i2 };
            Span<float> dist = stackalloc float[3];
            Span<bool> front = stackalloc bool[3];

            int frontCount = 0;
            for (int k = 0; k < 3; k++)
            {
                dist[k] = plane.GetDistanceToPoint(source.vertices[idx[k]].position);
                front[k] = dist[k] >= 0f; // tie-break: on-plane counts as front
                if (front[k]) frontCount++;
            }

            if (frontCount == 3)
            {
                anyFront = true;
                AddExistingTriangle(source, idx[0], idx[1], idx[2], posMesh, posSub, posMap);
                return;
            }
            if (frontCount == 0)
            {
                anyBack = true;
                AddExistingTriangle(source, idx[0], idx[1], idx[2], negMesh, negSub, negMap);
                return;
            }

            anyFront = true;
            anyBack = true;

            // Rotate so the "lone" vertex (the one alone on its side) is at index 0.
            int lone = frontCount == 1
                ? (front[0] ? 0 : front[1] ? 1 : 2)
                : (!front[0] ? 0 : !front[1] ? 1 : 2);

            int a = idx[lone];
            int b = idx[(lone + 1) % 3];
            int c = idx[(lone + 2) % 3];
            bool loneIsFront = front[lone];

            ShatterVertex vA = source.vertices[a];
            ShatterVertex vB = source.vertices[b];
            ShatterVertex vC = source.vertices[c];

            float dA = plane.GetDistanceToPoint(vA.position);
            float dB = plane.GetDistanceToPoint(vB.position);
            float dC = plane.GetDistanceToPoint(vC.position);

            // X = intersection of edge A->B, Y = intersection of edge C->A
            float tAB = dA / (dA - dB);
            float tCA = dC / (dC - dA);
            ShatterVertex vX = ShatterVertex.Lerp(vA, vB, Mathf.Clamp01(tAB));
            ShatterVertex vY = ShatterVertex.Lerp(vC, vA, Mathf.Clamp01(tCA));

            var loneMesh = loneIsFront ? posMesh : negMesh;
            var loneSub = loneIsFront ? posSub : negSub;
            var otherMesh = loneIsFront ? negMesh : posMesh;
            var otherSub = loneIsFront ? negSub : posSub;

            int iA = GetOrAddSourceVertex(source, a, loneMesh, loneIsFront ? posMap : negMap);
            int iX_lone = loneMesh.AddVertex(vX);
            int iY_lone = loneMesh.AddVertex(vY);
            loneSub.triangles.Add(iA);
            loneSub.triangles.Add(iX_lone);
            loneSub.triangles.Add(iY_lone);

            int iB = GetOrAddSourceVertex(source, b, otherMesh, loneIsFront ? negMap : posMap);
            int iC = GetOrAddSourceVertex(source, c, otherMesh, loneIsFront ? negMap : posMap);
            int iX_other = otherMesh.AddVertex(vX);
            int iY_other = otherMesh.AddVertex(vY);
            // Quad (X, B, C, Y) in CCW order -> two triangles
            otherSub.triangles.Add(iX_other);
            otherSub.triangles.Add(iB);
            otherSub.triangles.Add(iC);

            otherSub.triangles.Add(iX_other);
            otherSub.triangles.Add(iC);
            otherSub.triangles.Add(iY_other);

            // Record the cut edge for loop-stitching. Direction is normalized
            // later when loops are built, so raw order here doesn't matter.
            cutSegments.Add((vX.position, vY.position));
        }

        private static void AddExistingTriangle(
            ShatterMeshData source, int i0, int i1, int i2,
            ShatterMeshData dstMesh, ShatterSubMesh dstSub, Dictionary<int, int> map)
        {
            dstSub.triangles.Add(GetOrAddSourceVertex(source, i0, dstMesh, map));
            dstSub.triangles.Add(GetOrAddSourceVertex(source, i1, dstMesh, map));
            dstSub.triangles.Add(GetOrAddSourceVertex(source, i2, dstMesh, map));
        }

        private static int GetOrAddSourceVertex(
            ShatterMeshData source, int srcIndex, ShatterMeshData dstMesh, Dictionary<int, int> map)
        {
            if (map.TryGetValue(srcIndex, out int existing))
                return existing;

            int newIndex = dstMesh.AddVertex(source.vertices[srcIndex]);
            map[srcIndex] = newIndex;
            return newIndex;
        }

        // -----------------------------------------------------------------
        // Capping: stitch raw cut segments into closed loops, triangulate,
        // and add the resulting cap faces to both output meshes with
        // opposing winding/normals.
        // -----------------------------------------------------------------
        private static void CapCutFaces(
            List<(Vector3 a, Vector3 b)> segments,
            Plane plane,
            Material interiorMaterial,
            ShatterMeshData positive,
            ShatterMeshData negative)
        {
            var loops = BuildLoops(segments);
            if (loops.Count == 0) return;

            var posCapSub = positive.GetOrCreateSubMesh(interiorMaterial, true);
            var negCapSub = negative.GetOrCreateSubMesh(interiorMaterial, true);

            // Build an orthonormal basis on the plane for 2D projection.
            Vector3 normal = plane.normal;
            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            foreach (var loop in loops)
            {
                if (loop.Count < 3) continue; // degenerate sliver, skip

                var loop2D = new List<Vector2>(loop.Count);
                foreach (var p in loop)
                    loop2D.Add(new Vector2(Vector3.Dot(p, tangent), Vector3.Dot(p, bitangent)));

                // Ensure CCW winding as seen looking down -normal (this is the
                // orientation the negative-side cap wants; positive side is
                // the reverse of every triangle we generate below).
                if (SignedArea(loop2D) < 0f)
                {
                    loop.Reverse();
                    loop2D.Reverse();
                }

                var earTriangles = EarClipTriangulate(loop2D);
                if (earTriangles == null) continue; // triangulation failed (self-intersecting/degenerate loop)

                for (int t = 0; t < earTriangles.Count; t += 3)
                {
                    Vector3 p0 = loop[earTriangles[t]];
                    Vector3 p1 = loop[earTriangles[t + 1]];
                    Vector3 p2 = loop[earTriangles[t + 2]];

                    // Negative side: normal = +plane.normal, winding as computed (CCW looking down -normal).
                    AddCapTriangle(negative, negCapSub, p0, p1, p2, normal);

                    // Positive side: normal = -plane.normal, winding reversed.
                    AddCapTriangle(positive, posCapSub, p0, p2, p1, -normal);
                }
            }
        }

        private static void AddCapTriangle(
            ShatterMeshData mesh, ShatterSubMesh sub, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 normal)
        {
            // Cap faces get flat UVs based on world position on the cut plane;
            // good enough default for a tiling "interior" material. Can be
            // swapped for a triplanar shader with no code changes here.
            int i0 = mesh.AddVertex(new ShatterVertex { position = p0, normal = normal, uv = PlanarUV(p0, normal), color = new Color32(255, 255, 255, 255) });
            int i1 = mesh.AddVertex(new ShatterVertex { position = p1, normal = normal, uv = PlanarUV(p1, normal), color = new Color32(255, 255, 255, 255) });
            int i2 = mesh.AddVertex(new ShatterVertex { position = p2, normal = normal, uv = PlanarUV(p2, normal), color = new Color32(255, 255, 255, 255) });
            sub.triangles.Add(i0);
            sub.triangles.Add(i1);
            sub.triangles.Add(i2);
        }

        private static Vector2 PlanarUV(Vector3 p, Vector3 normal)
        {
            Vector3 t = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
            Vector3 b = Vector3.Cross(normal, t);
            return new Vector2(Vector3.Dot(p, t), Vector3.Dot(p, b));
        }

        /// <summary>
        /// Greedily chains unordered segments into closed polylines by matching
        /// endpoints within WeldEpsilon. O(n^2) - fine for per-fragment segment
        /// counts (tens to low hundreds); revisit with a spatial hash if
        /// profiling shows this matters for very dense source meshes.
        /// </summary>
        private static List<List<Vector3>> BuildLoops(List<(Vector3 a, Vector3 b)> segments)
        {
            var remaining = new List<(Vector3 a, Vector3 b)>(segments);
            var loops = new List<List<Vector3>>();

            while (remaining.Count > 0)
            {
                var loop = new List<Vector3>();
                var (start, current) = remaining[0];
                remaining.RemoveAt(0);
                loop.Add(start);
                loop.Add(current);

                bool closed = false;
                int guard = segments.Count + 4; // safety against malformed input causing infinite loop
                while (!closed && guard-- > 0)
                {
                    int foundAt = -1;
                    bool flip = false;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        if (Approximately(remaining[i].a, current)) { foundAt = i; flip = false; break; }
                        if (Approximately(remaining[i].b, current)) { foundAt = i; flip = true; break; }
                    }

                    if (foundAt < 0) break; // open chain - malformed/degenerate input, stop here

                    var seg = remaining[foundAt];
                    remaining.RemoveAt(foundAt);
                    current = flip ? seg.a : seg.b;

                    if (Approximately(current, start))
                    {
                        closed = true;
                    }
                    else
                    {
                        loop.Add(current);
                    }
                }

                loops.Add(loop);
            }

            return loops;
        }

        private static bool Approximately(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= WeldEpsilon * WeldEpsilon;

        private static float SignedArea(List<Vector2> poly)
        {
            float area = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p0 = poly[i];
                Vector2 p1 = poly[(i + 1) % poly.Count];
                area += p0.x * p1.y - p1.x * p0.y;
            }
            return area * 0.5f;
        }

        /// <summary>
        /// Classic O(n^2) ear-clipping triangulation for a simple (possibly
        /// concave, non-self-intersecting) CCW polygon. Returns a flat list of
        /// indices into `poly`, or null if it fails (e.g. degenerate/self-intersecting input).
        /// </summary>
        private static List<int> EarClipTriangulate(List<Vector2> poly)
        {
            int n = poly.Count;
            if (n < 3) return null;

            var indices = new List<int>(n);
            for (int i = 0; i < n; i++) indices.Add(i);

            var result = new List<int>((n - 2) * 3);
            int guard = n * n + 8; // safety valve for pathological input

            while (indices.Count > 3 && guard-- > 0)
            {
                bool earFound = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int i0 = indices[(i - 1 + indices.Count) % indices.Count];
                    int i1 = indices[i];
                    int i2 = indices[(i + 1) % indices.Count];

                    if (!IsConvex(poly[i0], poly[i1], poly[i2])) continue;

                    bool anyInside = false;
                    for (int j = 0; j < indices.Count; j++)
                    {
                        int idx = indices[j];
                        if (idx == i0 || idx == i1 || idx == i2) continue;
                        if (PointInTriangle(poly[idx], poly[i0], poly[i1], poly[i2])) { anyInside = true; break; }
                    }
                    if (anyInside) continue;

                    result.Add(i0);
                    result.Add(i1);
                    result.Add(i2);
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound) return null; // couldn't find a valid ear - malformed polygon
            }

            if (indices.Count == 3)
            {
                result.Add(indices[0]);
                result.Add(indices[1]);
                result.Add(indices[2]);
            }

            return result;
        }

        private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
        {
            float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            return cross > 1e-9f;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b);
            float d2 = Cross(p, b, c);
            float d3 = Cross(p, c, a);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
    }
}
