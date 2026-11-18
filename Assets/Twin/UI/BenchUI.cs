// BenchUI.cs — procedural uGUI factory for the MR Bench screens.
// Builds every widget from the concept HTML at runtime (no prefabs):
// frosted-glass panels with a 135° gradient, pill buttons, rows, cards,
// search fields, hints — all sized in design px (BenchTheme.Px converts
// to metres). Text is TextMeshPro; fonts are injected by
// BenchSceneSetup (Arial regular/bold, matching the mock's Arial).

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class Art
{
    // ── procedural sprites, cached by parameter key ───────────────
    static readonly Dictionary<string, Sprite> Cache = new();

    static Sprite Bake(string key, int w, int h, Color32[] px)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h),
            new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
    }

    /// Frosted glass panel: rounded rect, 135° gradient fill, 1.5px
    /// border, soft inner top highlight. Size in design px.
    public static Sprite Glass(float wF, float hF, float radiusF,
                               Color top, Color bottom,
                               Color border, float borderPx = 1.5f)
    {
        int w = Mathf.CeilToInt(wF), h = Mathf.CeilToInt(hF);
        float r = radiusF;
        string k = $"glass{r * 10:F0}:{w}x{h}";
        if (Cache.TryGetValue(k, out var s)) return s;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // rounded-rect distance (px), negative = outside
                float dx = Mathf.Max(0, Mathf.Abs(x - (w - 1) / 2f) - (w / 2f - r));
                float dy = Mathf.Max(0, Mathf.Abs(y - (h - 1) / 2f) - (h / 2f - r));
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float edge = Mathf.Clamp01(r - dist + 0.5f);

                // 135° gradient: light at top-left, dark at bottom-right
                float g = Mathf.Clamp01((x + (h - 1 - y)) / (float)(w + h - 2));
                Color c = Color.Lerp(bottom, top, g);
                // inner top highlight along the top rim
                if (y < h * 0.18f)
                    c = Color.Lerp(c, BenchTheme.InnerHighlight,
                        0.25f * (1f - y / (h * 0.18f)));

                // border ring just inside the edge
                float ring = Mathf.Max(0, dist - (r - borderPx));
                if (ring <= borderPx + 0.5f)
                {
                    float a = Mathf.Clamp01(borderPx + 0.5f - ring);
                    c = Color.Lerp(c, border, a * 0.9f);
                }
                c.a *= edge;
                px[y * w + x] = c;
            }
        }
        s = Bake(k, w, h, px);
        Cache[k] = s;
        return s;
    }

    /// Flat rounded rect (rows, cards, scope screens, chips).
    public static Sprite Rounded(float wF, float hF, float radiusF,
                                 Color fill, Color? border = null,
                                 float borderPx = 1.5f)
    {
        int w = Mathf.CeilToInt(wF), h = Mathf.CeilToInt(hF);
        float r = radiusF;
        string k = $"r{r * 10:F0}{ColorUtility.ToHtmlStringRGBA(fill)}" +
                   $"{ColorUtility.ToHtmlStringRGBA(border ?? Color.clear)}:{w}x{h}";
        if (Cache.TryGetValue(k, out var s)) return s;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x - (w - 1) / 2f) - (w / 2f - r));
                float dy = Mathf.Max(0, Mathf.Abs(y - (h - 1) / 2f) - (h / 2f - r));
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float edge = Mathf.Clamp01(r - dist + 0.5f);
                Color c = fill;
                if (border.HasValue)
                {
                    float ring = Mathf.Max(0, dist - (r - borderPx));
                    if (ring <= borderPx + 0.5f)
                    {
                        float a = Mathf.Clamp01(borderPx + 0.5f - ring);
                        c = Color.Lerp(c, border.Value, a * 0.95f);
                    }
                }
                c.a *= edge;
                px[y * w + x] = c;
            }
        }
        s = Bake(k, w, h, px);
        Cache[k] = s;
        return s;
    }

    /// Soft blurred shadow quad to sit behind a panel (0 24 60 black .35).
    public static Sprite Shadow(float wF, float hF, float radiusF)
    {
        int w = Mathf.CeilToInt(wF), h = Mathf.CeilToInt(hF);
        string k = $"shadow:{w}x{h}";
        if (Cache.TryGetValue(k, out var s)) return s;
        var px = new Color32[w * h];
        float r = radiusF;
        float blur = Mathf.Max(24f, Mathf.Min(w, h) * 0.12f);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x - (w - 1) / 2f) - (w / 2f - r));
                float dy = Mathf.Max(0, Mathf.Abs(y - (h - 1) / 2f) - (h / 2f - r));
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - dist / blur);
                a = a * a * (3 - 2 * a); // smoothstep
                px[y * w + x] = (Color)new Color(0, 0, 0, 0.35f * a);
            }
        }
        s = Bake(k, w, h, px);
        Cache[k] = s;
        return s;
    }

    // ── small vector glyphs (pin shapes, status dots) ─────────────
    const int GlyphN = 48;

    public static Sprite Circle(Color c)
    {
        string k = "circle" + ColorUtility.ToHtmlStringRGBA(c);
        if (Cache.TryGetValue(k, out var s)) return s;
        var px = new Color32[GlyphN * GlyphN];
        for (int y = 0; y < GlyphN; y++)
            for (int x = 0; x < GlyphN; x++)
            {
                float d = Mathf.Sqrt(Sq(x - GlyphN / 2f + .5f)
                                   + Sq(y - GlyphN / 2f + .5f)) - GlyphN / 2f + 2f;
                var cc = c; cc.a *= Mathf.Clamp01(d);
                px[y * GlyphN + x] = cc;
            }
        s = Bake(k, GlyphN, GlyphN, px); Cache[k] = s; return s;
    }

    /// Upright triangle (power-pin marker ▲).
    public static Sprite Triangle(Color c)
    {
        string k = "tri" + ColorUtility.ToHtmlStringRGBA(c);
        if (Cache.TryGetValue(k, out var s)) return s;
        var px = new Color32[GlyphN * GlyphN];
        for (int y = 0; y < GlyphN; y++)
            for (int x = 0; x < GlyphN; x++)
            {
                // half-planes of an inscribed triangle, AA over 1.5px
                float nx = (x + .5f) / GlyphN * 2 - 1;
                float ny = 1 - (y + .5f) / GlyphN * 2; // y up
                float dLeft = (0.82f * nx + 1.6f * ny) - 1f;
                float dRight = (-0.82f * nx + 1.6f * ny) - 1f;
                float dBottom = -ny - 0.86f;
                float d = Mathf.Max(dLeft, Mathf.Max(dRight, dBottom));
                var cc = c; cc.a *= Mathf.Clamp01(0.5f - d / 0.06f);
                px[y * GlyphN + x] = cc;
            }
        s = Bake(k, GlyphN, GlyphN, px); Cache[k] = s; return s;
    }

    public static Sprite Square(Color c)
    {
        string k = "sq" + ColorUtility.ToHtmlStringRGBA(c);
        if (Cache.TryGetValue(k, out var s)) return s;
        var px = new Color32[GlyphN * GlyphN];
        const int inset = 8;
        for (int y = 0; y < GlyphN; y++)
            for (int x = 0; x < GlyphN; x++)
            {
                float d = Mathf.Max(inset - x, x - (GlyphN - inset),
                                    inset - y, y - (GlyphN - inset));
                var cc = c; cc.a *= Mathf.Clamp01(0.5f - d / 1f);
                px[y * GlyphN + GlyphN - 1 - x] = cc;
            }
        s = Bake(k, GlyphN, GlyphN, px); Cache[k] = s; return s;
    }

    /// Hollow ring with AA edge (wire endpoints, valid-target glow).
    public static Sprite Ring(Color c, float thickness = 0.12f)
    {
        string k = "ring" + ColorUtility.ToHtmlStringRGBA(c) + thickness;
        if (Cache.TryGetValue(k, out var s)) return s;
        const int n = 64;
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt(Sq(x - n / 2f + .5f) + Sq(y - n / 2f + .5f))
                          / (n / 2f); // 0 centre → 1 edge
                // band between (1-thickness) and 1
                float band = Mathf.Clamp01((d - (1f - thickness)) / (thickness * 0.5f))
                           * Mathf.Clamp01((1f - d) / (thickness * 0.5f));
                var cc = c; cc.a *= band;
                px[y * n + x] = cc;
            }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear };
        tex.SetPixels32(px); tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, n, n),
            new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
        Cache[k] = s; return s;
    }

    /// Dashed ring (the concept's "valid targets glow green" marker).
    public static Sprite DashRing(Color c)
    {
        string k = "dashring" + ColorUtility.ToHtmlStringRGBA(c);
        if (Cache.TryGetValue(k, out var s)) return s;
        const int n = 96, dashes = 12;
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x - n / 2f + .5f, dy = y - n / 2f + .5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / (n / 2f);
                float band = Mathf.Clamp01((d - 0.82f) / 0.09f)
                           * Mathf.Clamp01((1f - d) / 0.09f);
                float ang = Mathf.Atan2(dy, dx) / (2 * Mathf.PI) + 1f;
                float seg = ang * dashes;
                bool on = seg % 1f < 0.62f;
                var cc = c; cc.a *= band * (on ? 1f : 0f);
                px[y * n + x] = cc;
            }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Bilinear };
        tex.SetPixels32(px); tex.Apply();
        s = Sprite.Create(tex, new Rect(0, 0, n, n),
            new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
        Cache[k] = s; return s;
    }

    static float Sq(float v) => v * v;
}

/// Widget factory + layout helpers. All coordinates are design px with
/// (0,0) at the TOP-LEFT of the parent, y growing downward — same as
/// the concept CSS, so panel geometry transcribes directly.
public static class BenchUI
{
    public static TMP_FontAsset Font;       // Arial regular
    public static TMP_FontAsset FontBold;   // Arial bold

    /// Resolve fonts: baked Resources assets first (device builds),
    /// then a dynamic OS-font TMP asset, then the TMP package's
    /// LiberationSans SDF (Arial metric-compatible). Idempotent.
    public static void EnsureFonts()
    {
        if (Font == null) Font = Resources.Load<TMP_FontAsset>("BenchArial");
        if (FontBold == null) FontBold = Resources.Load<TMP_FontAsset>("BenchArialBold");
        if (Font == null) Font = MakeOsFont("Arial");
        if (FontBold == null) FontBold = MakeOsFont("Arial Bold");
        if (Font == null || FontBold == null)
        {
            var libSans = Resources.Load<TMP_FontAsset>("LiberationSans SDF")
                ?? TMP_Settings.defaultFontAsset;
            if (libSans != null)
            {
                if (Font == null) Font = libSans;
                if (FontBold == null) FontBold = libSans;
                Debug.Log("[BenchUI] using LiberationSans SDF " +
                    "(Arial-metric) for bench text");
            }
            else Debug.LogWarning("[BenchUI] no bench font resolved — " +
                "run Twin ▸ Setup Bench UI to import TMP essentials");
        }
    }

    static TMP_FontAsset MakeOsFont(string family)
    {
        try
        {
            var os = UnityEngine.Font.CreateDynamicFontFromOSFont(family, 44);
            return TMP_FontAsset.CreateFontAsset(os);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[BenchUI] OS font '" + family + "' failed: "
                + e.Message);
            return null;
        }
    }

    // ── canvases ──────────────────────────────────────────────────
    /// World-space canvas sized w×h design px, centred per the CSS rect.
    public static Canvas WorldCanvas(Transform parent, string name,
                                     float cssLeft, float cssTop,
                                     float w, float h, float z = -1)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(GraphicRaycaster));
        var c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        c.sortingOrder = 10;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = new Vector2(w, h);
        if (z < 0) z = BenchTheme.PlaneZ;
        rt.position = BenchTheme.StagePos(cssLeft, cssTop, w, h, z);
        // NOTE: uGUI world canvases already face the −Z viewer — do
        // NOT rotate 180° about Y here, that mirrors every glyph.
        // (uiRoot supplies the yaw so the stage follows the head.)
        rt.rotation = Quaternion.identity;
        rt.localScale = Vector3.one * BenchTheme.Px;
        c.worldCamera = Camera.main;
        return c;
    }

    /// Child rect at CSS coordinates (x,y from top-left of parent).
    public static RectTransform Rect(Transform parent, string name,
                                     float x, float yTop, float w, float h)
    {
        var rt = new GameObject(name, typeof(RectTransform))
            .transform as RectTransform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -yTop);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    public static RectTransform Fill(Transform parent, string name)
    {
        var rt = new GameObject(name, typeof(RectTransform)).transform as RectTransform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    // ── panels ────────────────────────────────────────────────────
    /// Glass panel (light or dark) with drop shadow; returns its rect.
    public static RectTransform Panel(Transform parent, string name,
                                      float x, float yTop, float w, float h,
                                      bool dark = false,
                                      float radius = BenchTheme.RPanel,
                                      bool shadow = true)
    {
        if (shadow)
        {
            var shRect = Rect(parent, name + "Shadow", x, yTop + 8, w, h);
            var sh = shRect.gameObject.AddComponent<Image>();
            sh.sprite = Art.Shadow(w, h, radius);
            sh.raycastTarget = false;
        }
        var rt = Rect(parent, name, x, yTop, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Art.Glass(w, h, radius,
            dark ? BenchTheme.GlassDarkTop : BenchTheme.GlassLightTop,
            dark ? BenchTheme.GlassDarkBottom : BenchTheme.GlassLightBottom,
            BenchTheme.GlassBorder);
        img.type = Image.Type.Sliced;
        img.raycastTarget = true;
        return rt;
    }

    // ── text ──────────────────────────────────────────────────────
    public static TextMeshProUGUI Label(Transform parent, string name,
                                        string text, float x, float yTop,
                                        float w, float sizePx, Color color,
                                        bool bold = false,
                                        TextAlignmentOptions align = TextAlignmentOptions.TopLeft,
                                        float h = 0)
    {
        var rt = Rect(parent, name, x, yTop, w, Mathf.Max(h, sizePx * 1.5f));
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = sizePx;
        t.color = color;
        t.alignment = align;
        t.font = bold ? FontBold : Font;
        t.raycastTarget = false;
        t.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        t.richText = true;  // status/hint labels use <color>/<alpha>
        return t;
    }

    // ── pill button ──────────────────────────────────────────────
    /// .tb from the concepts: white .45 border, radius 22, bold 17px.
    public static BenchButton Button(Transform parent, string label,
                                     float x, float yTop, float w,
                                     float h = BenchTheme.BtnH,
                                     bool active = false, float fontSize = 17f)
        => Button(parent, label, x, yTop, w, h, active, fontSize, "Btn" + label);

    public static BenchButton Button(Transform parent, string label,
                                     float x, float yTop, float w, float h,
                                     bool active, float fontSize, string name)
    {
        var rt = Rect(parent, name, x, yTop, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        var b = rt.gameObject.AddComponent<BenchButton>();
        b.Init(img, label, active, fontSize);
        return b;
    }
}

/// .tb pill button with normal / hover / pressed / active visuals.
public class BenchButton : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    Image bg;
    TextMeshProUGUI label;
    bool active;
    float radius = BenchTheme.RButton;

    public void Init(Image bg, string text, bool activeState, float fontSize)
    {
        this.bg = bg;
        active = activeState;
        bg.color = Color.white;
        // label lives on a child rect: a GameObject can host only one
        // Graphic (the pill Image is already on this one)
        var labelRt = new GameObject("label", typeof(RectTransform))
            .transform as RectTransform;
        labelRt.SetParent(transform, false);
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.sizeDelta = Vector2.zero;
        label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Refresh();
    }

    /// Toggle .on state (tabs, filters).
    public void SetActive(bool on)
    {
        active = on;
        Refresh();
    }

    public void SetText(string t) => label.text = t;

    void Refresh()
    {
        var r = (RectTransform)transform;
        bg.sprite = Art.Rounded(r.rect.width, r.rect.height, radius,
            active ? BenchTheme.ActiveBg : Color.clear,
            active ? BenchTheme.ActiveBorder : BenchTheme.BtnBorder, 1.5f);
        bg.type = Image.Type.Sliced;
        label.color = active ? BenchTheme.ActiveText : BenchTheme.Text;
        label.font = BenchUI.FontBold ?? BenchUI.Font;
    }

    public void OnPointerEnter(PointerEventData _) => Hover(true);
    public void OnPointerExit(PointerEventData _) => Hover(false);
    void Hover(bool on)
    {
        if (active) return;
        bg.color = on ? new Color(1, 1, 1, 0.10f) : Color.white;
        label.color = on ? BenchTheme.CyanLight : BenchTheme.Text;
    }

    public void OnPointerDown(PointerEventData _) => transform.localScale = Vector3.one * 0.96f;
    public void OnPointerUp(PointerEventData _) => transform.localScale = Vector3.one;
}
