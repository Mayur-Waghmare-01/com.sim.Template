using UnityEngine;
using UnityEngine.InputSystem;

// Put on the camera. Hold left mouse to fire at the crosshair.
public class BulletGun : MonoBehaviour
{
    [SerializeField] float fireRate = 12f;       // bullets per second
    [SerializeField] float range = 200f;
    [SerializeField] float damage = 60f;         // 100 chunk health = ~2 hits on the same spot
    [SerializeField] float impactRadius = 0.35f; // bigger = wider holes
    [SerializeField] float spread = 0.01f;       // small random aim error
    [SerializeField] GameObject impactFx;        // optional dust/spark prefab

    float nextShot;

    void Update()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.isPressed) return;
        if (Time.time < nextShot) return;
        nextShot = Time.time + 1f / fireRate;

        Vector3 dir = transform.forward + Random.insideUnitSphere * spread;

        if (!Physics.Raycast(transform.position, dir, out var hit, range)) return;

        var building = hit.collider.GetComponentInParent<DestructibleBuilding>();
        if (building != null) building.Hit(hit.point, dir, impactRadius, damage);

        if (impactFx != null)
            Destroy(Instantiate(impactFx, hit.point, Quaternion.LookRotation(hit.normal)), 2f);
    }
}
