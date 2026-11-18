// WindowLayoutManager.cs — owns every window bound. Named slots are
// posed relative to the user's view (yaw only, so nodding never
// moves windows): CENTRE_FRONT (home), LEFT (project panel), RIGHT
// (display/SSH), TOP (instruments). A newcomer takes its slot; if
// that slot is occupied (sphere-overlap test on window centres)
// it is pushed to the nearest free slot with a 320 ms dock tween —
// windows never overlap. Side windows sit 0.7-1.0 m away and about
// 15 degrees below eye level; the workspace volume in front of the
// user is never assigned a slot. Registered windows billboard
// gently toward the user (max 90 deg/s). ResetLayout() re-seats
// everything (palm-up menu in a later task; editor key R for now).
using System.Collections.Generic;
using Bench.Core;
using Bench.UI;
using UnityEngine;

namespace Bench.Windows
{
    public enum SlotKind { CentreFront, Left, Right, Top, Free }

    public class WindowLayoutManager : MonoBehaviour
    {
        public static WindowLayoutManager Instance { get; private set; }

        [Tooltip("Side window distance (m). Kept inside 0.7-1.0.")]
        public float sideDistance = 0.9f;
        [Tooltip("Centre window distance (m).")]
        public float centreDistance = 1.6f;
        [Tooltip("Side slot angle from forward (deg).")]
        public float sideAngle = 38f;
        [Tooltip("Side windows sit this far below eye level (m).")]
        public float sideDrop = 0.22f;
        [Tooltip("Top slot height above eye level (m).")]
        public float topRise = 0.55f;
        [Tooltip("Max billboard yaw speed (deg/s).")]
        public float billboardSpeed = 90f;

        private class Entry
        {
            public GlassWindow window;
            public SlotKind slot;
        }

        private readonly List<Entry> _windows = new List<Entry>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        // ── API ──────────────────────────────────────────────────

        /// <summary>Give a window its slot (or nearest free one).</summary>
        public SlotKind DockToSlot(GlassWindow window, SlotKind preferred,
                                   bool animate = true)
        {
            if (window == null) return SlotKind.Free;
            var slot = ResolveSlot(window, preferred);
            if (slot != SlotKind.Free)
            {
                var pose = SlotPose(slot);
                if (animate) window.DockTo(pose.position, pose.rotation);
                else window.transform.SetPositionAndRotation(pose.position, pose.rotation);
            }
            Upsert(window, slot);
            return slot;
        }

        /// <summary>Forget a window (it floats or hides on its own).</summary>
        public void Release(GlassWindow window)
        {
            _windows.RemoveAll(e => e.window == window || e.window == null);
        }

        /// <summary>Head-relative TOP pose (instruments above the
        /// workspace). Public so non-GlassWindow stages can use it.</summary>
        public void TopPose(out Vector3 pos, out Quaternion rot)
        {
            var p = SlotPose(SlotKind.Top);
            pos = p.position;
            rot = p.rotation;
        }

        /// <summary>Re-seat every registered window to its slot.</summary>
        public void ResetLayout()
        {
            foreach (var e in new List<Entry>(_windows))
            {
                if (e.window == null) continue;
                var pose = SlotPose(e.slot);
                e.window.DockTo(pose.position, pose.rotation);
            }
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R)) ResetLayout();
        }
#endif

        private void LateUpdate()
        {
            var head = HeadPose();
            if (head == null) return;
            float maxStep = billboardSpeed * Time.unscaledDeltaTime;
            foreach (var e in _windows)
            {
                if (e.window == null) continue;
                var t = e.window.transform;
                Vector3 toHead = head.position - t.position;
                toHead.y = 0f;
                if (toHead.sqrMagnitude < 0.001f) continue;
                Quaternion want = Quaternion.LookRotation(-toHead.normalized, Vector3.up);
                t.rotation = Quaternion.RotateTowards(t.rotation, want, maxStep);
            }
        }

        // ── Internals ────────────────────────────────────────────

        private void Upsert(GlassWindow window, SlotKind slot)
        {
            _windows.RemoveAll(e => e.window == window || e.window == null);
            _windows.Add(new Entry { window = window, slot = slot });
        }

        private SlotKind ResolveSlot(GlassWindow window, SlotKind preferred)
        {
            var order = new List<SlotKind> { preferred };
            foreach (SlotKind s in System.Enum.GetValues(typeof(SlotKind)))
                if (s != preferred && s != SlotKind.Free) order.Add(s);
            float radius = WindowRadius(window);
            foreach (var s in order)
            {
                if (s == SlotKind.CentreFront && preferred != SlotKind.CentreFront)
                    continue; // centre is reserved for Home.
                // Test the SLOT pose, not the window's current pose.
                if (!OverlapsAny(SlotPose(s).position, radius, window)) return s;
            }
            return SlotKind.Free; // nowhere free: float in place.
        }

        private bool OverlapsAny(Vector3 centre, float radius, GlassWindow self)
        {
            foreach (var e in _windows)
            {
                if (e.window == null || e.window == self) continue;
                float d = Vector3.Distance(centre, WindowCentre(e.window));
                if (d < radius + WindowRadius(e.window)) return true;
            }
            return false;
        }

        private static Vector3 WindowCentre(GlassWindow w) =>
            w.transform.position;

        private static float WindowRadius(GlassWindow w)
        {
            var rt = w.transform as RectTransform;
            if (rt == null) return 0.3f;
            float s = w.transform.lossyScale.x;
            return Mathf.Max(rt.rect.width, rt.rect.height) * 0.5f * s * 0.8f;
        }

        private Transform HeadPose()
        {
            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        private struct Pose
        {
            public Vector3 position;
            public Quaternion rotation;
        }

        private Pose SlotPose(SlotKind slot)
        {
            var head = HeadPose();
            Vector3 hp = head != null ? head.position : new Vector3(0f, 1.5f, 0f);
            float yaw = head != null ? head.eulerAngles.y : 0f;
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Vector3 p;
            switch (slot)
            {
                case SlotKind.Left:
                    p = hp + Quaternion.AngleAxis(-sideAngle, Vector3.up) * fwd * sideDistance;
                    p.y = hp.y - sideDrop;
                    break;
                case SlotKind.Right:
                    p = hp + Quaternion.AngleAxis(sideAngle, Vector3.up) * fwd * sideDistance;
                    p.y = hp.y - sideDrop;
                    break;
                case SlotKind.Top:
                    p = hp + fwd * centreDistance;
                    p.y = hp.y + topRise;
                    break;
                default: // CentreFront
                    p = hp + fwd * centreDistance;
                    p.y = hp.y;
                    break;
            }
            p.y = Mathf.Max(p.y, 0.4f); // never below waist height.
            // +z points AWAY from the head: canvases read from the
            // -z side (verified: parked panels at dotToHead -1 read
            // fine). Matches the billboard convention below.
            Quaternion r = Quaternion.LookRotation(p - hp, Vector3.up);
            return new Pose { position = p, rotation = r };
        }
    }
}
