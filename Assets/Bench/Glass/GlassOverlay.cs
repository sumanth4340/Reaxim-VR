// GlassOverlay.cs — Tier 1 full-FOV frost wash: a head-following
// billboard quad with a stock URP/Unlit translucent tint (proven
// render path; the earlier custom shader never rasterised and was
// retired — see GlassOverlay.shader.retired). A very slow opacity
// breathe + tint drift keeps it alive without distracting motion.
// Sized past the FOV edges. DissolveTo melts it for WORKSPACE.
using Bench.Core;
using Bench.UI;
using UnityEngine;

namespace Bench.Glass
{
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasGroup))]
    public class GlassOverlay : MonoBehaviour
    {
        [Tooltip("Quad distance in metres in front of the head.")]
        public float distance = 8f;

        private Transform _head;
        private float _baseOpacity = 0.5f;
        private Color _baseTint = Color.white;

        public float Dissolve01 { get; private set; }

        private CanvasGroup _group;
        private UnityEngine.UI.Image _wash;

        private void Awake()
        {
            var theme = UI.BenchTheme.Current;
            _baseTint = theme != null ? theme.overlayTint :
                new Color(0.59f, 0.71f, 0.82f, 1f);
            _baseOpacity = theme != null
                ? theme.overlayOpacity * BenchSettings.Brightness : 0.5f;

            var canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            if (GetComponent<CanvasGroup>() == null)
                gameObject.AddComponent<CanvasGroup>();
            // Retire legacy mesh components (frost is UGUI now).
            var mf = GetComponent<MeshFilter>();
            if (mf != null) Object.Destroy(mf);
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) Object.Destroy(mr);
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)transform;
            rt.localScale = Vector3.one * 0.001f;
            rt.sizeDelta = new Vector2(24000f, 13500f);

            var go = new GameObject("Frost");
            go.transform.SetParent(transform, false);
            var grt = go.AddComponent<RectTransform>();
            grt.localScale = Vector3.one;
            grt.anchorMin = Vector2.zero;
            grt.anchorMax = Vector2.one;
            grt.sizeDelta = Vector2.zero;
            grt.anchoredPosition = Vector2.zero;
            _wash = go.AddComponent<UnityEngine.UI.Image>();
            _wash.raycastTarget = false;
            GetComponent<CanvasGroup>();
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            var cam = Camera.main;
            if (cam != null) _head = cam.transform;
            BenchSettings.Changed += ApplyTheme;
            ApplyTheme();
        }

        private void OnDestroy() => BenchSettings.Changed -= ApplyTheme;

        private void LateUpdate()
        {
            // Re-acquire: the main camera may appear after us (tag
            // juggling, rig init). Never leave the quad stranded.
            if (_head == null)
            {
                var cam = Camera.main;
                if (cam != null) _head = cam.transform;
                else return;
            }
            // Follow position + rotation only: the canvas rect
            // (24 x 13.5 m at 0.001 scale) already overshoots the FOV.
            // (Do NOT scale here — that double-applies the size.)
            transform.position = _head.position + _head.forward * distance;
            transform.rotation = _head.rotation;
            if (!BenchSettings.ReduceMotion) Paint();
        }

        public void ApplyTheme()
        {
            if (_wash == null) return;
            var c = _baseTint;
            c.a = Mathf.Clamp01(_baseOpacity) * (1f - Dissolve01);
            _wash.color = c;
        }

        public void SetDissolved(float v)
        {
            Dissolve01 = Mathf.Clamp01(v);
            ApplyTheme();
            gameObject.SetActive(Dissolve01 < 0.999f);
        }

        private void Paint()
        {
            if (_wash == null) return;
            float breathe = BenchSettings.ReduceMotion ? 0f :
                Mathf.Sin(Time.unscaledTime * 0.35f) * 0.02f;
            var c = _baseTint;
            c.a = Mathf.Clamp01(_baseOpacity + breathe) * (1f - Dissolve01);
            _wash.color = c;
        }

        /// <summary>700 ms ease-in-out-cubic dissolve (state machine drives).</summary>
        public void DissolveTo(float target, System.Action done = null)
        {
            var theme = UI.BenchTheme.Current;
            float dur = theme != null ? theme.dissolveDuration : 0.7f;
            float from = Dissolve01;
            gameObject.SetActive(true);
            MiniTween.Float(this, from, target, dur, SetDissolved, done, Ease.InOutCubic);
        }

        // (Quad-mesh path retired: the frost is a UGUI wash now —
        // same proven pipeline as the glass windows.)
    }
}
