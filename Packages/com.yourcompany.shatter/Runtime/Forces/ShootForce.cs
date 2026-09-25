using UnityEngine;
using Shatter.Core;

namespace Shatter.Forces
{
    /// <summary>
    /// A single-target hit (gun/projectile style): raycasts, detaches the
    /// specific fragment hit if it belongs to a cluster, then triggers a
    /// small local ExplosionForce at the impact point for "punch" - nearby
    /// fragments get pushed/detached too, not just the one directly hit.
    /// </summary>
    public static class ShootForce
    {
        public struct ShootResult
        {
            public bool hit;
            public RaycastHit hitInfo;
        }

        public static ShootResult Shoot(
            Ray ray,
            float maxDistance,
            float force,
            float impactRadius = 0.5f,
            LayerMask? layerMask = null)
        {
            LayerMask mask = layerMask ?? ~0;

            if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, mask))
                return new ShootResult { hit = false };

            var cluster = hit.collider.GetComponentInParent<ShatterCluster>();
            cluster?.TryBreakByCollider(hit.collider);

            ExplosionForce.Explode(
                hit.point,
                impactRadius,
                force,
                upwardsBias: 0f,
                breakRadiusFraction: 0.6f);

            return new ShootResult { hit = true, hitInfo = hit };
        }
    }
}
