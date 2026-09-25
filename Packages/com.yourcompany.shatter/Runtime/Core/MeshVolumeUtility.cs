using UnityEngine;

namespace Shatter.Core
{
    public static class MeshVolumeUtility
    {
        /// <summary>
        /// Signed-tetrahedron volume sum - a standard closed-mesh volume estimate.
        /// Assumes the mesh is closed/manifold (true for MeshClipper output,
        /// since every cut is capped). Returns local-space volume (caller must
        /// account for the object's scale if needed).
        /// </summary>
        public static float ComputeApproxVolume(ShatterMeshData data)
        {
            float volume = 0f;
            foreach (var sub in data.subMeshes)
            {
                var tris = sub.triangles;
                for (int i = 0; i < tris.Count; i += 3)
                {
                    Vector3 p0 = data.vertices[tris[i]].position;
                    Vector3 p1 = data.vertices[tris[i + 1]].position;
                    Vector3 p2 = data.vertices[tris[i + 2]].position;
                    volume += Vector3.Dot(p0, Vector3.Cross(p1, p2)) / 6f;
                }
            }
            return Mathf.Abs(volume);
        }
    }
}
