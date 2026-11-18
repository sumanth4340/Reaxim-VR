// WorkspacePhysics.cs — Freeze / Gravity toggles + fallback floor.
// Parts collide with each other (convex mesh colliders) and with a
// static fallback floor at y=0 so editor drops land somewhere sane;
// on device the MRUK room mesh colliders (EffectMesh, Task 10) take
// over. Honest limits are logged, never hidden.
using System.Collections.Generic;
using UnityEngine;
using Bench.Core;
using Bench.UI;

namespace Bench.Workspace
{
    public class WorkspacePhysics : MonoBehaviour
    {
        public static WorkspacePhysics Instance { get; private set; }

        public static bool FreezeAll = false;
        public static bool GravityOn = true;

        private static readonly HashSet<PlacedPart> Parts = new HashSet<PlacedPart>();

        internal static System.Collections.Generic.List<PlacedPart> PartsSnapshot()
        {
            var list = new System.Collections.Generic.List<PlacedPart>();
            foreach (var p in Parts)
                if (p != null) list.Add(p);
            return list;
        }
        private GameObject _toggles;
        private AppStateMachine _machine;

        public static void Register(PlacedPart p)
        {
            if (p != null) Parts.Add(p);
        }

        public static void Unregister(PlacedPart p) => Parts.Remove(p);

        public static void ApplyAll()
        {
            foreach (var p in Parts)
            {
                if (p == null) continue;
                var rb = p.GetComponent<Rigidbody>();
                if (rb == null) continue;
                rb.isKinematic = FreezeAll || p.Carried;
                rb.useGravity = GravityOn && !rb.isKinematic;
            }
            if (Instance != null && Instance._toggles != null)
                Instance.ShowToggles(); // keep chip labels truthful
        }

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
            EnsureFallbackFloor();
            _machine = AppStateMachine.Instance;
            if (_machine != null) _machine.StateChanged += OnState;
            OnState(_machine != null ? _machine.Current : AppState.Home,
                _machine != null ? _machine.Current : AppState.Home);
        }

        private void OnDestroy()
        {
            if (_machine != null) _machine.StateChanged -= OnState;
            if (Instance == this) Instance = null;
        }

        private void EnsureFallbackFloor()
        {
            if (GameObject.Find("FallbackFloor") != null) return;
            var go = new GameObject("FallbackFloor");
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(20f, 0.1f, 20f);
            go.transform.position = new Vector3(0f, -0.05f, 0f);
            Debug.Log("[Bench] physics: fallback floor at y=0 " +
                "(device uses MRUK room colliders; see Task 10).");
        }

        private void OnState(AppState from, AppState to)
        {
            if (to == AppState.Workspace) ShowToggles();
            else HideToggles();
        }

        private void ShowToggles()
        {
            HideToggles();
            var stage = GameObject.Find("ConceptUI")?.transform
                ?.Find("Screen_Work");
            if (stage == null) return;
            _toggles = new GameObject("PhysicsToggles");
            _toggles.transform.SetParent(stage, false);
            ConceptUI.Place(_toggles, 24f, 640f, 300f, 52f);
            var img = _toggles.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(1f, 1f, 1f, 0.02f);
            img.raycastTarget = true;
            var gp = _toggles.AddComponent<GlassPanel>();
            gp.light = false; gp.dark = true;
            gp.radiusOverride = 26f;
            gp.ApplyTheme();
            ToggleChip("Freeze", FreezeAll, v =>
            {
                FreezeAll = v;
                ApplyAll();
                ShowToggles();
            });
            ToggleChip("Gravity", GravityOn, v =>
            {
                GravityOn = v;
                ApplyAll();
                ShowToggles();
            });
        }

        private void ToggleChip(string label, bool value,
            System.Action<bool> set)
        {
            int i = _toggles.transform.childCount;
            var chip = ConceptWidgets.Chip(_toggles.transform,
                12f + i * 142f, 6f, 130f,
                label + ": " + (value ? "On" : "Off"), value,
                () => set(!value));
            chip.name = "Toggle_" + label;
        }

        private void HideToggles()
        {
            if (_toggles != null) Destroy(_toggles);
            _toggles = null;
        }
    }
}
