using UnityEngine;
using cowsins;

namespace Shatter.Core
{
    // Lives on each fragment child. Bullet.cs finds this via IDamageable and
    // damages it normally; on death we forward to the cluster's own
    // TryBreakByCollider so Shatter's connectivity graph handles the detach.
    [RequireComponent(typeof(Collider))]
    public class ChunkDamage : MonoBehaviour, IDamageable
    {
        [SerializeField] float health = 100f;
        public float Health => health;
        public float Shield => 0f;
        public bool IsDead { get; private set; }

        ShatterCluster cluster;
        Collider col;

        public void Init(ShatterCluster owner, float startingHealth)
        {
            cluster = owner;
            health = startingHealth;
            col = GetComponent<Collider>();
        }

        public void Damage(float damage, bool isHeadshot)
        {
            if (IsDead) return;
            health -= damage;
            if (health > 0f) return;

            IsDead = true;
            cluster.TryBreakByCollider(col);
        }
    }
}
