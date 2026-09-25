using UnityEngine;
using Shatter.Core;
using Shatter.Fracture;

namespace Shatter.Slicing
{
    /// <summary>
    /// Slices a GameObject's mesh along a world-space plane at runtime,
    /// replacing it with two physically simulated fragment pieces.
    /// This is intentionally the first "real feature" built on MeshClipper -
    /// it's a thin wrapper, so any bug here is almost certainly a clipper bug.
    /// </summary>
    public static class RuntimeSlicer
    {
        public struct SliceOutput
        {
            public GameObject positive;
            public GameObject negative;
            public bool success;
        }

        /// <summary>
        /// Slices `target` with a plane defined in world space.
        /// `target` must have a MeshFilter + MeshRenderer. The original
        /// GameObject is deactivated (not destroyed - caller decides pooling/cleanup).
        /// </summary>
        public static SliceOutput Slice(
            GameObject target,
            Vector3 worldPlanePoint,
            Vector3 worldPlaneNormal,
            Material interiorMaterial,
            float separationForce = 2f)
        {
            var meshFilter = target.GetComponent<MeshFilter>();
            var meshRenderer = target.GetComponent<MeshRenderer>();
            if (meshFilter == null || meshFilter.sharedMesh == null || meshRenderer == null)
            {
                Debug.LogWarning($"[Shatter] RuntimeSlicer: '{target.name}' has no mesh to slice.");
                return new SliceOutput { success = false };
            }

            // Clip works in local space of the source mesh, so transform the
            // world-space cut plane into the target's local space first.
            Transform tr = target.transform;
            Vector3 localPoint = tr.InverseTransformPoint(worldPlanePoint);
            Vector3 localNormal = tr.InverseTransformDirection(worldPlaneNormal).normalized;
            Plane localPlane = new Plane(localNormal, localPoint);

            var sourceData = ShatterMeshData.FromUnityMesh(meshFilter.sharedMesh, meshRenderer.sharedMaterials);
            var result = MeshClipper.Clip(sourceData, localPlane, interiorMaterial);

            if (!result.wasClipped)
            {
                // Plane didn't intersect the mesh at all - nothing to do.
                return new SliceOutput { success = false };
            }

            GameObject posGO = BuildFragment(target, result.positive, "_Piece_A");
            GameObject negGO = BuildFragment(target, result.negative, "_Piece_B");

            // Small separating impulse so coincident geometry doesn't visibly
            // z-fight / interpenetrate the instant physics takes over.
            ApplySeparationImpulse(posGO, worldPlaneNormal, separationForce);
            ApplySeparationImpulse(negGO, -worldPlaneNormal, separationForce);

            target.SetActive(false);

            return new SliceOutput { positive = posGO, negative = negGO, success = true };
        }

        private static GameObject BuildFragment(GameObject source, ShatterMeshData data, string suffix)
        {
            var go = new GameObject(source.name + suffix);
            go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            go.transform.localScale = source.transform.localScale;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = data.ToUnityMesh(go.name);

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = data.GetMaterialArray();

            // Convex hull collider: fragments from clipping are near-convex by
            // construction for reasonably-convex source pieces, and Unity's
            // convex MeshCollider is required for a non-kinematic Rigidbody
            // anyway. Concave source meshes may need decomposition later -
            // flagged as a known limitation, not handled in v1.
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = true;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.05f, ComputeApproxVolume(data)); // placeholder density=1; replace with per-material density later

            return go;
        }

        private static void ApplySeparationImpulse(GameObject go, Vector3 worldDir, float force)
        {
            if (go == null) return;
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
                rb.AddForce(worldDir.normalized * force, ForceMode.Impulse);
        }

        /// <summary>Rough tetrahedral-decomposition volume estimate for mass approximation.</summary>
        private static float ComputeApproxVolume(ShatterMeshData data)
        {
            float volume = 0f;
            foreach (var sub in data.subMeshes)
            {
                var tris = sub.triangles;
                for (int i = 0; i < tris.Count; i += 3)
                {
                    Vector3 p0 = data.vertices[tris[i]].position;
                    Vector3 p1 = data.vertices[tris[i + 1]].position;
                    Vector3 p2 = data.vertices[tris[i + 2]].position;
                    volume += Vector3.Dot(p0, Vector3.Cross(p1, p2)) / 6f;
                }
            }
            return Mathf.Abs(volume);
        }
    }
}
