// UITheme.cs — every design token for MR Electronics Bench lives here.
// No colours, sizes, fonts, radii, durations or easings are hard-coded
// anywhere else: UI code reads BenchTheme.Current.
using TMPro;
using UnityEngine;

namespace Bench.UI
{
    [CreateAssetMenu(fileName = "BenchTheme", menuName = "Bench/UI Theme")]
    public class UITheme : ScriptableObject
    {
        [Header("Glass")]
        [Tooltip("Panel fill (#0F1218). Opacity animated by state.")]
        public Color glassFill = new Color(0x0F / 255f, 0x12 / 255f, 0x18 / 255f, 1f);
        [Range(0f, 1f)] public float glassOpacityHome = 0.75f;
        [Range(0f, 1f)] public float glassOpacityWorkspace = 0.60f;
        public Color glassRim = new Color(1f, 1f, 1f, 0.40f);
        [Tooltip("Light frosted glass (concept panels, rows, tabs).")]
        public Color lightGlassFill = new Color(1f, 1f, 1f, 0.16f);
        [Tooltip("Light glass row fill (concept .row white .12).")]
        public Color lightRowFill = new Color(1f, 1f, 1f, 0.12f);
        [Tooltip("Light glass row border (concept .row white .2).")]
        public Color lightRowBorder = new Color(1f, 1f, 1f, 0.20f);
        [Tooltip("Dark glass variant (project panel, pills).")]
        public Color darkGlassFill = new Color(18f / 255f, 24f / 255f, 32f / 255f, 1f);
        [Range(0f, 1f)] public float darkGlassOpacity = 0.78f;
        [Tooltip("Solid fallback for Reduce Transparency.")]
        public Color solidPanelFill = new Color(0x0F / 255f, 0x12 / 255f, 0x18 / 255f, 1f);
        [Range(2f, 64f)] public float windowRadius = 28f;
        [Range(2f, 64f)] public float cardRadius = 20f;
        [Range(1f, 8f)] public float edgeWidth = 2f;
        [Tooltip("Seconds per specular sweep across glass.")]
        public float sweepPeriod = 8f;

        [Header("Overlay (Tier 1 frost)")]
        public Color overlayTint = new Color(150f / 255f, 180f / 255f, 210f / 255f, 1f);
        [Range(0f, 1f)] public float overlayOpacity = 0.55f;
        [Range(0f, 0.1f)] public float causticIntensity = 0.025f;
        public float causticSpeed = 0.12f;
        public float driftSpeed = 0.02f;

        [Header("Text")]
        public Color textPrimary = new Color(0xF4 / 255f, 0xF7 / 255f, 0xFA / 255f, 1f);
        public Color textSecondary = new Color(0xA7 / 255f, 0xB3 / 255f, 0xBF / 255f, 1f);
        public Color accent = new Color(0x5C / 255f, 0xE1 / 255f, 0xE6 / 255f, 1f);
        public Color success = new Color(0x4A / 255f, 0xDE / 255f, 0x80 / 255f, 1f);
        public Color warning = new Color(0xFF / 255f, 0xB4 / 255f, 0x54 / 255f, 1f);
        public Color error = new Color(0xFF / 255f, 0x6B / 255f, 0x6B / 255f, 1f);

        [Header("Type (px = mm at 0.001 canvas scale)")]
        public TMP_FontAsset interRegular;
        public TMP_FontAsset interSemiBold;
        public TMP_FontAsset monoRegular;
        public TMP_FontAsset monoBold;
        public float titleSize = 40f;
        public float headingSize = 32f;
        public float bodySize = 28f;
        public float labelSize = 22f;
        public float readingSize = 64f;

        [Header("Spacing / radii")]
        public float space8 = 8f;
        public float space16 = 16f;
        public float space24 = 24f;
        public float space32 = 32f;
        public float space48 = 48f;
        public float padding = 24f;

        [Header("Motion (seconds)")]
        public float hoverDuration = 0.12f;
        public float pressDuration = 0.09f;
        public float pressRecessMm = 4f;
        public float tabSwitchDuration = 0.18f;
        public float tabSwitchSlidePx = 12f;
        public float dockDuration = 0.32f;
        public float appearDuration = 0.22f;
        public float snapDuration = 0.16f;
        public float snapOvershoot = 0.04f;
        public float dissolveDuration = 0.70f;
        public float panelGlideDuration = 0.32f;

        [Header("Audio")]
        [Range(0f, 1f)] public float uiVolume = 0.5f;

        [Header("Materials (wired by the Task 1 build script)")]
        public Material glassMaterial;
        public Material overlayMaterial;
    }

    /// <summary>Global theme access. The asset must live at
    /// Assets/Bench/UI/Resources/BenchTheme.asset.</summary>
    public static class BenchTheme
    {
        private static UITheme _current;
        public static UITheme Current
        {
            get
            {
                if (_current == null)
                    _current = Resources.Load<UITheme>("BenchTheme");
                return _current;
            }
        }

        public static float Scaled(float px) =>
            px * Bench.Core.BenchSettings.TextScale;
    }
}
