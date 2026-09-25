using UnityEngine;
using Shatter.Forces;

namespace Shatter.Samples
{
    /// <summary>
    /// Test harness: left-click to shoot (raycast from camera), right-click to
    /// explode at the clicked point. Attach to the Main Camera.
    /// </summary>
    public class ForceTester : MonoBehaviour
    {
        [Header("Shoot")]
        [SerializeField] private Camera cam;
        [SerializeField] private float shootForce = 15f;
        [SerializeField] private float shootImpactRadius = 0.4f;
        [SerializeField] private float shootMaxDistance = 500f;

        [Header("Explosion")]
        [SerializeField] private float explosionRadius = 5f;
        [SerializeField] private float explosionForce = 20f;
        [SerializeField] private float explosionUpwardsBias = 0.4f;
        [SerializeField] private AnimationCurve explosionFalloff = AnimationCurve.Linear(0, 1, 1, 0);

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
                TryShoot();

            if (Input.GetMouseButtonDown(1))
                TryExplode();
        }

        private void TryShoot()
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            var result = ShootForce.Shoot(ray, shootMaxDistance, shootForce, shootImpactRadius);

            if (!result.hit)
                Debug.Log("[Shatter] Shot missed.");
        }

        private void TryExplode()
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f))
            {
                Debug.Log("[Shatter] Explosion click missed - no surface under cursor.");
                return;
            }

            ExplosionForce.Explode(hit.point, explosionRadius, explosionForce, explosionUpwardsBias, explosionFalloff);
        }

        private void OnDrawGizmosSelected()
        {
            // Just a visual reminder of explosion radius scale in the editor - not tied to actual click position.
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        }
    }
}
