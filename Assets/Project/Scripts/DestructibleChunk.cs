using System;
using System.Collections;
using UnityEngine;
using cowsins;

// Auto-added to every fractured chunk by DestructibleBuilding.
// Implementing IDamageable means Cowsins' Bullet.cs damages walls through its
// normal DamageService path. This also listens for the bullet's own trigger
// so each hit shows progressive damage (cracks + a small impact jolt) instead
// of the chunk looking untouched until the moment it finally breaks.
[RequireComponent(typeof(Renderer))]
public class DestructibleChunk : MonoBehaviour, IDamageable
{
    [Tooltip("Crack materials from light to heavy damage. Leave empty to skip the visual stages.")]
    [SerializeField] Material[] damageStages;
    [SerializeField] float shakeStrength = 0.03f;
    [SerializeField] float shakeDuration = 0.08f;

    public float Health { get; private set; }
    public float Shield => 0f;
    public bool IsDead { get; private set; }
    public float HealthPercent01 { get; private set; } = 1f;

    public event Action<DestructibleChunk> OnBroken;
    public event Action<DestructibleChunk, Vector3> OnImpact; // fires per hit, before it breaks

    float maxHealth;
    Renderer rend;
    Vector3 basePos;
    Coroutine shakeRoutine;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        basePos = transform.localPosition;
    }

    public void Init(float startingHealth)
    {
        maxHealth = startingHealth;
        Health = startingHealth;
    }

    public void Damage(float damage, bool isHeadshot)
    {
        if (IsDead) return;

        Health -= damage;
        HealthPercent01 = Mathf.Clamp01(Health / maxHealth);
        ApplyDamageStage();

        if (Health > 0f) return;

        IsDead = true;
        OnBroken?.Invoke(this);
    }

    // Bullet.cs's collider is a trigger, so Unity calls this on the chunk too -
    // purely visual, doesn't affect the damage math above.
    void OnTriggerEnter(Collider other)
    {
        if (IsDead || other.GetComponent<IBullet>() == null) return;

        Vector3 point = GetComponent<Collider>().ClosestPoint(other.transform.position);
        OnImpact?.Invoke(this, point);

        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(Shake());
    }

    void ApplyDamageStage()
    {
        if (damageStages == null || damageStages.Length == 0) return;
        // 1.0 health -> stage 0 (undamaged), 0.0 health -> last (most cracked) stage
        int stage = Mathf.Clamp(
            Mathf.FloorToInt((1f - HealthPercent01) * damageStages.Length),
            0, damageStages.Length - 1);
        rend.material = damageStages[stage];
    }

    IEnumerator Shake()
    {
        float t = 0f;
        while (t < shakeDuration)
        {
            t += Time.deltaTime;
            transform.localPosition = basePos + (Vector3)UnityEngine.Random.insideUnitCircle * shakeStrength;
            yield return null;
        }
        transform.localPosition = basePos;
    }
}