// LedGlow.cs — LED lens glows from the solved branch current.
// Reads PinModeController.Model.LedMilliamps at 5 Hz and drives
// emission on the largest mesh (the dome, not the leads).
using UnityEngine;
using Bench.PinMode;

namespace Bench.Workspace
{
    public class LedGlow : MonoBehaviour
    {
        private Renderer _lens;
        private Material _mat;
        private Light _glowLight;
        private string _partId = "";
        private float _nextT;

        private void Start()
        {
            var part = GetComponent<PlacedPart>();
            if (part != null) _partId = part.partId;
            // Dome = largest mesh under the part.
            float best = 0f;
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var r = mf.GetComponent<Renderer>();
                if (r == null) continue;
                float ext = Bench.UI.ModelScale.MaxExtent(mf.transform);
                if (ext > best) { best = ext; _lens = r; }
            }
            if (_lens != null)
            {
                _mat = _lens.material; // instance, not shared
                _mat.EnableKeyword("_EMISSION");
            }
            // Guaranteed-visible glow even if the GLB shader
            // ignores emission: a tiny red point light.
            var lo = new GameObject("LedLight");
            lo.transform.SetParent(transform, false);
            lo.transform.localPosition = Vector3.up * 0.004f;
            _glowLight = lo.AddComponent<Light>();
            _glowLight.type = LightType.Point;
            _glowLight.color = new Color(1f, 0.1f, 0.05f, 1f);
            _glowLight.range = 0.18f;
            _glowLight.intensity = 0f;
            _glowLight.shadows = LightShadows.None;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextT) return;
            _nextT = Time.unscaledTime + 0.2f;
            float ma = 0f;
            var ctl = PinModeController.Instance;
            if (ctl != null && !string.IsNullOrEmpty(_partId))
                ctl.Model.LedMilliamps.TryGetValue(_partId, out ma);
            // 14 mA nominal → full glow; linear below that.
            float k = Mathf.Clamp01(ma / 14f);
            if (_mat != null)
                _mat.SetColor("_EmissionColor",
                    new Color(1f, 0.08f, 0.05f, 1f) * (k * 3f));
            if (_glowLight != null)
                _glowLight.intensity = k * 0.8f;
        }
    }
}
