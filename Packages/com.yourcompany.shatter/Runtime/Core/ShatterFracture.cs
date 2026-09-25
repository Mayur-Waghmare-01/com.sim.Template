using System.Collections.Generic;
using UnityEngine;
using Shatter.Fracture;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Shatter.Core
{
    /// <summary>
    /// Attach to any mesh you want to fracture. Workflow (all in-editor, no
    /// Play Mode required):
    ///   1. Set fragment count / seed / detail.
    ///   2. Click "Generate Preview" (via the custom inspector) - computes the
    ///      actual fracture cells AND their connectivity/anchors, caches them,
    ///      and draws them as a colored wireframe + connectivity overlay.
    ///   3. Click "Fracture" to commit. With Bake As Cluster on (default),
    ///      spawns ONE ShatterCluster containing all fragments sharing a
    ///      single Rigidbody. With it off, spawns independent ShatterObjects.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class ShatterFracture : MonoBehaviour
    {
        [Header("Fracture")]
        [Min(2)]
        [SerializeField] private int fragmentCount = 8;
        [SerializeField] private int randomSeed = 12345;
        [SerializeField] private Material interiorMaterial;

        [Header("Detail")]
        [Range(0, 3)]
        [Tooltip("Subdivides the source mesh before fracturing, for more detailed cut silhouettes. Applied once, before seed generation.")]
        [SerializeField] private int subdivisionLevel = 0;

        [Header("Bake Mode")]
        [Tooltip("If true, fragments are baked as ONE ShatterCluster sharing a single Rigidbody with a connectivity graph - fragments can break off individually and disconnected sub-groups fall together. If false, each fragment becomes an independent ShatterObject with its own Rigidbody.")]
        [SerializeField] private bool bakeAsCluster = true;

        [Tooltip("Fragments whose lowest point is within this distance of the whole mesh's lowest point are treated as structurally anchored (e.g. touching the ground) and keep the cluster kinematic until they're broken off.")]
        [SerializeField] private float anchorBottomThreshold = 0.05f;

        [Header("Physics")]
        [SerializeField] private float density = 1f;
        [SerializeField] private float minMass = 0.05f;

        [System.NonSerialized] public List<Vector3> previewSeeds;
        [System.NonSerialized] public List<ShatterMeshData> previewFragments;
        [System.NonSerialized] public ConnectivityGraph previewGraph;
        [System.NonSerialized] public HashSet<int> previewAnchors;

        public int FragmentCount => fragmentCount;

        public void GeneratePreview()
        {
            var meshFilter = GetComponent<MeshFilter>();
            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogWarning($"[Shatter] '{name}' has no mesh to fracture.", this);
                return;
            }

            var sourceData = ShatterMeshData.FromUnityMesh(meshFilter.sharedMesh, meshRenderer.sharedMaterials);
            if (subdivisionLevel > 0)
                sourceData = MeshSubdivider.Subdivide(sourceData, subdivisionLevel);

            Bounds sourceBounds = sourceData.ComputeBounds();

            previewSeeds = VoronoiFracture.GenerateSeeds(sourceData, fragmentCount, randomSeed);
            var result = VoronoiFracture.Fracture(sourceData, previewSeeds, interiorMaterial);
            previewFragments = result.fragments;
            previewGraph = result.graph;

            previewAnchors = ComputeAnchors(previewFragments, sourceBounds);
            foreach (var a in previewAnchors)
                previewGraph.MarkAnchored(a);

            Debug.Log($"[Shatter] '{name}' preview: {previewFragments.Count}/{fragmentCount} fragments, {previewAnchors.Count} anchored.", this);
        }

        private HashSet<int> ComputeAnchors(List<ShatterMeshData> frags, Bounds sourceBounds)
        {
            var anchors = new HashSet<int>();
            float floor = sourceBounds.min.y + anchorBottomThreshold;
            for (int i = 0; i < frags.Count; i++)
                if (frags[i].ComputeBounds().min.y <= floor)
                    anchors.Add(i);
            return anchors;
        }

        public void ClearPreview()
        {
            previewSeeds = null;
            previewFragments = null;
            previewGraph = null;
            previewAnchors = null;
        }

        /// <summary>
        /// Commits the cached preview into real GameObjects and removes this
        /// object. Generates a preview first if none is cached yet.
        /// </summary>
        public void BakeFragments()
        {
            if (previewFragments == null || previewFragments.Count == 0)
                GeneratePreview();

            if (previewFragments == null || previewFragments.Count == 0)
            {
                Debug.LogWarning($"[Shatter] '{name}': fracture produced no fragments - nothing baked.", this);
                return;
            }

            if (bakeAsCluster)
                BakeCluster();
            else
                BakeIndependentFragments();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.DestroyObjectImmediate(gameObject);
                ClearPreview();
                return;
            }
#endif
            Destroy(gameObject);
            ClearPreview();
        }

        private void BakeCluster()
        {
            var go = new GameObject(name + "_cluster");
            go.transform.SetParent(transform.parent, false);

            var cluster = go.AddComponent<ShatterCluster>();
            cluster.Initialize(previewFragments, previewGraph, previewAnchors, interiorMaterial, transform);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RegisterCreatedObjectUndo(go, "Fracture");
#endif
        }

        private void BakeIndependentFragments()
        {
            Transform parent = transform.parent;

            foreach (var fragData in previewFragments)
            {
                var go = new GameObject(name + "_frag");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(transform.position, transform.rotation);
                go.transform.localScale = transform.localScale;

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = fragData.ToUnityMesh(go.name);

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = fragData.GetMaterialArray();

                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = true;

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mathf.Max(minMass, MeshVolumeUtility.ComputeApproxVolume(fragData) * density);

                go.AddComponent<ShatterObject>();

#if UNITY_EDITOR
                if (!Application.isPlaying)
                    Undo.RegisterCreatedObjectUndo(go, "Fracture");
#endif
            }
        }
    }
}