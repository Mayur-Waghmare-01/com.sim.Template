using System;
using UnityEngine;
using cowsins;

// Auto-added to every fractured chunk by DestructibleBuilding.
// Implementing IDamageable means Cowsins' Bullet.cs damages walls
// through its normal DamageService path — no changes to the asset needed.
public class DestructibleChunk : MonoBehaviour, IDamageable
{
    public float Health { get; private set; }
    public float Shield => 0f;
    public bool IsDead { get; private set; }

    public event Action<DestructibleChunk> OnBroken;

    public void Init(float startingHealth) => Health = startingHealth;

    public void Damage(float damage, bool isHeadshot)
    {
        if (IsDead) return;

        Health -= damage;
        if (Health > 0f) return;

        IsDead = true;
        OnBroken?.Invoke(this);
    }
}
