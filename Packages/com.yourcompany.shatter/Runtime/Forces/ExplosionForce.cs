using System.Collections.Generic;
using UnityEngine;
using Shatter.Core;

namespace Shatter.Forces
{
    /// <summary>
    /// Applies an explosion at a world point in two passes:
    ///   1. "Break" pass (inner radius): any fragment belonging to a
    ///      ShatterCluster within breakRadius is detached via
    ///      ShatterCluster.TryBreakByCollider - this is what causes
    ///      structural cascading (support gone -> whole disconnected section
    ///      becomes its own dynamic cluster and falls).
    ///   2. "Push" pass (full radius): every already-dynamic Rigidbody in
    ///      range (independent ShatterObjects, or clusters/pieces that just
    ///      became dynamic in pass 1) gets a falloff-scaled impulse away from
    ///      the explosion point, with an optional upward bias.
    ///
    /// Kinematic Rigidbodies (still-anchored clusters) are correctly ignored
    /// by the push pass, since physics forces don't affect kinematic bodies -
    /// they only move once something detaches them structurally in pass 1.
    /// </summary>
    public static class ExplosionForce
    {
        public static void Explode(
            Vector3 worldPoint,
            float radius,
            float forceStrength,
            float upwardsBias = 0.3f,
            AnimationCurve falloff = null,
            LayerMask? layerMask = null,
            float breakRadiusFraction = 0.35f)
        {
            LayerMask mask = layerMask ?? ~0;
            float breakRadius = radius * Mathf.Clamp01(breakRadiusFraction);

            // Pass 1: detach fragments close to the center.
            Collider[] breakHits = Physics.OverlapSphere(worldPoint, breakRadius, mask);
            foreach (var col in breakHits)
            {
                var cluster = col.GetComponentInParent<ShatterCluster>();
                if (cluster != null)
                    cluster.TryBreakByCollider(col);
            }

            // Pass 2: push everything dynamic in the full radius. Re-queried
            // AFTER breaking, since pass 1 may have destroyed some colliders
            // and spawned new dynamic Rigidbodies for detached sections.
            Collider[] pushHits = Physics.OverlapSphere(worldPoint, radius, mask);
            var affected = new HashSet<Rigidbody>();

            foreach (var col in pushHits)
            {
                Rigidbody rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                if (!affected.Add(rb)) continue; // one impulse per Rigidbody even with multiple colliders (clusters)

                Vector3 toBody = rb.worldCenterOfMass - worldPoint;
                float dist = toBody.magnitude;
                Vector3 dir = dist > 1e-4f ? toBody / dist : Vector3.up;

                float t = Mathf.Clamp01(dist / radius);
                float falloffMul = falloff != null ? falloff.Evaluate(t) : (1f - t);

                Vector3 forceVec = dir * (forceStrength * falloffMul) + Vector3.up * (upwardsBias * forceStrength * falloffMul);
                rb.AddForce(forceVec, ForceMode.Impulse);
            }
        }
    }
}
