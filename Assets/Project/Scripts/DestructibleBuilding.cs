using System.Collections.Generic;
using UnityEngine;

// Put this on the root of a pre-fractured building. Every child with a MeshFilter becomes
// a chunk that Cowsins' Bullet.cs can damage directly (via IDamageable on DestructibleChunk).
public class DestructibleBuilding : MonoBehaviour
{
    [SerializeField] float linkPadding = 0.1f;     // how close chunks must be to count as connected
    [SerializeField] float groundTolerance = 0.2f; // chunks this near the lowest point are anchored
    [SerializeField] float debrisLifetime = 8f;
    [SerializeField] float chunkHealth = 100f;      // Bullet's Damage value chips this down
    [SerializeField] float explosionForce = 800f;
    [SerializeField] GameObject impactVFX;          // optional dust/spark, spawned on every hit (not just the break)

    class Chunk
    {
        public Collider col;
        public Rigidbody rb;
        public DestructibleChunk dc;
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
            var go = mf.gameObject;
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = true;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var dc = go.AddComponent<DestructibleChunk>();
            dc.Init(chunkHealth);

            var c = new Chunk { col = mc, rb = rb, dc = dc };
            dc.OnBroken += _ => HandleBroken(c);
            dc.OnImpact += (_, point) => { if (impactVFX != null) Destroy(Instantiate(impactVFX, point, Quaternion.identity), 2f); };

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

    // For explosions/grenades that don't go through Cowsins' Bullet (which already
    // damages + force-pushes chunks on its own via IDamageable + its own AddExplosionForce loop).
    public void Explode(Vector3 pos, float radius)
    {
        foreach (var c in chunks)
        {
            if (c.broken) continue;
            if (Vector3.Distance(c.col.ClosestPoint(pos), pos) <= radius)
                c.dc.Damage(chunkHealth * 2f, false); // triggers HandleBroken via OnBroken
        }
    }

    void HandleBroken(Chunk c)
    {
        if (c.broken) return; // guard against double-trigger
        c.broken = true;
        c.rb.isKinematic = false;
        Destroy(c.col.gameObject, debrisLifetime + Random.value * 3f);
        CheckIntegrity(c.col.bounds.center, 3f);
    }

    // Any chunk no longer connected to the ground through intact chunks falls too.
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
            if (!c.broken && !visited.Contains(c)) c.dc.Damage(chunkHealth * 2f, false);
    }
}