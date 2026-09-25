using UnityEngine;

namespace Shatter.Core
{
    /// <summary>
    /// Geometry queries against ShatterMeshData that go beyond simple bounds
    /// checks. Currently just point-in-mesh (used for fracture seed
    /// placement) - kept as its own utility since VoronoiFracture, and later
    /// damage/proximity queries, will both need it.
    /// </summary>
    public static class MeshGeometryUtility
    {
        // An arbitrary non-axis-aligned direction. Using a "clean" axis (like
        // straight up) makes coincidental exact edge/vertex grazes far more
        // likely on authored meshes (which are often axis-aligned), which is
        // exactly the degenerate case a parity ray test is most fragile against.
        private static readonly Vector3 TestDirection = new Vector3(0.9153f, 1.1091f, 0.6432f).normalized;

        /// <summary>
        /// Parity ray-casting point-in-mesh test: casts one ray from `point`
        /// and counts triangle crossings; odd = inside. Assumes a closed/
        /// manifold mesh (true for MeshClipper output and most authored
        /// assets). Not bulletproof against pathological geometry - for
        /// fracture seed sampling, an occasional misclassified point just
        /// yields a slightly different (still valid) seed layout, so this
        /// tradeoff is fine here specifically.
        /// </summary>
        public static bool IsPointInside(ShatterMeshData data, Vector3 point)
        {
            int hitCount = 0;
            foreach (var sub in data.subMeshes)
            {
                var tris = sub.triangles;
                for (int i = 0; i < tris.Count; i += 3)
                {
                    Vector3 a = data.vertices[tris[i]].position;
                    Vector3 b = data.vertices[tris[i + 1]].position;
                    Vector3 c = data.vertices[tris[i + 2]].position;
                    if (RayIntersectsTriangle(point, TestDirection, a, b, c))
                        hitCount++;
                }
            }
            return (hitCount % 2) == 1;
        }

        // Moller-Trumbore ray-triangle intersection, forward-along-ray only.
        private static bool RayIntersectsTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c)
        {
            const float eps = 1e-7f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 h = Vector3.Cross(dir, edge2);
            float det = Vector3.Dot(edge1, h);
            if (det > -eps && det < eps) return false;

            float invDet = 1f / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, h) * invDet;
            if (u < 0f || u > 1f) return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(dir, q) * invDet;
            if (v < 0f || u + v > 1f) return false;

            float t = Vector3.Dot(edge2, q) * invDet;
            return t > eps;
        }
    }
}
