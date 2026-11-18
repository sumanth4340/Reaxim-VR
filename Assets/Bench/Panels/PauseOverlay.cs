// PauseOverlay.cs — Task 10: calm pause/resume on tracking
// loss. Subscribes the documented static OVRManager.TrackingLost
// / TrackingAcquired events (Meta XR SDK v209 OVRManager
// reference, verified; also present in the installed package).
// One line, head-following, dismisses itself on re-acquire.
// TestSimulateLoss/Acquire drive the same path in the editor.
using UnityEngine;
using UnityEngine.UI;
using Bench.UI;

namespace Bench.Panels
{
    public class PauseOverlay : MonoBehaviour
    {
        public static PauseOverlay Instance { get; private set; }

        public bool Showing => _root != null && _root.activeSelf;

        private GameObject _root;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            try
            {
                OVRManager.TrackingLost += OnLost;
                OVRManager.TrackingAcquired += OnAcquired;
            }
            catch { /* editor without XR: test hooks drive us */ }
        }

        private void OnDestroy()
        {
            try
            {
                OVRManager.TrackingLost -= OnLost;
                OVRManager.TrackingAcquired -= OnAcquired;
            }
            catch { }
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!Showing) return;
            var cam = Camera.main;
            if (cam == null) return;
            _root.transform.position = cam.transform.position
                + cam.transform.forward * 1.2f;
            var look = cam.transform.position - _root.transform.position;
            if (look.sqrMagnitude > 1e-6f)
                _root.transform.rotation = Quaternion.LookRotation(
                    -look.normalized, Vector3.up);
        }

        public void TestSimulateLoss() => OnLost();

        public void TestSimulateAcquire() => OnAcquired();

        private void OnLost()
        {
            EnsureBuilt();
            _root.SetActive(true);
        }

        private void OnAcquired()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void EnsureBuilt()
        {
            if (_root != null) return;
            _root = new GameObject("PauseOverlay");
            _root.transform.SetParent(transform, false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _root.AddComponent<GraphicRaycaster>();
            var rt = _root.GetComponent<RectTransform>();
            rt.localScale = new Vector3(0.001f, 0.001f, 0.001f);
            rt.sizeDelta = new Vector2(520f, 96f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var back = new GameObject("Back");
            back.transform.SetParent(_root.transform, false);
            ConceptUI.Place(back, 0f, 0f, 520f, 96f);
            var bi = back.AddComponent<Image>();
            bi.raycastTarget = false;
            var gp = back.AddComponent<GlassPanel>();
            gp.light = false;
            gp.dark = true;
            gp.radiusOverride = 20f;
            gp.ApplyTheme();
            var tx = new GameObject("PauseText");
            tx.transform.SetParent(_root.transform, false);
            ConceptUI.Place(tx, 20f, 12f, 480f, 72f);
            ConceptUI.Text(tx, "Tracking paused — look around to resume",
                22f, Color.white, false,
                TMPro.TextAlignmentOptions.Center);
            _root.SetActive(false);
        }
    }
}
