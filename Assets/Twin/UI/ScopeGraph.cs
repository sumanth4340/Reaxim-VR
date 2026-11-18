// ScopeGraph.cs — the oscilloscope screen from the Testing concept:
// near-black #0A0A0A screen, faint 38×24.5 grid, CH1 solid cyan and
// CH2 dashed amber step traces, driven live by the twin's probe
// views (edges ages → scrolling window). A uGUI Graphic that builds
// its own mesh, so it sits inside the glass panel like any widget.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

public class ScopeGraph : MaskableGraphic
{
    public float windowSeconds = 2f;
    public Color ch1Color = BenchTheme.Cyan;
    public Color ch2Color = BenchTheme.Amber;

    TwinBridge.ProbeView? ch1, ch2;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var r = GetPixelAdjustedRect();
        if (r.width < 1 || r.height < 1) return;

        // ── grid: 38×24.5 cells, lines white .14 (concept pattern) ──
        var grid = BenchTheme.GridLine;
        for (float gx = r.x; gx <= r.x + r.width + 0.1f;
             gx += BenchTheme.ScopeCellW)
            VLine(vh, r, gx, grid, 1.4f);
        for (float gy = r.y; gy <= r.y + r.height + 0.1f;
             gy += BenchTheme.ScopeCellH)
            HLine(vh, r, gy, grid, 1.4f);

        // ── traces ─────────────────────────────────────────────────
        if (ch1.HasValue) Trace(vh, r, ch1.Value, ch1Color, dashed: false);
        if (ch2.HasValue) Trace(vh, r, ch2.Value, ch2Color, dashed: true);
    }

    // ── helpers: line segments as thin quads ──────────────────────
    void VLine(VertexHelper vh, Rect r, float x, Color c, float w)
    {
        if (x > r.x + r.width) return;
        Quad(vh, new Vector2(x - w / 2, r.yMin),
             new Vector2(x + w / 2, r.yMin),
             new Vector2(x - w / 2, r.yMax),
             new Vector2(x + w / 2, r.yMax), c);
    }

    void HLine(VertexHelper vh, Rect r, float y, Color c, float h)
    {
        if (y > r.y + r.height) return;
        Quad(vh, new Vector2(r.xMin, y - h / 2),
             new Vector2(r.xMax, y - h / 2),
             new Vector2(r.xMin, y + h / 2),
             new Vector2(r.xMax, y + h / 2), c);
    }

    void Quad(VertexHelper vh, Vector2 bl, Vector2 br, Vector2 tl,
              Vector2 tr, Color c)
    {
        int i = vh.currentVertCount;
        vh.AddVert(bl, c, Vector2.zero);
        vh.AddVert(br, c, Vector2.one);
        vh.AddVert(tl, c, Vector2.zero);
        vh.AddVert(tr, c, Vector2.one);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i + 2, i + 1, i + 3);
    }

    void Segment(VertexHelper vh, Vector2 a, Vector2 b, Color c,
                 float thickness, bool dashed)
    {
        const float dashLen = 8f, gapLen = 5f;
        if (!dashed)
        {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x) * (thickness / 2f);
            Quad(vh, a - n, a + n, b - n, b + n, c);
            return;
        }
        float len = Vector2.Distance(a, b);
        float t = 0;
        while (t < len)
        {
            float step = Mathf.Min(dashLen, len - t);
            var p0 = Vector2.Lerp(a, b, t / len);
            var p1 = Vector2.Lerp(a, b, (t + step) / len);
            var d = (p1 - p0).normalized;
            var n = new Vector2(-d.y, d.x) * (thickness / 2f);
            Quad(vh, p0 - n, p0 + n, p1 - n, p1 + n, c);
            t += dashLen + gapLen;
        }
    }

    /// One channel's step waveform: edges are (age, level) pairs.
    void Trace(VertexHelper vh, Rect r, TwinBridge.ProbeView pv,
               Color c, bool dashed)
    {
        // screen coords: x = now→left .. window→right (scrolling),
        // y = low level at 25% height, high at 75%.
        float X(float age) => r.xMax - Mathf.Clamp(age, 0f, windowSeconds)
                              / windowSeconds * r.width;
        float Y(bool level) => level
            ? r.yMin + r.height * 0.25f
            : r.yMin + r.height * 0.75f;
        const float thickness = 3f;

        var edges = pv.edges;
        if (edges == null || edges.Count == 0)
        {
            // unknown: flat dashed line mid-screen ("flat unknown line")
            Segment(vh, new Vector2(r.xMin, r.yMin + r.height / 2),
                    new Vector2(r.xMax, r.yMin + r.height / 2), c,
                    thickness, dashed);
            return;
        }

        // Build the step polyline, oldest (largest age) first.
        var pts = new List<Vector2>();
        bool level = edges.Count > 0 ? edges[edges.Count - 1].y > 0.5f : false;
        pts.Add(new Vector2(r.xMax, Y(level)));
        for (int i = edges.Count - 1; i >= 0; i--)
        {
            var e = edges[i];
            if (e.x > windowSeconds) continue; // clipped
            bool lvl = e.y > 0.5f;
            pts.Add(new Vector2(X(e.x), Y(level)));  // horizontal to edge
            if (lvl != level)
            {
                pts.Add(new Vector2(X(e.x), Y(lvl))); // vertical flip
                level = lvl;
            }
        }
        pts.Add(new Vector2(r.xMin, Y(level)));

        for (int i = 0; i < pts.Count - 1; i++)
            Segment(vh, pts[i], pts[i + 1], c, thickness, dashed);
    }

    // ── data feed ────────────────────────────────────────────────
    /// Push this frame's probe views (JArray from the snapshot).
    public void SetProbes(JArray probes, float window)
    {
        windowSeconds = window;
        ch1 = null; ch2 = null;
        if (probes == null) { SetVerticesDirty(); return; }
        if (probes.Count > 0) ch1 = TwinBridge.ParseProbe(probes[0]);
        if (probes.Count > 1) ch2 = TwinBridge.ParseProbe(probes[1]);
        SetVerticesDirty();
    }

    /// Readouts for the legend column (concept: Vpp · kHz · Duty · DC).
    public string Ch1Legend => ch1.HasValue
        ? $"CH1 solid · GPIO{ch1.Value.pin}" : "CH1 solid · —";
    public string Ch2Legend => ch2.HasValue
        ? $"CH2 dashed · GPIO{ch2.Value.pin}" : "CH2 dashed · 3V3";

    public string StatsLine
    {
        get
        {
            var p = ch1;
            if (!p.HasValue) return "";
            string s = "";
            if (p.Value.freqHz.HasValue)
            {
                double f = p.Value.freqHz.Value;
                s += f >= 1000
                    ? $"Vpp 3.3 V · {f / 1000f:F1} kHz"
                    : $"Vpp 3.3 V · {f:F0} Hz";
            }
            else s += "Vpp 3.3 V";
            if (p.Value.duty.HasValue)
                s += $" · Duty {p.Value.duty.Value * 100f:F0}%";
            return s;
        }
    }

    public string DcLine => ch1.HasValue && ch1.Value.dcVolts.HasValue
        ? $"{ch1.Value.dcVolts.Value:F2} V DC" : "—";
}
