using UnityEngine;

namespace Shatter.Core
{
    // Add this next to ShatterCluster on the cluster's root object.
    // Gives every already-created fragment a ChunkDamage so Cowsins' guns
    // can shoot the cluster piece by piece.
    [RequireComponent(typeof(ShatterCluster))]
    public class ClusterCowsinsSetup : MonoBehaviour
    {
        [SerializeField] float chunkHealth = 100f;

        void Start()
        {
            var cluster = GetComponent<ShatterCluster>();
            foreach (var col in GetComponentsInChildren<MeshCollider>())
            {
                if (col.GetComponent<ChunkDamage>() != null) continue;
                col.gameObject.AddComponent<ChunkDamage>().Init(cluster, chunkHealth);
            }
        }
    }
}
