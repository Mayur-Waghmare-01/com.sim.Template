using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Shatter.Core
{
    /// <summary>
    /// Drop this on ANY GameObject with a MeshFilter (any imported model or
    /// primitive) to subdivide its mesh directly in the Scene/Inspector - no
    /// Play Mode required. This is separate from ShatterObject on purpose:
    /// it has no physics/cutting dependencies, so it works as a pure
    /// "increase the polycount of this mesh" preview/modify tool, the way
    /// RayFire's editor-side mesh operations do.
    ///
    /// Always re-subdivides from the cached ORIGINAL mesh (not from the
    /// already-subdivided result), so moving the slider up/down gives
    /// predictable triangle counts instead of compounding on every call.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    public class MeshEditTools : MonoBehaviour
    {
        [Header("Subdivision")]
        [Range(0, 3)]
        [Tooltip("Each level ~4x's the triangle count. 0 = original mesh.")]
        [SerializeField] private int subdivisionLevel = 1;

        [Tooltip("Automatically cached the first time you subdivide - the mesh this object had before any edits, used by Revert.")]
        [SerializeField] private Mesh originalMesh;

        private MeshFilter meshFilter;

        public int SubdivisionLevel
        {
            get => subdivisionLevel;
            set => subdivisionLevel = Mathf.Clamp(value, 0, 3);
        }

        public int CurrentTriangleCount
        {
            get
            {
                var mf = GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) return 0;
                int total = 0;
                var mesh = mf.sharedMesh;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    total += (int)mesh.GetIndexCount(i) / 3;
                return total;
            }
        }

        public int OriginalTriangleCount
        {
            get
            {
                if (originalMesh == null) return CurrentTriangleCount;
                int total = 0;
                for (int i = 0; i < originalMesh.subMeshCount; i++)
                    total += (int)originalMesh.GetIndexCount(i) / 3;
                return total;
            }
        }

        [ContextMenu("Shatter/Subdivide Mesh")]
        public void SubdivideNow()
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogWarning($"[Shatter] '{name}' has no mesh to subdivide.", this);
                return;
            }

            if (originalMesh == null)
                originalMesh = meshFilter.sharedMesh;

            if (subdivisionLevel <= 0)
            {
                RevertToOriginal();
                return;
            }

            var renderer = GetComponent<MeshRenderer>();
            var materials = renderer != null ? renderer.sharedMaterials : null;

            var sourceData = ShatterMeshData.FromUnityMesh(originalMesh, materials);
            var subdivided = MeshSubdivider.Subdivide(sourceData, subdivisionLevel);
            var newMesh = subdivided.ToUnityMesh($"{originalMesh.name}_subdiv{subdivisionLevel}");

            AssignMesh(newMesh);

            Debug.Log($"[Shatter] '{name}' subdivided: {CountTriangles(sourceData)} -> {CountTriangles(subdivided)} triangles.", this);
        }

        [ContextMenu("Shatter/Revert To Original Mesh")]
        public void RevertToOriginal()
        {
            if (originalMesh == null) return;
            AssignMesh(originalMesh);
        }

        private void AssignMesh(Mesh mesh)
        {
            meshFilter = meshFilter != null ? meshFilter : GetComponent<MeshFilter>();

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(meshFilter, "Shatter Mesh Edit");
#endif
            meshFilter.sharedMesh = mesh;

            var collider = GetComponent<MeshCollider>();
            if (collider != null)
            {
                collider.sharedMesh = null;
                collider.sharedMesh = mesh;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(this);
                EditorUtility.SetDirty(meshFilter);
            }
#endif
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
