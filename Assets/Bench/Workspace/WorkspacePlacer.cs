// WorkspacePlacer.cs — MRUK room bootstrap queries for Task 6.
// Scene data comes from the device on headset, or from the prefab
// fallback room in the editor (see BuildWorkspace.cs). Everything is
// null-safe: with no room data we report it and place on a manual
// fallback plane (seated-friendly table height) instead of failing.
using UnityEngine;
using Meta.XR.MRUtilityKit;

namespace Bench.Workspace
{
    public class WorkspacePlacer : MonoBehaviour
    {
        public static WorkspacePlacer Instance { get; private set; }

        public const float FallbackPlaneY = 0.75f;
        public const float SnapLiftM = 0.002f;

        public bool RoomReady { get; private set; }
        public string Status { get; private set; } = "uninitialized";
        public Vector3 DefaultSurfacePoint { get; private set; } =
            new Vector3(0f, FallbackPlaneY, 1.2f);
        public Vector3 DefaultSurfaceNormal { get; private set; } = Vector3.up;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            var mruk = MRUK.Instance;
            if (mruk == null)
            {
                Status = "no-mruk (fallback plane)";
                Debug.LogWarning("[Bench] Workspace: no MRUK — manual plane.");
                return;
            }
            Status = "loading-room";
            mruk.RegisterSceneLoadedCallback(OnSceneLoaded);
            StartCoroutine(Watchdog());
        }

        private System.Collections.IEnumerator Watchdog()
        {
            float t = 0f;
            while (!RoomReady && t < 25f)
            {
                t += 1f;
                yield return new WaitForSecondsRealtime(1f);
            }
            if (!RoomReady)
            {
                Status = "room-timeout (fallback plane)";
                Debug.LogWarning("[Bench] Workspace: room load timed out.");
            }
        }

        private void OnSceneLoaded()
        {
            var room = MRUK.Instance != null
                ? MRUK.Instance.GetCurrentRoom() : null;
            if (room == null || room.Anchors.Count == 0)
            {
                Status = "room-empty (fallback plane)";
                return;
            }
            // Prefer a table, else the floor.
            MRUKAnchor best = null;
            foreach (var a in room.Anchors)
            {
                if (a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE)) { best = a; break; }
            }
            if (best == null)
            {
                foreach (var a in room.Anchors)
                {
                    if (a.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR)) { best = a; break; }
                }
            }
            if (best == null) best = room.Anchors[0];
            var c = best.GetAnchorCenter();
            if (best.VolumeBounds.HasValue)
            {
                // VolumeBounds is ANCHOR-LOCAL (anchors are rotated);
                // the world top is the highest transformed corner.
                var lb = best.VolumeBounds.Value;
                float top = float.NegativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = lb.center + new Vector3(
                        ((i & 1) == 0 ? -1f : 1f) * lb.extents.x,
                        ((i & 2) == 0 ? -1f : 1f) * lb.extents.y,
                        ((i & 4) == 0 ? -1f : 1f) * lb.extents.z);
                    float wy = best.transform.TransformPoint(corner).y;
                    if (wy > top) top = wy;
                }
                c.y = top;
            }
            DefaultSurfacePoint = c;
            DefaultSurfaceNormal = Vector3.up;
            RoomReady = true;
            Status = "room:" + best.Label + " anchors=" + room.Anchors.Count;
            Debug.Log("[Bench] Workspace ready: " + Status);
        }

        /// <summary>Room raycast with manual-plane fallback. Never fails.</summary>
        public bool SurfacePoint(Ray ray, out Vector3 point,
            out Vector3 normal, out string source)
        {
            var room = RoomReady && MRUK.Instance != null
                ? MRUK.Instance.GetCurrentRoom() : null;
            if (room != null
                && room.Raycast(ray, 100f, out var hit, out var anchor))
            {
                point = hit.point + hit.normal * SnapLiftM;
                normal = hit.normal;
                source = "room:" + anchor.Label;
                return true;
            }
            var plane = new Plane(Vector3.up, new Vector3(0f, FallbackPlaneY, 0f));
            if (plane.Raycast(ray, out float d))
            {
                point = ray.GetPoint(d);
                normal = Vector3.up;
                source = "fallback-plane";
                return true;
            }
            point = new Vector3(0f, FallbackPlaneY, 1.2f);
            normal = Vector3.up;
            source = "fallback-default";
            return false;
        }

        public static Ray CenterEyeRay()
        {
            var cam = Camera.main;
            if (cam != null) return new Ray(cam.transform.position,
                cam.transform.forward);
            return new Ray(new Vector3(0f, 1.6f, 0f), Vector3.forward);
        }
    }
}
