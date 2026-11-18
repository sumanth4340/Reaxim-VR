// Waveforms.cs — signal synthesis + measurement for Task 8.
// Instruments resolve each probed net to a NetSignal (from the twin
// probe views when live, else from CircuitModel), then synthesize,
// trigger, measure and classify here. All buffers caller-owned and
// reused: no per-frame allocations.
using System;

namespace Bench.Tools
{
    public enum SignalKind { Dc, Square, High }

    public struct NetSignal
    {
        public SignalKind kind;
        public float dc;     // volts (DC level, or square low+high mean base)
        public float high;   // square top (3.3 for GPIO)
        public float freqHz;
        public float duty;   // 0..1
    }

    public static class Waveforms
    {
        public const int BufN = 240;

        public static void Sample(NetSignal s, float t0, float dt,
            float[] buf)
        {
            int n = buf.Length;
            switch (s.kind)
            {
                case SignalKind.Dc:
                    for (int i = 0; i < n; i++) buf[i] = s.dc;
                    break;
                case SignalKind.High:
                    for (int i = 0; i < n; i++) buf[i] = s.high;
                    break;
                default: // Square
                    float period = s.freqHz > 0.01f ? 1f / s.freqHz : 1f;
                    for (int i = 0; i < n; i++)
                    {
                        float ph = ((t0 + i * dt) / period) % 1f;
                        if (ph < 0f) ph += 1f;
                        buf[i] = ph < s.duty ? s.high : s.dc;
                    }
                    break;
            }
        }

        public static void Measure(float[] buf, float dt, out float vpp,
            out float freq, out float duty, out float vdc, out float vrmsAc)
        {
            int n = buf.Length;
            float min = float.MaxValue, max = float.MinValue, sum = 0f;
            for (int i = 0; i < n; i++)
            {
                float v = buf[i];
                if (v < min) min = v;
                if (v > max) max = v;
                sum += v;
            }
            vpp = max - min;
            vdc = sum / Math.Max(1, n);
            float mid = (min + max) / 2f;
            int rise = 0, high = 0, first = -1, last = -1;
            for (int i = 1; i < n; i++)
            {
                if (buf[i - 1] < mid && buf[i] >= mid)
                {
                    if (first < 0) first = i;
                    last = i;
                    rise++;
                }
                if (buf[i] >= mid) high++;
            }
            float window = n * dt;
            // Span-based: exact even when the buffer starts mid-high
            // (hiding one edge) or a trigger rotation broke an edge.
            if (rise >= 2 && last > first && dt > 0f)
                freq = (rise - 1) / ((last - first) * dt);
            else freq = window > 0f ? rise / window : 0f;
            duty = n > 1 ? high / (float)(n - 1) : 0f;
            double acc = 0;
            for (int i = 0; i < n; i++)
            {
                double d = buf[i] - vdc;
                acc += d * d;
            }
            vrmsAc = (float)Math.Sqrt(acc / Math.Max(1, n));
        }

        /// <summary>Rotate buffer so a trigger crossing sits at 20%.
        /// Scratch must be at least buf.Length (no allocation here).</summary>
        public static void TriggerAlign(float[] buf, float level, bool rising,
            float[] scratch)
        {
            int n = buf.Length;
            int at = (int)(n * 0.2f);
            int found = -1;
            for (int i = 1; i < n; i++)
            {
                bool cross = rising
                    ? buf[i - 1] < level && buf[i] >= level
                    : buf[i - 1] > level && buf[i] <= level;
                if (cross) { found = i; break; }
            }
            if (found < 0 || scratch.Length < n) return;
            Array.Copy(buf, scratch, n);
            for (int i = 0; i < n; i++)
                buf[i] = scratch[(found - at + i + n * 2) % n];
        }

        public static void Fft64(float[] real, float[] mag32)
        {
            const int N = 64;
            for (int k = 0; k < 32; k++)
            {
                double re = 0, im = 0;
                for (int i = 0; i < N; i++)
                {
                    double a = -2.0 * Math.PI * k * i / N;
                    re += real[i] * Math.Cos(a);
                    im += real[i] * Math.Sin(a);
                }
                mag32[k] = (float)Math.Sqrt(re * re + im * im) / N;
            }
        }

        public static string Classify(float vpp, float freq, float duty)
        {
            if (vpp < 0.2f) return "DC";
            bool square = duty > 0.01f && duty < 0.99f && freq > 0.5f;
            if (square && freq > 50000f) return "Digital burst";
            if (square) return "PWM";
            if (freq > 0.5f) return "AC";
            return "DC";
        }

        public static string FormatFreq(float hz)
        {
            if (hz >= 1000f) return (hz / 1000f).ToString("F1") + " kHz";
            if (hz >= 1f) return hz.ToString("F0") + " Hz";
            return hz.ToString("F2") + " Hz";
        }

        public static string FormatVolts(float v)
        {
            if (Math.Abs(v) >= 1f) return v.ToString("F2") + " V";
            return (v * 1000f).ToString("F0") + " mV";
        }
    }
}
