using UnityEngine;
using Shatter.Fracture;

namespace Shatter.Core
{
    /// <summary>
    /// Attach this to any GameObject that should be sliceable/fracturable.
    /// It owns its own mesh state end-to-end: calling Slice() clips the
    /// object's *current* mesh against a world-space plane, turns this
    /// GameObject into the "positive" piece in place, and spawns a sibling
    /// GameObject (also carrying a ShatterObject) for the "negative" piece.
    ///
    /// Because the result is itself a normal ShatterObject, pieces can be
    /// sliced again recursively - which is exactly what VoronoiFracture will
    /// rely on later (a fracture is really just many slices in sequence).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class ShatterObject : MonoBehaviour
    {
        [Header("Cutting")]
        [Tooltip("Material assigned to newly-exposed interior faces created by a cut.")]
        [SerializeField] private Material interiorMaterial;

        [Header("Detail")]
        [Tooltip("Subdivides the mesh before cutting so the cut edge and fragment " +
                 "silhouettes carry more triangle detail (looks like it's actually " +
                 "breaking, not just cleanly sliced). Each level ~4x's the triangle " +
                 "count - keep this low (0-2) on anything but very simple meshes.")]
        [Range(0, 3)]
        [SerializeField] private int subdivisionLevel = 0;

        [Tooltip("If true, subdivision is only applied the first time this object " +
                 "is sliced. If false, every recursive cut re-subdivides the current " +
                 "mesh, compounding detail (and triangle count) with each cut.")]
        [SerializeField] private bool subdivideOnlyOnce = true;

        [Tooltip("Logs before/after triangle counts to the console on each slice - useful while tuning subdivisionLevel.")]
        [SerializeField] private bool logTriangleCounts = false;

        [Header("Physics")]
        [Tooltip("If true, a Rigidbody + convex MeshCollider are added/kept on this object and its pieces.")]
        [SerializeField] private bool simulatePhysics = true;
        [SerializeField] private float density = 1f;
        [SerializeField] private float minMass = 0.05f;
        [SerializeField] private float separationImpulse = 2f;

        [Header("Limits")]
        [Tooltip("Pieces smaller than this local-space volume are discarded instead of spawned (avoids sliver fragments).")]
        [SerializeField] private float minFragmentVolume = 0.0001f;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;
        private Rigidbody rb;
        private bool hasSubdivided;

        public bool SimulatePhysics => simulatePhysics;

        /// <summary>Current triangle count of this object's mesh, across all submeshes.</summary>
        public int CurrentTriangleCount
        {
            get
            {
                if (meshFilter == null || meshFilter.sharedMesh == null) return 0;
                int total = 0;
                var mesh = meshFilter.sharedMesh;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    total += (int)mesh.GetIndexCount(i) / 3;
                return total;
            }
        }

        private void Awake()
        {
            CacheComponents();
        }

        private void CacheComponents()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();

            if (simulatePhysics)
            {
                meshCollider = GetComponent<MeshCollider>();
                if (meshCollider == null) meshCollider = gameObject.AddComponent<MeshCollider>();
                meshCollider.convex = true;

                rb = GetComponent<Rigidbody>();
                if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            }
        }

        /// <summary>
        /// Slices this object with a plane defined in world space. Returns the
        /// newly-spawned piece (the "negative" side), or null if the plane
        /// didn't intersect this object's mesh (no-op).
        /// </summary>
        public ShatterObject Slice(Vector3 worldPlanePoint, Vector3 worldPlaneNormal)
        {
            if (meshFilter == null) CacheComponents();
            if (meshFilter.sharedMesh == null)
            {
                Debug.LogWarning($"[Shatter] '{name}' has no mesh assigned.", this);
                return null;
            }

            Transform tr = transform;
            Vector3 localPoint = tr.InverseTransformPoint(worldPlanePoint);
            Vector3 localNormal = tr.InverseTransformDirection(worldPlaneNormal).normalized;
            var localPlane = new Plane(localNormal, localPoint);

            var sourceData = ShatterMeshData.FromUnityMesh(meshFilter.sharedMesh, meshRenderer.sharedMaterials);

            int beforeCount = 0;
            if (logTriangleCounts) beforeCount = CountTriangles(sourceData);

            bool shouldSubdivide = subdivisionLevel > 0 && (!subdivideOnlyOnce || !hasSubdivided);
            if (shouldSubdivide)
            {
                sourceData = MeshSubdivider.Subdivide(sourceData, subdivisionLevel);
                hasSubdivided = true;
            }

            if (logTriangleCounts)
                Debug.Log($"[Shatter] '{name}' triangles before cut: {beforeCount} -> after subdivision: {CountTriangles(sourceData)}", this);

            var clip = MeshClipper.Clip(sourceData, localPlane, interiorMaterial);

            if (!clip.wasClipped)
            {
                // Plane missed the mesh entirely - nothing to do.
                return null;
            }

            bool positiveOK = clip.positive != null && MeshVolumeUtility.ComputeApproxVolume(clip.positive) >= minFragmentVolume;
            bool negativeOK = clip.negative != null && MeshVolumeUtility.ComputeApproxVolume(clip.negative) >= minFragmentVolume;

            ShatterObject spawnedPiece = null;

            if (negativeOK)
            {
                // Instantiate BEFORE mutating self, so the clone inherits this
                // object's current inspector settings (interior material,
                // density, subdivision settings, etc.) via the normal Unity
                // clone-then-diverge pattern.
                var clone = Instantiate(gameObject, tr.position, tr.rotation, tr.parent);
                clone.name = gameObject.name + "_frag";
                spawnedPiece = clone.GetComponent<ShatterObject>();
                spawnedPiece.CacheComponents();
                spawnedPiece.hasSubdivided = true; // inherits already-subdivided detail, don't redo it
                spawnedPiece.ApplyMeshData(clip.negative);
                spawnedPiece.ApplyImpulse(-worldPlaneNormal);
            }

            if (positiveOK)
            {
                ApplyMeshData(clip.positive);
                ApplyImpulse(worldPlaneNormal);
            }
            else
            {
                // The "self" side vanished entirely (e.g. sliced right at an edge) - remove it.
                Destroy(gameObject);
            }

            if (logTriangleCounts)
                Debug.Log($"[Shatter] Slice result - self: {CurrentTriangleCount} tris, piece: {(spawnedPiece != null ? spawnedPiece.CurrentTriangleCount.ToString() : "none")} tris", this);

            return spawnedPiece;
        }

        private void ApplyMeshData(ShatterMeshData data)
        {
            Mesh mesh = data.ToUnityMesh(gameObject.name);
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterials = data.GetMaterialArray();

            if (simulatePhysics && meshCollider != null)
            {
                // MeshCollider needs its shared mesh cleared before reassigning
                // one with different topology, or Unity keeps stale collision data.
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
                meshCollider.convex = true;
            }

            if (simulatePhysics && rb != null)
            {
                float volume = MeshVolumeUtility.ComputeApproxVolume(data);
                rb.mass = Mathf.Max(minMass, volume * density);
            }
        }

        private void ApplyImpulse(Vector3 worldDirection)
        {
            if (simulatePhysics && rb != null)
                rb.AddForce(worldDirection.normalized * separationImpulse, ForceMode.Impulse);
        }

        private static int CountTriangles(ShatterMeshData data)
        {
            int total = 0;
            foreach (var sub in data.subMeshes)
                total += sub.triangles.Count / 3;
            return total;
        }
    }
}