// PassthroughStyler.cs — Tier 1 passthrough styling using ONLY
// verified OVRPassthroughLayer APIs: SetBrightnessContrastSaturation
// for the soft HOME/PANEL look (dimmer, flatter, desaturated) and
// DisableColorMap to return to normal for WORKSPACE. Transitions
// lerp the BCS values (the API itself takes absolute values).
// Safe with no headset attached: everything null-guarded, never throws.
using System.Collections;
using Bench.Core;
using UnityEngine;

namespace Bench.Glass
{
    public class PassthroughStyler : MonoBehaviour
    {
        [Tooltip("Auto-found in the scene when empty.")]
        public OVRPassthroughLayer layer;

        [Header("Soft style (HOME / PROJECT_PANEL)")]
        [Range(-1f, 1f)] public float softBrightness = -0.25f;
        [Range(-1f, 1f)] public float softContrast = -0.10f;
        [Range(-1f, 1f)] public float softSaturation = -0.60f;

        private Coroutine _tween;
        private bool _warned;

        private void Awake()
        {
            if (layer == null)
                layer = FindAnyObjectByType<OVRPassthroughLayer>();
        }

        public void ApplySoft() => Apply(softBrightness, softContrast, softSaturation);

        public void ApplyNormal()
        {
            if (!Check()) return;
            layer.DisableColorMap();
        }

        /// <summary>700 ms ease-in-out-cubic style transition.</summary>
        public void TransitionTo(bool soft)
        {
            if (_tween != null) StopCoroutine(_tween);
            _tween = StartCoroutine(RunTransition(soft));
        }

        private IEnumerator RunTransition(bool soft)
        {
            if (!Check()) yield break;
            var theme = UI.BenchTheme.Current;
            float dur = theme != null ? theme.dissolveDuration : 0.7f;
            if (BenchSettings.ReduceMotion) dur = 0f;
            // Read back current values from the layer's editor fields via
            // a fresh start: we always transition from full normal.
            float fromB = 0f, fromC = 0f, fromS = 0f;
            float toB = soft ? softBrightness : 0f;
            float toC = soft ? softContrast : 0f;
            float toS = soft ? softSaturation : 0f;
            if (dur <= 0f)
            {
                if (soft) Apply(toB, toC, toS); else ApplyNormal();
                yield break;
            }
            float t = 0f;
            while (t < dur)
            {
                if (this == null || layer == null) yield break;
                t += Time.unscaledDeltaTime;
                float k = MiniTween.Evaluate(Ease.InOutCubic, Mathf.Clamp01(t / dur));
                Apply(Mathf.LerpUnclamped(fromB, toB, k),
                      Mathf.LerpUnclamped(fromC, toC, k),
                      Mathf.LerpUnclamped(fromS, toS, k));
                yield return null;
            }
            if (!soft) ApplyNormal(); else Apply(toB, toC, toS);
        }

        private void Apply(float b, float c, float s)
        {
            if (!Check()) return;
            layer.SetBrightnessContrastSaturation(b, c, s);
        }

        private bool Check()
        {
            if (layer != null) return true;
            layer = FindAnyObjectByType<OVRPassthroughLayer>();
            if (layer != null) return true;
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("[Bench] PassthroughStyler: no OVRPassthroughLayer in scene (normal in the Editor without a rig).");
            }
            return false;
        }
    }
}
