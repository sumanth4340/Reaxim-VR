// BenchTheme.cs — design tokens for the MR Bench UI.
// Every colour/size/radius here is transcribed 1:1 from the
// "MR Electronics Bench" interface concepts (the HTML mockups):
// glass panels, pill buttons, rows, cards, pin badges, scope.
// One source of truth so every screen matches the concept exactly.

using UnityEngine;

/// Design-space is the 1280×720 canvas the concepts were drawn on.
/// 1 design px = BenchTheme.Px metres in world space.
public static class BenchTheme
{
    // ── Stage ─────────────────────────────────────────────────────
    public const float StageW = 1280, StageH = 720;

    /// World metres per design px (1280 px ≈ 2.3 m of view at ~1.3 m).
    public const float Px = 0.0018f;

    /// UI plane sits this far in front of the (seated) user, facing them.
    public const float PlaneZ = 1.15f;

    /// Vertical bias of the design-stage centre relative to the
    /// head (BenchRoot anchors the UI stage at the camera).
    public const float PlaneCenterY = 0f;

    /// Convert a concept-anchored rect (CSS left/top + size) to the
    /// world-space centre + size of the panel, on the UI plane.
    public static Vector3 StagePos(float cssLeft, float cssTop,
                                   float w, float h, float z = PlaneZ)
    {
        float x = (cssLeft + w / 2f - StageW / 2f) * Px;
        float y = PlaneCenterY + (StageH / 2f - (cssTop + h / 2f)) * Px;
        return new Vector3(x, y, z);
    }

    public static Vector2 StageSize(float w, float h) =>
        new Vector2(w * Px, h * Px);

    // ── Glass ─────────────────────────────────────────────────────
    // .gl — light frosted glass
    public static readonly Color GlassLightTop = new Color(1f, 1f, 1f, 0.24f);
    public static readonly Color GlassLightBottom = new Color(1f, 1f, 1f, 0.08f);
    // .gl.dk — dark docked glass
    public static readonly Color GlassDarkTop = new Color32(18, 24, 32, 199); // .78
    public static readonly Color GlassDarkBottom = new Color32(18, 24, 32, 153); // .60

    public static readonly Color GlassBorder = new Color(1f, 1f, 1f, 0.40f);
    public static readonly Color InnerHighlight = new Color(1f, 1f, 1f, 0.55f);

    // 0 24px 60px rgba(0,0,0,.35) — drawn as a soft blurred quad behind.
    public static readonly Color DropShadow = new Color(0f, 0f, 0f, 0.35f);
    public const float ShadowOffsetY = -24 * Px * 10f; // visually tuned
    public const float ShadowBlur = 60 * Px * 10f;

    // Radii (design px)
    public const float RPanel = 24f, RPill = 30f, RButton = 22f,
        RRow = 20f, RCard = 22f, RPinBadge = 15f, RScreen = 10f,
        RTile = 20f, RSmall = 18f;

    // ── Buttons (.tb) ──────────────────────────────────────────────
    public const float BtnH = 44f, BtnPadX = 20f, BtnFont = 17f;
    public static readonly Color BtnBorder = new Color(1f, 1f, 1f, 0.45f);

    // .on — active state
    public static readonly Color ActiveBg = new Color32(92, 225, 230, 56); // .22
    public static readonly Color ActiveBorder = Hex(0x5CE1E6);
    public static readonly Color ActiveText = Hex(0x8DF3F6);

    // ── Rows / cards ─────────────────────────────────────────────
    public static readonly Color RowBg = new Color(1f, 1f, 1f, 0.12f);
    public static readonly Color RowBorder = new Color(1f, 1f, 1f, 0.20f);
    public static readonly Color CardBg = new Color(1f, 1f, 1f, 0.13f);
    public static readonly Color CardBorder = new Color(1f, 1f, 1f, 0.25f);
    public static readonly Color RowSelectedBg = new Color32(92, 225, 230, 41); // .16
    public static readonly Color RowSelectedBorder = Hex(0x5CE1E6);
    public const float RowH = 78f, RowHSmall = 64f, CardH = 126f;

    // ── Type ──────────────────────────────────────────────────────
    public const float FontTitle = 30f;      // 700 home title
    public const float FontPanel = 17f;      // 600 panel titles
    public const float FontRowName = 21f;    // 600 row/card names
    public const float FontBody = 16f;       // body / meta
    public const float FontSmall = 15f;      // .sm
    public const float FontPinBadge = 15f;   // 600
    public const float FontBigValue = 26f;   // 700 scope DC volts
    public const float FontHint = 15f;
    public static readonly Color Text = Color.white;
    public static readonly Color TextSubtle = new Color(1f, 1f, 1f, 0.80f); // .sm
    public static readonly Color TextHint = new Color(1f, 1f, 1f, 0.88f);

    // ── Accents ───────────────────────────────────────────────────
    public static Color Hex(int rgb) => new Color32(
        (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

    public static readonly Color Cyan = Hex(0x5CE1E6);        // wires, active
    public static readonly Color CyanLight = Hex(0x8DF3F6);  // active text
    public static readonly Color Green = Hex(0x4ADE80);       // live / valid
    public static readonly Color Red = Hex(0xFF6B6B);        // LED / power pin
    public static readonly Color RedDeep = Hex(0xE5484D);    // probe / rail+
    public static readonly Color Amber = Hex(0xFFB454);      // CH2
    public static readonly Color Gold = Hex(0xE8D26A);       // header pins
    public static readonly Color GndWhite = Hex(0xD0D6DE);   // GND badges

    // ── Electronics palette (from the concept's 3D mock parts) ────
    public static readonly Color Pcb = Hex(0x2F8F6B);
    public static readonly Color PcbGradTop = Hex(0x2A9E70);
    public static readonly Color PcbGradBottom = Hex(0x176B49);
    public static readonly Color PcbJetsonTop = Hex(0x2D323A);
    public static readonly Color PcbJetsonBottom = Hex(0x1B1E24);
    public static readonly Color Silver = Hex(0xC9CED6);
    public static readonly Color ChipDark = Hex(0x15181D);
    public static readonly Color SocGrey = Hex(0x9AA3AD);
    public static readonly Color JetsonGreen = Hex(0x76B900);
    public static readonly Color Breadboard = Hex(0xE9E6DE);
    public static readonly Color RailRed = Hex(0xE5484D);
    public static readonly Color RailBlue = Hex(0x3E63DD);
    public static readonly Color ScreenBg = Hex(0x0A0A0A);
    public static readonly Color GridLine = new Color(1f, 1f, 1f, 0.14f);
    public static readonly Color HoleGrey = Hex(0x8A8F98);

    // ── Room (Editor / simulator backdrop) ─────────────────────────
    public static readonly Color RoomTop = Hex(0x6D8199);
    public static readonly Color RoomMid = Hex(0x4A5A6D);
    public static readonly Color RoomDesk = Hex(0x3B3128);
    public static readonly Color RoomBottom = Hex(0x241D17);
    public static readonly Color LampGlow = Hex(0xFFD9A0);
    public static readonly Color DimOverlay = new Color32(28, 38, 54, 102); // .4

    // ── Pin badge geometry (.lb) ───────────────────────────────────
    public const float BadgeW = 74f, BadgeH = 30f, BadgeBorder = 1.5f;
    public static readonly Color BadgeBg = new Color32(14, 18, 24, 217); // .85

    // ── Scope grid (concept: 38×24.5 px cells) ─────────────────────
    public const float ScopeCellW = 38f, ScopeCellH = 24.5f;

    public static Color WithA(Color c, float a)
    {
        c.a = a; return c;
    }
}
