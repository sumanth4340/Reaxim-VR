// GlassWindow.cs — behaviour for one reusable glass window:
// themed backplate, appear/disappear tweens, dock tween, selected
// and error states, grab-bar anchor. One world-space canvas per
// window; poke + ray + grab are baked by the build script via
// Interaction SDK Quick Actions. No hard-coded visuals: everything
// comes from BenchTheme.
using Bench.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bench.UI
{
    [RequireComponent(typeof(Canvas))]
    public class GlassWindow : MonoBehaviour
    {
        [Header("Wired by the prefab / build script")]
        public GlassPanel backplate;
        public RectTransform grabBar;
        public TextMeshProUGUI titleLabel;
        public RectTransform tabRow;
        public RectTransform contentRoot;
        public CanvasGroup canvasGroup;

        public Canvas Canvas { get; private set; }
        public bool IsShowing { get; private set; }

        private void Awake()
        {
            Canvas = GetComponent<Canvas>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        }

        private void OnEnable() => ApplyTheme();

        public void ApplyTheme()
        {
            var theme = BenchTheme.Current;
            if (theme == null) return;
            backplate?.ApplyTheme();
            if (titleLabel != null)
            {
                titleLabel.font = theme.interSemiBold;
                titleLabel.fontSize = BenchTheme.Scaled(theme.titleSize);
                titleLabel.color = theme.textPrimary;
            }
        }

        /// <summary>220 ms appear: scale 0.96 -> 1 plus fade.</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            IsShowing = true;
            var theme = BenchTheme.Current;
            float dur = theme != null ? theme.appearDuration : 0.22f;
            transform.localScale = Vector3.one * 0.96f;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            MiniTween.Float(this, 0f, 1f, dur, t =>
            {
                transform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, t);
                if (canvasGroup != null) canvasGroup.alpha = t;
            }, null, Ease.OutCubic);
        }

        public void Hide()
        {
            IsShowing = false;
            gameObject.SetActive(false);
        }

        /// <summary>Dock glide: 320 ms ease-out-cubic to a world pose.</summary>
        public void DockTo(Vector3 worldPos, Quaternion worldRot, System.Action done = null)
        {
            var theme = BenchTheme.Current;
            float dur = theme != null ? theme.dockDuration : 0.32f;
            Vector3 from = transform.position;
            Quaternion rFrom = transform.rotation;
            MiniTween.Float(this, 0f, 1f, dur, t =>
            {
                transform.position = Vector3.LerpUnclamped(from, worldPos, t);
                transform.rotation = Quaternion.SlerpUnclamped(rFrom, worldRot, t);
            }, done, Ease.OutCubic);
        }

        public void SetSelected(bool selected) => backplate?.SetSelected(selected);

        /// <summary>Gentle error shake (skipped under Reduce Motion).</summary>
        public void ErrorShake()
        {
            if (BenchSettings.ReduceMotion) return;
            SoundBank.Play(BenchSound.Error);
            Vector3 basePos = transform.position;
            MiniTween.Float(this, 0f, 1f, 0.3f, t =>
            {
                float x = Mathf.Sin(t * Mathf.PI * 4f) * (1f - t) * 0.012f;
                transform.position = basePos + transform.right * x;
            }, () => transform.position = basePos, Ease.Linear);
        }
    }
}
