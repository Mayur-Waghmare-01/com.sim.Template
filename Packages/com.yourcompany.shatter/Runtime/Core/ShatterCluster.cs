using System.Collections.Generic;
using UnityEngine;

namespace Shatter.Core
{
    /// <summary>
    /// A group of fragments sharing ONE Rigidbody - the compound collider
    /// pattern: each fragment is a child transform with its own convex
    /// MeshCollider and no Rigidbody of its own, so Unity treats every child
    /// collider as one combined shape under this GameObject's Rigidbody. This
    /// is what keeps a 100+ fragment structure from meaning 100+ live
    /// Rigidbodies from frame one.
    ///
    /// BreakFragment(nodeId) simulates that one fragment being destroyed or
    /// knocked loose entirely: it's removed from the ConnectivityGraph, and
    /// the graph's connected components are recomputed among what's left. Any
    /// component still touching an anchor stays merged into THIS cluster; any
    /// component with no anchor at all gets split off into a brand new
    /// ShatterCluster with its own Rigidbody (dynamic, since it has no
    /// support) - "the wall loses its base and that whole section falls".
    /// </summary>
    [DisallowMultipleComponent]
    public class ShatterCluster : MonoBehaviour
    {
        [Header("Physics")]
        [SerializeField] private float density = 1f;
        [SerializeField] private float minMass = 0.05f;

        [Header("Breaking")]
        [Tooltip("Minimum collision impulse magnitude on a fragment before it breaks out of the cluster. Tune per scene scale/mass.")]
        [SerializeField] private float breakImpulseThreshold = 5f;

        private class FragmentSlot
        {
            public int nodeId;
            public GameObject go;
            public MeshCollider collider;
            public ShatterMeshData meshData;
        }

        private readonly Dictionary<int, FragmentSlot> fragments = new Dictionary<int, FragmentSlot>();
        private readonly Dictionary<Collider, int> colliderToNode = new Dictionary<Collider, int>();
        private ConnectivityGraph graph;
        private Rigidbody rb;
        private Material interiorMaterial;

        /// <summary>
        /// Builds a cluster from fracture output. `anchoredFragmentIndices`
        /// marks which fragments are structurally fixed (e.g. touching the
        /// ground) - a cluster containing at least one anchored fragment
        /// stays kinematic; one with none starts fully dynamic.
        /// </summary>
        public void Initialize(
            List<ShatterMeshData> fragmentMeshes,
            ConnectivityGraph sourceGraph,
            HashSet<int> anchoredFragmentIndices,
            Material interiorMat,
            Transform sourceTransform)
        {
            interiorMaterial = interiorMat;
            graph = sourceGraph.Clone();

            transform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);
            transform.localScale = sourceTransform.localScale;

            rb = gameObject.AddComponent<Rigidbody>();

            for (int i = 0; i < fragmentMeshes.Count; i++)
            {
                if (anchoredFragmentIndices != null && anchoredFragmentIndices.Contains(i))
                    graph.MarkAnchored(i);

                CreateFragmentChild(i, fragmentMeshes[i]);
            }

            RefreshPhysicsState();
        }

        private void CreateFragmentChild(int nodeId, ShatterMeshData data)
        {
            var go = new GameObject($"{name}_piece{nodeId}");
            go.transform.SetParent(transform, false);

            var mf = go.AddComponent<MeshFilter>();
            Mesh mesh = data.ToUnityMesh(go.name);
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = data.GetMaterialArray();

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;

            fragments[nodeId] = new FragmentSlot { nodeId = nodeId, go = go, collider = mc, meshData = data };
            colliderToNode[mc] = nodeId;
        }

        private void RefreshPhysicsState()
        {
            if (rb == null) return;

            float totalMass = 0f;
            foreach (var slot in fragments.Values)
                totalMass += MeshVolumeUtility.ComputeApproxVolume(slot.meshData) * density;
            rb.mass = Mathf.Max(minMass, totalMass);

            bool anyAnchored = false;
            foreach (var nodeId in fragments.Keys)
            {
                if (graph.IsAnchored(nodeId)) { anyAnchored = true; break; }
            }

            rb.isKinematic = anyAnchored;
        }

        // Compound colliders report collisions to the GameObject holding the
        // Rigidbody (this one), not to each child collider individually -
        // Collision.GetContact gives us thisCollider so we can tell exactly
        // which fragment was actually hit.
        private void OnCollisionEnter(Collision collision)
        {
            float impulseMag = collision.impulse.magnitude;
            if (impulseMag < breakImpulseThreshold) return;

            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                if (colliderToNode.TryGetValue(contact.thisCollider, out int nodeId))
                {
                    BreakFragment(nodeId);
                    break; // one break per collision event for v1 - revisit for high-energy multi-fragment impacts
                }
            }
        }

        /// <summary>
        /// Looks up which fragment a specific Collider belongs to (e.g. from a
        /// Physics.OverlapSphere/Raycast hit) and breaks it. Returns false if
        /// the collider isn't one of this cluster's fragments. This is the
        /// entry point explosion/shoot forces use to detach a specific hit
        /// fragment without needing to know node ids.
        /// </summary>
        public bool TryBreakByCollider(Collider col)
        {
            if (colliderToNode.TryGetValue(col, out int nodeId))
            {
                BreakFragment(nodeId);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Breaks a specific fragment out of the structure entirely (destroyed
        /// / ejected - not just "this cluster splits into still-whole pieces").
        /// Can also be called directly by damage/explosion/gun systems later,
        /// not just from collision impacts.
        /// </summary>
        public void BreakFragment(int nodeId)
        {
            if (!fragments.TryGetValue(nodeId, out var slot)) return;

            colliderToNode.Remove(slot.collider);
            fragments.Remove(nodeId);
            graph.RemoveNode(nodeId);
            Destroy(slot.go); // TODO: swap for pooled debris/particle spawn later instead of a bare Destroy

            ResolveConnectivity();
        }

        /// <summary>
        /// After the graph changes, recompute connected components among the
        /// fragments still in this cluster. Components still touching an
        /// anchor remain part of THIS cluster object; anchor-less components
        /// split off into their own new (dynamic) ShatterCluster.
        /// </summary>
        private void ResolveConnectivity()
        {
            if (fragments.Count == 0)
            {
                Destroy(gameObject);
                return;
            }

            var components = graph.GetConnectedComponents();

            if (components.Count <= 1)
            {
                // Still one connected group - just refresh mass/kinematic
                // state, since it may have lost its only anchor.
                RefreshPhysicsState();
                return;
            }

            foreach (var component in components)
            {
                if (graph.ComponentHasAnchor(component))
                    continue; // stays part of this cluster

                SpawnDetachedCluster(component);
                foreach (var nodeId in component)
                {
                    colliderToNode.Remove(fragments[nodeId].collider);
                    fragments.Remove(nodeId);
                    graph.RemoveNode(nodeId);
                }
            }

            if (fragments.Count == 0)
                Destroy(gameObject);
            else
                RefreshPhysicsState();
        }

        private void SpawnDetachedCluster(List<int> componentNodeIds)
        {
            var go = new GameObject(name + "_detached");
            go.transform.SetPositionAndRotation(transform.position, transform.rotation);
            go.transform.localScale = transform.localScale;

            var newCluster = go.AddComponent<ShatterCluster>();
            newCluster.interiorMaterial = interiorMaterial;
            newCluster.density = density;
            newCluster.minMass = minMass;
            newCluster.breakImpulseThreshold = breakImpulseThreshold;
            newCluster.graph = new ConnectivityGraph();
            newCluster.rb = go.AddComponent<Rigidbody>();

            foreach (var nodeId in componentNodeIds)
            {
                var slot = fragments[nodeId];
                slot.go.transform.SetParent(go.transform, true);

                newCluster.fragments[nodeId] = slot;
                newCluster.colliderToNode[slot.collider] = nodeId;
                newCluster.graph.AddNode(nodeId);
            }

            // Re-link edges between nodes that both ended up in this component.
            var idSet = new HashSet<int>(componentNodeIds);
            foreach (var nodeId in componentNodeIds)
                foreach (var neighbor in graph.GetNeighbors(nodeId))
                    if (idSet.Contains(neighbor))
                        newCluster.graph.AddEdge(nodeId, neighbor);

            // No anchors by construction (ComponentHasAnchor was false above) - fully dynamic.
            newCluster.RefreshPhysicsState();
        }
    }
}