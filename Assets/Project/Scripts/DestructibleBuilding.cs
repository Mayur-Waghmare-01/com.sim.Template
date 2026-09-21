using System.Collections.Generic;
using UnityEngine;

// Put this on the root of a pre-fractured building. Every child with a MeshFilter is a chunk.
public class DestructibleBuilding : MonoBehaviour
{
    [SerializeField] float linkPadding = 0.1f;     // how close chunks must be to count as connected
    [SerializeField] float groundTolerance = 0.2f; // chunks this near the lowest point are anchored
    [SerializeField] float debrisLifetime = 8f;
    [SerializeField] float explosionForce = 800f;
    [SerializeField] float chunkHealth = 100f;     // bullets chip this down, chunk breaks at 0
    [SerializeField] float bulletForce = 6f;

    class Chunk
    {
        public float health;
        public Collider col;
        public Rigidbody rb;
        public bool anchored, broken;
        public List<Chunk> links = new();
    }

    readonly Dictionary<Collider, Chunk> map = new();
    readonly List<Chunk> chunks = new();

    void Start()
    {
        float baseY = float.MaxValue;

        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.convex = true;
            var rb = mf.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            var c = new Chunk { col = mc, rb = rb, health = chunkHealth };
            chunks.Add(c);
            map[mc] = c;
            baseY = Mathf.Min(baseY, mc.bounds.min.y);
        }

        Physics.SyncTransforms();

        foreach (var c in chunks)
        {
            c.anchored = c.col.bounds.min.y <= baseY + groundTolerance;
            var b = c.col.bounds;
            var hits = Physics.OverlapBox(b.center, b.extents + Vector3.one * linkPadding);
            foreach (var h in hits)
                if (h != c.col && map.TryGetValue(h, out var other)) c.links.Add(other);
        }
    }

    public void Explode(Vector3 pos, float radius)
    {
        foreach (var c in chunks)
        {
            if (c.broken) continue;
            if (Vector3.Distance(c.col.ClosestPoint(pos), pos) <= radius) Break(c, pos, radius);
        }
        CheckIntegrity(pos, radius);
    }

    void Break(Chunk c, Vector3 pos, float radius)
    {
        c.broken = true;
        c.rb.isKinematic = false;
        c.rb.AddExplosionForce(explosionForce, pos, radius * 1.5f, 0.3f);
        Destroy(c.col.gameObject, debrisLifetime + Random.value * 3f);
    }

    // Any chunk no longer connected to the ground through intact chunks falls.
    void CheckIntegrity(Vector3 pos, float radius)
    {
        var visited = new HashSet<Chunk>();
        var stack = new Stack<Chunk>();

        foreach (var c in chunks)
            if (c.anchored && !c.broken && visited.Add(c)) stack.Push(c);

        while (stack.Count > 0)
            foreach (var n in stack.Pop().links)
                if (!n.broken && visited.Add(n)) stack.Push(n);

        foreach (var c in chunks)
            if (!c.broken && !visited.Contains(c)) Break(c, pos, radius * 2f);
    }

    // Bullet hit: small radius, damage accumulates, chunks get pushed along the bullet direction.
    public void Hit(Vector3 point, Vector3 dir, float radius, float damage)
    {
        bool anyBroken = false;

        foreach (var c in chunks)
        {
            if (c.broken) continue;
            float d = Vector3.Distance(c.col.ClosestPoint(point), point);
            if (d > radius) continue;

            c.health -= damage * (1f - d / radius);
            if (c.health > 0f) continue;

            c.broken = true;
            c.rb.isKinematic = false;
            c.rb.AddForceAtPosition(dir.normalized * bulletForce, point, ForceMode.Impulse);
            Destroy(c.col.gameObject, debrisLifetime + Random.value * 3f);
            anyBroken = true;
        }

        if (anyBroken) CheckIntegrity(point, radius);
    }
}