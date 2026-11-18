// GlassPanel.cs — binds a UGUI Image to the Bench glass shader:
// per-instance material, rect-size sync for the corner SDF, theme
// fill/rim/radius, selected boost, and the Reduce Transparency
// fallback (solid dark fill). Attach next to an Image.
using UnityEngine;
using UnityEngine.UI;

namespace Bench.UI
{
    [RequireComponent(typeof(Image))]
    [ExecuteAlways]
    public class GlassPanel : MonoBehaviour
    {
        [Tooltip("Use the dark glass variant (panels, pills).")]
        public bool dark;
        [Tooltip("Use the LIGHT frosted glass (concept panels/rows/tabs).")]
        public bool light;
        [Tooltip("Exact per-instance fill (alpha 0 = follow theme).")]
        public Color fillOverride = new Color(0f, 0f, 0f, 0f);
        [Tooltip("Exact per-instance rim (alpha 0 = follow theme).")]
        public Color rimOverride = new Color(0f, 0f, 0f, 0f);

        [Tooltip("Corner radius override in px (<=0 follows the theme).")]
        public float radiusOverride = -1f;

        private Image _image;
        private Material _mat;
        private static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");
        private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        private static readonly int RimBoostId = Shader.PropertyToID("_RimBoost");
        private static readonly int SweepPeriodId = Shader.PropertyToID("_SweepPeriod");
        private static readonly int SweepEnabledId = Shader.PropertyToID("_SweepEnabled");

        public Material Material => _mat;

        private void OnEnable()
        {
            _image = GetComponent<Image>();
            var theme = BenchTheme.Current;
            if (theme != null && theme.glassMaterial != null && theme.glassMaterial.shader != null)
            {
                if (_mat == null || _mat.shader != theme.glassMaterial.shader)
                    _mat = new Material(theme.glassMaterial);
                _image.material = _mat;
            }
            Bench.Core.BenchSettings.Changed += ApplyTheme;
            ApplyTheme();
            SyncRect();
        }

        private void OnDisable()
        {
            Bench.Core.BenchSettings.Changed -= ApplyTheme;
        }

        private void OnRectTransformDimensionsChange() => SyncRect();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            _image = GetComponent<Image>();
            ApplyTheme();
            SyncRect();
        }
#endif

        /// <summary>Fill opacity 0..1 (state fades, workspace glass).</summary>
        public void SetOpacity(float a)
        {
            if (_mat == null) return;
            var c = _mat.GetColor(FillColorId);
            c.a = Mathf.Clamp01(a);
            _mat.SetColor(FillColorId, c);
        }

        /// <summary>Selected-state rim boost 0..1.</summary>
        public void SetSelected(bool selected)
        {
            if (_mat == null) return;
            _mat.SetFloat(RimBoostId, selected ? 0.35f : 0f);
        }

        public void ApplyTheme()
        {
            var theme = BenchTheme.Current;
            if (theme == null || _mat == null || _image == null) return;
            bool solid = Bench.Core.BenchSettings.ReduceTransparency;
            Color fill;
            float opacity;
            if (light) { fill = theme.lightGlassFill; opacity = fill.a; }
            else if (dark) { fill = theme.darkGlassFill; opacity = theme.darkGlassOpacity; }
            else { fill = theme.glassFill; opacity = theme.glassOpacityHome; }
            if (fillOverride.a > 0f) { fill = fillOverride; opacity = fillOverride.a; }
            if (solid) { fill = theme.solidPanelFill; opacity = 1f; }
            fill.a = opacity;
            _mat.SetColor(FillColorId, fill);
            var rim = theme.glassRim;
            if (rimOverride.a > 0f) rim = rimOverride;
            _mat.SetColor(RimColorId, rim);
            _mat.SetFloat(RadiusId, radiusOverride > 0 ? radiusOverride : theme.windowRadius);
            _mat.SetFloat(EdgeWidthId, theme.edgeWidth);
            _mat.SetFloat(SweepPeriodId, theme.sweepPeriod);
            _mat.SetFloat(SweepEnabledId, Bench.Core.BenchSettings.ReduceMotion ? 0f : 1f);
        }

        private void SyncRect()
        {
            if (_mat == null) return;
            var rt = transform as RectTransform;
            if (rt == null) return;
            _mat.SetVector(RectSizeId, new Vector4(rt.rect.width, rt.rect.height, 0f, 0f));
        }
    }
}
