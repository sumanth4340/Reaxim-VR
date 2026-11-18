// MiniTween.cs — tiny coroutine tween helper (no external DOTween
// dependency). All durations honour Reduce Motion (finish instantly).
// Tweens abort safely if the owner is destroyed mid-flight.
using System;
using System.Collections;
using UnityEngine;

namespace Bench.Core
{
    public enum Ease { Linear, OutCubic, InOutCubic, OutBack }

    public static class MiniTween
    {
        public static float Duration(float seconds) =>
            BenchSettings.ReduceMotion ? 0f : seconds;

        public static float Evaluate(Ease ease, float t, float overshoot = 0.04f)
        {
            switch (ease)
            {
                case Ease.OutCubic: return 1f - Mathf.Pow(1f - t, 3f);
                case Ease.InOutCubic:
                    return t < 0.5f ? 4f * t * t * t
                                    : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
                case Ease.OutBack:
                    const float c1 = 1.70158f;
                    float c3 = c1 * (1f + overshoot * 10f);
                    float u = t - 1f;
                    return 1f + (c3 + 1f) * u * u * u + c3 * u * u;
                default: return t;
            }
        }

        public static void Float(MonoBehaviour owner, float from, float to,
            float seconds, Action<float> onUpdate, Action onComplete = null,
            Ease ease = Ease.OutCubic)
        {
            float dur = Duration(seconds);
            if (dur <= 0f || owner == null)
            {
                if (owner != null) { onUpdate?.Invoke(to); onComplete?.Invoke(); }
                return;
            }
            TweenDriver.Instance.StartCoroutine(Run(owner, dur,
                t => onUpdate?.Invoke(Mathf.LerpUnclamped(from, to, Evaluate(ease, t))),
                onComplete));
        }

        public static void Vector3(MonoBehaviour owner, Vector3 from, Vector3 to,
            float seconds, Action<Vector3> onUpdate, Action onComplete = null,
            Ease ease = Ease.OutCubic)
        {
            float dur = Duration(seconds);
            if (dur <= 0f || owner == null)
            {
                if (owner != null) { onUpdate?.Invoke(to); onComplete?.Invoke(); }
                return;
            }
            TweenDriver.Instance.StartCoroutine(Run(owner, dur,
                t => onUpdate?.Invoke(UnityEngine.Vector3.LerpUnclamped(from, to, Evaluate(ease, t))),
                onComplete));
        }

        public static void Color(MonoBehaviour owner, Color from, Color to,
            float seconds, Action<Color> onUpdate, Action onComplete = null,
            Ease ease = Ease.OutCubic)
        {
            float dur = Duration(seconds);
            if (dur <= 0f || owner == null)
            {
                if (owner != null) { onUpdate?.Invoke(to); onComplete?.Invoke(); }
                return;
            }
            TweenDriver.Instance.StartCoroutine(Run(owner, dur,
                t => onUpdate?.Invoke(UnityEngine.Color.LerpUnclamped(from, to, Evaluate(ease, t))),
                onComplete));
        }

        private static IEnumerator Run(MonoBehaviour owner, float dur,
            Action<float> tick, Action done)
        {
            float t = 0f;
            while (t < dur)
            {
                if (owner == null) yield break;
                t += Time.unscaledDeltaTime;
                tick(Mathf.Clamp01(t / dur));
                yield return null;
            }
            if (owner == null) yield break;
            tick(1f);
            done?.Invoke();
        }

        private sealed class TweenDriver : MonoBehaviour
        {
            private static TweenDriver _instance;
            public static TweenDriver Instance
            {
                get
                {
                    if (_instance == null)
                    {
                        var go = new GameObject("BenchTweenDriver");
                        go.hideFlags = HideFlags.HideAndDontSave;
                        DontDestroyOnLoad(go);
                        _instance = go.AddComponent<TweenDriver>();
                    }
                    return _instance;
                }
            }
        }
    }
}
