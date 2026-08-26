using UnityEngine;
using Shatter.Core;

namespace Shatter.Samples
{
    /// <summary>
    /// Minimal manual test harness: left-click a ShatterObject in Play mode to
    /// cut it with a plane through the click point, oriented by the camera's
    /// right/up vectors (a simple "sword slash" style cut).
    ///
    /// This script does NOT touch meshes itself - it only finds the
    /// ShatterObject under the cursor and asks it to slice itself. All mesh
    /// editing lives on the object being cut, via ShatterObject.Slice().
    ///
    /// Attach to the Main Camera (or any object) - this only needs a Camera
    /// reference, it is not the thing being sliced.
    /// </summary>
    public class SliceTester : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField] private LayerMask sliceableLayers = ~0;

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
                TrySlice();
        }

        private void TrySlice()
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f, sliceableLayers))
                return;

            var shatterObject = hit.collider.GetComponentInParent<ShatterObject>();
            if (shatterObject == null)
            {
                Debug.Log($"[Shatter] '{hit.collider.name}' has no ShatterObject component - add one to make it sliceable.");
                return;
            }

            Vector3 planeNormal = Vector3.Cross(cam.transform.forward, cam.transform.up).normalized;
            if (planeNormal.sqrMagnitude < 1e-6f) planeNormal = cam.transform.right;

            shatterObject.Slice(hit.point, planeNormal);
        }
    }
}
