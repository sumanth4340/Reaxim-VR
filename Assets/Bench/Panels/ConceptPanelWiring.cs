// ConceptPanelWiring.cs — Task 5: the Project Panel. Tab switching
// (Boards / Components / Testing) across the concept screens,
// screen visibility per AppState, and the LEFT dock glide that pairs
// with the glass dissolve on drag-out. Drag logic itself lives on
// Draggable3D; this owns tabs, screens and the dock tween.
using UnityEngine;
using UnityEngine.UI;
using Bench.Core;
using Bench.UI;

namespace Bench.Panels
{
    public class ConceptPanelWiring : MonoBehaviour
    {
        public static ConceptPanelWiring Instance { get; private set; }

        private enum Tab { Boards, Components }

        private Transform _root;
        private Transform _boards;
        private Transform _comp;
        private Transform _work;
        private Transform _pin;
        private Transform _home;
        private Tab _tab = Tab.Boards;
        private bool _docked;
        private bool _away;
        private readonly System.Collections.Generic.Dictionary<Transform, int>
            _poseTokens = new System.Collections.Generic.Dictionary<Transform, int>();
        private AppStateMachine _machine;

        private const float PanelX = 420f;
        private const float PanelW = 440f;
        private const float DockX = 24f;
        // Focus mode: panel parks ~50° left (a deliberate head turn,
        // inside comfortable neck range), billboarded to stay readable.
        private const float AwayYawDeg = -50f;
        private const float AwayDist = 1.7f;

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
            _root = transform;
            _boards = _root.Find("Screen_Boards");
            _comp = _root.Find("Screen_Comp");
            _work = _root.Find("Screen_Work");
            _pin = _root.Find("Screen_Pin");
            _home = _root.Find("Screen_Home");
            _machine = AppStateMachine.Instance;
            if (_machine == null)
            {
                Debug.LogError("[Bench] Panel wiring: no AppStateMachine.");
                return;
            }
            WireTabs(_boards);
            WireTabs(_comp);
            _machine.StateChanged += OnState;
            ApplyState(_machine.Current, instant: true);
        }

        private void OnDestroy()
        {
            if (_machine != null) _machine.StateChanged -= OnState;
            if (Instance == this) Instance = null;
        }

        private void WireTabs(Transform screen)
        {
            if (screen == null) return;
            var panel = screen.Find("Panel");
            if (panel == null) return;
            var b = panel.Find("Tab_Boards")?.gameObject;
            var c = panel.Find("Tab_Components")?.gameObject;
            var t = panel.Find("Tab_Testing")?.gameObject;
            if (b != null) PanelBtn(b, () => SelectTab(Tab.Boards));
            if (c != null) PanelBtn(c, () => SelectTab(Tab.Components));
            if (t != null) PanelBtn(t, SelectTesting);
            Debug.Log("[Bench] tabs wired on " + screen.name
                + " (boards=" + (b != null) + " comp=" + (c != null)
                + " test=" + (t != null) + ")");
        }

        private static void PanelBtn(GameObject go, System.Action onClick)
        {
            if (go == null) return;
            var b = go.GetComponent<Button>();
            if (b == null) b = go.AddComponent<Button>();
            b.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                onClick?.Invoke();
            });
        }

        private void SelectTab(Tab tab)
        {
            _tab = tab;
            if (_machine.Current == AppState.Workspace)
            {
                // Swap screens in place; the panel keeps its pose.
                SetActive(_boards, _tab == Tab.Boards);
                SetActive(_comp, _tab == Tab.Components);
                return;
            }
            if (_machine.Current != AppState.ProjectPanel)
                _machine.TransitionTo(AppState.ProjectPanel);
            else
                ApplyState(AppState.ProjectPanel, instant: false);
        }

        private void SelectTesting()
        {
            // The Testing tab lives on the workspace: same transition
            // as dragging the first tool out (Task 8 shows instruments).
            _machine.TransitionTo(AppState.Workspace);
        }

        private void OnState(AppState from, AppState to) => ApplyState(to, false);

        public void ApplyState(AppState state, bool instant)
        {
            SetActive(_home, state == AppState.Home);
            bool panel = state == AppState.ProjectPanel;
            bool work = state == AppState.Workspace;
            SetActive(_boards, (panel || work) && _tab == Tab.Boards);
            SetActive(_comp, (panel || work) && _tab == Tab.Components);
            SetActive(_work, work);
            SetActive(_pin, false);
            if (work)
            {
                // Focus mode: workspace owns the view, panel parks left.
                _away = true;
                PoseAway(_boards, instant);
                PoseAway(_comp, instant);
                _docked = true;
                // Testing instruments float ABOVE the bench (TOP slot),
                // never piled into the workspace with the parts.
                var layout = Windows.WindowLayoutManager.Instance;
                if (_work != null && layout != null)
                {
                    layout.TopPose(out var tp, out var tr);
                    tp.y = CeilingClampY(tp);
                    _work.position = tp;
                    _work.rotation = tr;
                }
            }
            else
            {
                _away = false;
                Dock(panel ? false : true, instant);
            }
        }

        private void Update()
        {
            // Gentle billboard while parked (yaw + pitch face the
            // user; position untouched; max ~90 deg/s).
            if (!_away || _machine == null
                || _machine.Current != AppState.Workspace)
                return;
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var s in new[] { _boards, _comp })
            {
                if (s == null) continue;
                Vector3 toHead = cam.transform.position - s.position;
                if (toHead.sqrMagnitude < 1e-6f) continue;
                var want = Quaternion.LookRotation(-toHead.normalized,
                    Vector3.up);
                s.rotation = Quaternion.RotateTowards(s.rotation, want,
                    90f * Time.unscaledDeltaTime);
            }
        }

        /// <summary>Recall tab: park far left or bring back near.</summary>
        public void ToggleRecall()
        {
            if (_machine == null || _machine.Current != AppState.Workspace)
                return;
            _away = !_away;
            if (_away)
            {
                PoseAway(_boards, false);
                PoseAway(_comp, false);
                _docked = true;
            }
            else
            {
                // Near: classic left-dock look, facing forward.
                DockScreen(_boards, true, false);
                DockScreen(_comp, true, false);
                _docked = true;
            }
        }

        /// <summary>Lower a TOP pose so the 0.72 m panel clears a
        /// low ceiling (MRUK room raycast; no room = no clamp).</summary>
        private static float CeilingClampY(Vector3 tp)
        {
            var mruk = Meta.XR.MRUtilityKit.MRUK.Instance;
            var room = mruk != null ? mruk.GetCurrentRoom() : null;
            if (room == null) return tp.y;
            Meta.XR.MRUtilityKit.MRUKAnchor hit;
            UnityEngine.RaycastHit rh;
            if (room.Raycast(new Ray(tp, Vector3.up), 3f, out rh, out hit))
            {
                float want = rh.point.y - 0.41f; // half panel + margin
                if (want < tp.y) return Mathf.Max(want, 1.2f);
            }
            return tp.y;
        }

        /// <summary>Dragging another part out re-parks the panel.</summary>
        public void FocusAway()
        {
            if (_machine == null || _machine.Current != AppState.Workspace)
                return;
            if (_away) return;
            _away = true;
            PoseAway(_boards, false);
            PoseAway(_comp, false);
            _docked = true;
        }

        public bool IsAway() => _away;

        private void PoseAway(Transform screen, bool instant)
        {
            if (screen == null) return;
            var cam = Camera.main;
            Vector3 hp = cam != null ? cam.transform.position
                : new Vector3(0f, 1.5f, 0f);
            float yaw = (cam != null ? cam.transform.eulerAngles.y : 0f)
                + AwayYawDeg;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 pos = hp + dir * AwayDist;
            pos.y = hp.y - 0.1f;
            // Canvas front is -Z: +Z must point away from the head.
            var rot = Quaternion.LookRotation(pos - hp, Vector3.up);
            PoseTo(screen, pos, rot, instant);
        }

        private void PoseTo(Transform screen, Vector3 pos,
            Quaternion rot, bool instant)
        {
            if (screen == null) return;
            // Per-screen token: a shared token lets the second
            // stage's tween stillbirth the first's.
            int token = _poseTokens.TryGetValue(screen, out var cur)
                ? cur + 1 : 1;
            _poseTokens[screen] = token;
            if (instant)
            {
                screen.position = pos;
                screen.rotation = rot;
                return;
            }
            Vector3 from = screen.position;
            Quaternion rFrom = screen.rotation;
            MiniTween.Float(this, 0f, 1f, 0.32f, t =>
            {
                if (screen == null || !_poseTokens.TryGetValue(screen,
                    out var now) || now != token) return;
                screen.position = Vector3.LerpUnclamped(from, pos, t);
                screen.rotation = Quaternion.SlerpUnclamped(rFrom, rot,
                    t);
            }, null, Ease.OutCubic);
        }

        private static void SetActive(Transform t, bool on)
        {
            if (t != null) t.gameObject.SetActive(on);
        }

        private Transform ActivePanelScreen() =>
            (_tab == Tab.Boards ? _boards : _comp);

        /// <summary>Glide the panel stages to the LEFT dock (near)
        /// or back to centre. Poses both tab screens so tab swaps
        /// stay seamless; resets rotation (billboard may have yawed).
        /// </summary>
        public void Dock(bool docked, bool instant)
        {
            _docked = docked;
            DockScreen(_boards, docked, instant);
            DockScreen(_comp, docked, instant);
        }

        private void DockScreen(Transform screen, bool docked,
            bool instant)
        {
            if (screen == null) return;
            float toX = ((docked ? DockX : PanelX) + PanelW / 2f - 640f)
                * 0.001f;
            PoseTo(screen, new Vector3(toX, 1.5f, 2.2f),
                Quaternion.identity, instant);
        }

        public bool IsDocked() => _docked;

        /// <summary>Panel rect (for drop-on-panel tests), world aware.</summary>
        public bool PointerOverPanel(Vector2 screenPos, Camera cam)
        {
            var screen = ActivePanelScreen();
            var panel = screen?.Find("Panel") as RectTransform;
            if (panel == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(
                panel, screenPos, cam);
        }
    }
}
