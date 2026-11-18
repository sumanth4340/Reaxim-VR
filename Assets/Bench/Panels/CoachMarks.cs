// CoachMarks.cs — Task 10: first-run coach marks (max 3,
// skippable). Shows once on first Workspace entry unless
// BenchSettings.CoachSeen: drag a board out, open the board menu,
// try Pin Mode + probes. Persists on finish or skip.
using UnityEngine;
using UnityEngine.UI;
using Bench.Core;
using Bench.UI;

namespace Bench.Panels
{
    public class CoachMarks : MonoBehaviour
    {
        public static CoachMarks Instance { get; private set; }

        public int Step => _step;
        public bool Showing => _root != null && _root.activeSelf;

        private static readonly string[] Tips =
        {
            "1/3 · Drag a board or part out of the panel into your room",
            "2/3 · Touch a board for its menu: power, restart, Pin Mode",
            "3/3 · Testing tab: probe pins and read the scope",
        };

        private GameObject _root;
        private TMPro.TMP_Text _tipTmp;
        private int _step;

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
            var machine = AppStateMachine.Instance;
            if (machine != null) machine.StateChanged += OnState;
        }

        private void OnDestroy()
        {
            var machine = AppStateMachine.Instance;
            if (machine != null) machine.StateChanged -= OnState;
            if (Instance == this) Instance = null;
        }

        private void OnState(AppState from, AppState to)
        {
            if (to == AppState.Workspace && !BenchSettings.CoachSeen)
                Begin();
        }

        private void Update()
        {
            if (!Showing) return;
            var cam = Camera.main;
            if (cam == null) return;
            _root.transform.position = cam.transform.position
                + cam.transform.forward * 1.1f - Vector3.up * 0.35f;
            var look = cam.transform.position - _root.transform.position;
            if (look.sqrMagnitude > 1e-6f)
                _root.transform.rotation = Quaternion.LookRotation(
                    -look.normalized, Vector3.up);
        }

        public void Begin()
        {
            EnsureBuilt();
            _step = 0;
            _tipTmp.text = Tips[0];
            _root.SetActive(true);
        }

        public void Next()
        {
            _step++;
            if (_step >= Tips.Length)
            {
                Finish();
                return;
            }
            _tipTmp.text = Tips[_step];
        }

        public void Skip() => Finish();

        private void Finish()
        {
            BenchSettings.CoachSeen = true;
            if (_root != null) _root.SetActive(false);
        }

        private void EnsureBuilt()
        {
            if (_root != null) return;
            _root = new GameObject("CoachMarks");
            _root.transform.SetParent(transform, false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _root.AddComponent<GraphicRaycaster>();
            var rt = _root.GetComponent<RectTransform>();
            rt.localScale = new Vector3(0.001f, 0.001f, 0.001f);
            rt.sizeDelta = new Vector2(560f, 120f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var back = new GameObject("Back");
            back.transform.SetParent(_root.transform, false);
            ConceptUI.Place(back, 0f, 0f, 560f, 120f);
            var bi = back.AddComponent<Image>();
            bi.raycastTarget = false;
            var gp = back.AddComponent<GlassPanel>();
            gp.light = false;
            gp.dark = true;
            gp.radiusOverride = 20f;
            gp.ApplyTheme();
            var tx = new GameObject("TipText");
            tx.transform.SetParent(_root.transform, false);
            ConceptUI.Place(tx, 20f, 12f, 400f, 96f);
            _tipTmp = ConceptUI.Text(tx, Tips[0], 20f, Color.white,
                false, TMPro.TextAlignmentOptions.Left);
            Chip("CoachNext", 432f, 14f, 108f, 40f, "Next", Next);
            Chip("CoachSkip", 432f, 62f, 108f, 40f, "Skip", Skip);
            _root.SetActive(false);
        }

        private void Chip(string name, float x, float y, float w,
            float h, string text, System.Action act)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            ConceptUI.Place(go, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.raycastTarget = true;
            var gp = go.AddComponent<GlassPanel>();
            gp.light = false;
            gp.dark = true;
            gp.radiusOverride = h * 0.5f;
            gp.fillOverride = new Color(1f, 1f, 1f, 0.10f);
            gp.rimOverride = ConceptUI.Cyan;
            gp.ApplyTheme();
            ConceptUI.Text(go, text, 17f, Color.white, false,
                TMPro.TextAlignmentOptions.Center);
            var b = go.AddComponent<Button>();
            b.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                act?.Invoke();
            });
        }
    }
}
