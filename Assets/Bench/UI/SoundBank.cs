// SoundBank.cs — the five UI sounds, synthesised procedurally at
// runtime (no binary assets to ship). Short sine/noise envelopes:
// hover tick, press, snap/connect, error, power-on. One persistent
// AudioSource, honours the mute toggle and theme volume.
using System.Collections.Generic;
using UnityEngine;

namespace Bench.UI
{
    public enum BenchSound { Hover, Press, Snap, Error, PowerOn }

    public static class SoundBank
    {
        private const int Rate = 22050;
        private static readonly Dictionary<BenchSound, AudioClip> Clips =
            new Dictionary<BenchSound, AudioClip>();
        private static AudioSource _source;
        private static readonly List<AudioSource> _pool3D =
            new List<AudioSource>();
        private static int _poolIdx;

        public static void Play(BenchSound sound)
        {
            if (Bench.Core.BenchSettings.AudioMuted) return;
            var clip = Get(sound);
            if (clip == null) return;
            EnsureSource();
            float vol = BenchTheme.Current != null ? BenchTheme.Current.uiVolume : 0.5f;
            _source.PlayOneShot(clip, vol);
        }

        /// <summary>Positional one-shot (wires, drops, probes,
        /// power): pooled 3D voices, bench-scale falloff.</summary>
        public static void PlayAt(BenchSound sound, Vector3 worldPos)
        {
            if (Bench.Core.BenchSettings.AudioMuted) return;
            var clip = Get(sound);
            if (clip == null) return;
            EnsurePool();
            AudioSource voice = null;
            for (int i = 0; i < _pool3D.Count; i++)
            {
                var cand = _pool3D[_poolIdx % _pool3D.Count];
                _poolIdx++;
                if (!cand.isPlaying) { voice = cand; break; }
            }
            if (voice == null) voice = _pool3D[0];
            voice.transform.position = worldPos;
            _lastAt = worldPos;
            float vol = BenchTheme.Current != null ? BenchTheme.Current.uiVolume : 0.5f;
            voice.PlayOneShot(clip, vol);
        }

        private static Vector3 _lastAt;
        public static Vector3 DebugLastAt() => _lastAt;

        /// <summary>Test hook: currently audible voices (2D + 3D).</summary>
        public static int DebugActiveVoices()
        {
            int n = 0;
            if (_source != null && _source.isPlaying) n++;
            foreach (var v in _pool3D)
                if (v != null && v.isPlaying) n++;
            return n;
        }

        private static void EnsureSource()
        {
            if (_source != null) return;
            var go = new GameObject("BenchSoundBank");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
        }

        private static void EnsurePool()
        {
            if (_pool3D.Count > 0) return;
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("BenchSound3D_" + i);
                go.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(go);
                var s = go.AddComponent<AudioSource>();
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 0.3f;
                s.maxDistance = 8f;
                s.dopplerLevel = 0f;
                s.playOnAwake = false;
                _pool3D.Add(s);
            }
        }

        private static AudioClip Get(BenchSound sound)
        {
            if (!Clips.TryGetValue(sound, out var clip) || clip == null)
            {
                clip = Build(sound);
                Clips[sound] = clip;
            }
            return clip;
        }

        private static AudioClip Build(BenchSound sound)
        {
            float[] pcm;
            switch (sound)
            {
                case BenchSound.Hover:  pcm = Tone(1800f, 0.05f, 0.25f); break;
                case BenchSound.Press:  pcm = Sweep(620f, 320f, 0.09f, 0.5f); break;
                case BenchSound.Snap:   pcm = Snap(); break;
                case BenchSound.Error:  pcm = Buzz(); break;
                default:                pcm = PowerUp(); break;
            }
            var clip = AudioClip.Create("bench_" + sound, pcm.Length, 1, Rate, false);
            clip.SetData(pcm, 0);
            return clip;
        }

        private static float[] Tone(float freq, float seconds, float gain)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var pcm = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Exp(-t / (seconds * 0.35f));
                pcm[i] = gain * env * Mathf.Sin(2f * Mathf.PI * freq * t);
            }
            return pcm;
        }

        private static float[] Sweep(float f0, float f1, float seconds, float gain)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var pcm = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;
                phase += 2f * Mathf.PI * Mathf.Lerp(f0, f1, u) / Rate;
                float env = Mathf.Exp(-u * 4f);
                pcm[i] = gain * env * Mathf.Sin(phase);
            }
            return pcm;
        }

        private static float[] Snap()
        {
            int n = Mathf.CeilToInt(0.08f * Rate);
            var pcm = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Exp(-t / 0.012f);
                pcm[i] = 0.55f * env *
                    (Mathf.Sin(2f * Mathf.PI * 1250f * t) +
                     0.4f * Mathf.Sin(2f * Mathf.PI * 2500f * t));
            }
            return pcm;
        }

        private static float[] Buzz()
        {
            int n = Mathf.CeilToInt(0.22f * Rate);
            var pcm = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float gate = (t < 0.09f || (t > 0.12f && t < 0.21f)) ? 1f : 0f;
                pcm[i] = 0.4f * gate *
                    (Mathf.Sin(2f * Mathf.PI * 160f * t) +
                     0.3f * Mathf.Sin(2f * Mathf.PI * 480f * t));
            }
            return pcm;
        }

        private static float[] PowerUp()
        {
            int n = Mathf.CeilToInt(0.45f * Rate);
            var pcm = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;
                phase += 2f * Mathf.PI * Mathf.Lerp(300f, 920f, u * u) / Rate;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.05f));
                pcm[i] = 0.45f * env * Mathf.Sin(phase);
            }
            return pcm;
        }
    }
}
