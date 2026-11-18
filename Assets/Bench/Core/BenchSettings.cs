// BenchSettings.cs — persistent user settings (PlayerPrefs JSON).
// Drives theme toggles: transparency, motion, text size, audio,
// brightness, handedness, demo mode. Fires Changed on any edit.
using System;
using UnityEngine;

namespace Bench.Core
{
    public static class BenchSettings
    {
        public static event Action Changed;

        [Serializable]
        private class Data
        {
            public bool reduceTransparency;
            public bool reduceMotion;
            public float textScale = 1f;
            public bool leftHanded;
            public bool audioMuted;
            public float brightness = 1f;
            public bool demoMode = true;
            public bool coachSeen;
        }

        private const string Key = "bench.settings.v1";
        private static Data _d;

        private static Data D
        {
            get
            {
                if (_d == null)
                {
                    try { _d = JsonUtility.FromJson<Data>(PlayerPrefs.GetString(Key, "{}")) ?? new Data(); }
                    catch { _d = new Data(); }
                    if (_d == null) _d = new Data();
                    if (_d.textScale <= 0) _d.textScale = 1f;
                    _d.brightness = Mathf.Clamp01(_d.brightness <= 0 ? 1f : _d.brightness);
                }
                return _d;
            }
        }

        private static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(D));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static bool ReduceTransparency
        {
            get => D.reduceTransparency;
            set { D.reduceTransparency = value; Save(); }
        }

        public static bool ReduceMotion
        {
            get => D.reduceMotion;
            set { D.reduceMotion = value; Save(); }
        }

        /// <summary>1, 1.25 or 1.5 (clamped).</summary>
        public static float TextScale
        {
            get => D.textScale;
            set { D.textScale = Mathf.Clamp(value, 1f, 1.5f); Save(); }
        }

        public static bool LeftHanded
        {
            get => D.leftHanded;
            set { D.leftHanded = value; Save(); }
        }

        public static bool AudioMuted
        {
            get => D.audioMuted;
            set { D.audioMuted = value; Save(); }
        }

        public static float Brightness
        {
            get => D.brightness;
            set { D.brightness = Mathf.Clamp01(value); Save(); }
        }

        public static bool DemoMode
        {
            get => D.demoMode;
            set { D.demoMode = value; Save(); }
        }

        public static bool CoachSeen
        {
            get => D.coachSeen;
            set { D.coachSeen = value; Save(); }
        }
    }
}
