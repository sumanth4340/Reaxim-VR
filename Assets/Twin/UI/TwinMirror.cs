// TwinMirror.cs — applies bridge snapshots (real or mock — both are
// ITwinLink) to the desk: spawns/moves/removes part models, wires
// pins with WireRenderer tubes, exposes pin targets for the wiring
// flow, and routes UI commands back over the active link.
//
// Snapshot schema (BridgeProtocol.swift): parts[] {id,kind,pos,rot,
// labels,levels,extra}, sensors[], wires[] {id,a,b,color}, gpio[]
// {bcm,level,mode}, serial{lines,dropped}, probes[], powered, phase.
// Power pins use the PHYSICAL header number: "power:1" = 3V3.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

public class PartProxy
{
    public string id, kind;
    public GameObject root;
    public readonly List<PinTarget> pins = new();
    public readonly List<PinBadge> badges = new();
    public WorldLabel extraLabel;        // live one-liner ("ON 100%")
    public PartGlow glow;               // LED glow (null otherwise)
    public TwinBridge.PartView last;
}

public class TwinMirror : MonoBehaviour
{
    public ITwinLink Link { get; private set; }
    public TwinBridge Bridge;
    public TwinMockSource Mock;
    public Transform Desk;                  // board + parts root

    public bool Powered { get; private set; }
    public string Phase = "";
    public bool SourceIsBridge { get; private set; }
    public bool Live => SourceIsBridge && Bridge != null && Bridge.Connected;

    public readonly Dictionary<string, PartProxy> Parts = new();
    public readonly Dictionary<string, WireRenderer> Wires = new();
    public readonly List<PinTarget> AllTargets = new();
    public readonly Dictionary<int, bool?> GpioLine = new();
    public List<string> SerialLines = new();
    public JArray LatestProbes;
    public string BridgeUrl = "";

    public PinCategory Filter = PinCategory.All;

    public event Action OnSnapshotApplied;
    public event Action<bool> OnPowerChanged;

    GameObject boardRoot;
    Transform badgeRoot;   // world-scale parent for all pin badges
    readonly List<PinBadge> boardBadges = new();

    /// Every badge on the desk (board + parts), for gaze fades.
    public System.Collections.Generic.IEnumerable<PinBadge> AllBadges
    {
        get
        {
            foreach (var b in boardBadges) yield return b;
            foreach (var p in Parts.Values)
                foreach (var b in p.badges) yield return b;
        }
    }

    // ── lifecycle ────────────────────────────────────────────────
    void Awake()
    {
        if (Desk == null)
        {
            Desk = new GameObject("Desk").transform;
            Desk.SetParent(transform, false);
            Desk.localPosition = new Vector3(0, 0.72f, 0.72f);
        }
        if (badgeRoot == null)
        {
            badgeRoot = new GameObject("Badges").transform;
            badgeRoot.SetParent(Desk, false); // rides with the desk
            badgeRoot.localPosition = Vector3.zero;
        }
        BuildBoard();
    }

    void Start()
    {
        if (Bridge == null) Bridge = FindObjectOfType<TwinBridge>();
        if (Mock == null) Mock = GetComponent<TwinMockSource>();
        if (Mock != null) Mock.OnSnapshot += Apply;
        Link = Mock; // mock until the real socket comes up
    }

    float bridgeDownSince = -1f;

    void Update()
    {
        // Auto-switch to the real twin once the socket is alive…
        if (!SourceIsBridge && Bridge != null && Bridge.Connected)
        {
            if (Mock != null) Mock.OnSnapshot -= Apply;
            ClearDesk();
            SourceIsBridge = true;
            Link = Bridge;
            Bridge.Hello();
            BridgeUrl = Bridge.url;
            bridgeDownSince = -1f;
        }
        // …and fall back to the mock when it drops for 6 s, so the
        // bench never sits empty on a dead link (the banner shows
        // "paused link" meanwhile).
        else if (SourceIsBridge && Bridge != null && !Bridge.Connected)
        {
            if (bridgeDownSince < 0f) bridgeDownSince = Time.time;
            else if (Time.time - bridgeDownSince > 6f)
            {
                SourceIsBridge = false;
                Link = Mock;
                if (Mock != null) Mock.OnSnapshot += Apply;
                ClearDesk();
                bridgeDownSince = -1f;
            }
        }
    }

    // ── board ────────────────────────────────────────────────────
    void BuildBoard()
    {
        boardRoot = PartModels.Pi4B(Desk);
        // header toward the user (pin 1 at front-left); sit the PCB
        // on the desk plane (its box is centred, so lift by half)
        boardRoot.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        boardRoot.transform.localPosition =
            new Vector3(0f, 0.0009f, 0f);

        foreach (var p in BoardPins.All)
        {
            var anchor = boardRoot.transform.Find("pin" + p.physical);
            if (anchor == null) continue;
            var target = anchor.gameObject.AddComponent<PinTarget>();
            target.label = p.name;
            target.subtitle = p.alt != "" ? p.alt :
                (p.kind == PinKind.Gpio ? $"BCM GPIO{p.bcm}" :
                 p.kind == PinKind.V3 ? "3.3 V power rail" :
                 p.kind == PinKind.V5 ? "5 V power rail" : "Ground");
            if (p.kind == PinKind.Gpio)
            {
                target.node = $"gpio:{p.bcm}";
                target.category = p.alt.Contains("I2C")
                    ? PinCategory.I2C : PinCategory.Gpio;
            }
            else
            {
                target.node = $"power:{p.physical}";
                target.category = p.kind == PinKind.Gnd
                    ? PinCategory.Ground : PinCategory.Power;
            }
            AllTargets.Add(target);

            // pin collider for gaze/pinch (pin posts are mm-scaled)
            var col = anchor.gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(5f, 10f, 5f);   // mm, local units
            col.center = new Vector3(0, 5f, 0);

            // badge above the pin (world-scale root, see MakeBadge)
            var badge = MakeBadge(target,
                anchor.position + Vector3.up * 0.012f, 0.5f);
            badge.follow = anchor;
            badge.followOffset = Vector3.up * 0.012f;
            boardBadges.Add(badge);
        }
    }

    /// Badges are NOT parented under their pin anchors — pins are
    /// mm-scaled primitives and would crush the badge canvas scale
    /// to invisible. Instead they hang on a world-scale root at the
    /// pin's world position, billboarded toward the viewer.
    PinBadge MakeBadge(PinTarget target, Vector3 worldPos, float scale)
    {
        var go = new GameObject("badge_" + target.label);
        go.transform.SetParent(badgeRoot, true); // keep world position
        var badge = go.AddComponent<PinBadge>();
        badge.scale = scale;
        badge.Init(target);
        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(
            BenchTheme.BadgeW * BenchTheme.Px / 0.5f * scale,
            BenchTheme.BadgeH * BenchTheme.Px / 0.5f * scale, 0.004f);
        var bt = go.AddComponent<PinBadgeTarget>();
        bt.target = target;
        return badge;
    }

    // ── snapshot application ─────────────────────────────────────
    public void Apply(JObject snap)
    {
        Powered = snap.Value<bool?>("powered") ?? false;
        Phase = snap.Value<string>("phase") ?? "";

        ApplyParts((JArray)snap["parts"]);
        ApplyWires((JArray)snap["wires"]);
        ApplyGpio((JArray)snap["gpio"]);
        ApplyProbes((JArray)snap["probes"]);

        var serial = snap["serial"];
        if (serial != null && serial["lines"] != null)
        {
            SerialLines = new List<string>();
            foreach (var l in (JArray)serial["lines"])
                SerialLines.Add((string)l);
        }

        OnPowerChanged?.Invoke(Powered);
        OnSnapshotApplied?.Invoke();
    }

    void ApplyParts(JArray parts)
    {
        var seen = new HashSet<string>();
        if (parts != null)
            foreach (var p in parts)
            {
                var v = TwinBridge.ParsePart(p);
                seen.Add(v.id);
                if (!Parts.TryGetValue(v.id, out var proxy))
                    proxy = SpawnPart(v);
                else
                    MovePart(proxy, v);
                UpdatePart(proxy, v);
            }
        var gone = new List<string>();
        foreach (var kv in Parts) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (var id in gone) RemovePartLocal(id);
    }

    PartProxy SpawnPart(TwinBridge.PartView v)
    {
        var proxy = new PartProxy { id = v.id, kind = v.kind };
        var root = PartModels.ForKind(v.kind, Desk);
        root.name = $"part_{v.kind}_{SafeHead(v.id)}";
        proxy.root = root;
        Parts[v.id] = proxy;
        MovePart(proxy, v); // position BEFORE badges read pin positions

        var labels = v.labels ?? new string[0];
        for (int i = 0; i < labels.Length; i++)
        {
            var anchor = FindPinAnchor(root, i);
            if (anchor == null) continue;
            var t = anchor.gameObject.AddComponent<PinTarget>();
            t.node = $"comp:{v.id}:{i}";
            t.label = labels[i];
            t.subtitle = labels[i];
            t.category = Classify(labels[i]);
            AllTargets.Add(t);
            var col = anchor.gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(8f, 14f, 8f);   // mm local
            col.center = new Vector3(0, 6f, 0);

            var badge = MakeBadge(t,
                anchor.position + Vector3.up * 0.042f, 0.42f);
            badge.follow = anchor; // keep riding the pin on drags
            badge.followOffset = Vector3.up * 0.042f;
            proxy.pins.Add(t);
            proxy.badges.Add(badge);
        }

        var glow = root.transform.Find("Glow");
        if (glow != null) proxy.glow = glow.GetComponent<PartGlow>();
        var labelGo = new GameObject("ExtraLabel");
        labelGo.transform.SetParent(root.transform, false);
        labelGo.transform.localPosition = new Vector3(0, 26f, 0); // mm
        proxy.extraLabel = labelGo.AddComponent<WorldLabel>();

        root.AddComponent<TwinPartHandle>().partId = v.id;
        var grab = root.AddComponent<PartTarget>();
        grab.partId = v.id;
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0, 0.01f, 0);
        box.size = new Vector3(0.03f, 0.02f, 0.03f);
        return proxy;
    }

    static string SafeHead(string s) => s != null && s.Length > 8 ? s[..8] : s;

    static Transform FindPinAnchor(GameObject root, int idx)
    {
        var direct = root.transform.Find("p" + idx);
        if (direct != null) return direct;
        if (idx == 0) { var a = root.transform.Find("LegA"); if (a != null) return a; }
        if (idx == 1) { var c = root.transform.Find("LegC"); if (c != null) return c; }
        return null;
    }

    public static PinCategory Classify(string label)
    {
        var l = (label ?? "").ToLowerInvariant();
        if (l.Contains("gnd") || l.Contains("ground") || l.Contains("cathode"))
            return PinCategory.Ground;
        if (l.Contains("vcc") || l.Contains("vdd") || l.Contains("v3") ||
            l.Contains("v5") || l.Contains("5v") || l.Contains("3v") ||
            l.Contains("power") || l.Contains("anode"))
            return PinCategory.Power;
        if (l.Contains("scl") || l.Contains("sda"))
            return PinCategory.I2C;
        return PinCategory.Gpio;
    }

    void MovePart(PartProxy proxy, TwinBridge.PartView v)
    {
        // ParsePart already converted twin mm → Unity metres. No
        // cosmetic offsets here: TwinPartHandle round-trips the pose
        // on every snapshot, so any additive offset amplifies into
        // runaway drift (this once climbed at ~0.09 m/s).
        proxy.root.transform.localPosition = v.position;
        proxy.root.transform.localRotation =
            Quaternion.Euler(0, v.rotY * Mathf.Rad2Deg, 0);
    }

    void UpdatePart(PartProxy proxy, TwinBridge.PartView v)
    {
        proxy.last = v;
        for (int i = 0; i < proxy.pins.Count && i < v.levels.Length; i++)
            proxy.pins[i].free = !IsWired(proxy.pins[i].node);
        if (proxy.glow != null && v.levels.Length > 0 && v.levels[0].HasValue)
            proxy.glow.lit = v.levels[0].Value;
        if (proxy.extraLabel != null)
            proxy.extraLabel.SetText(v.extra ?? "");
        ApplyFilterTo(proxy);
    }

    void RemovePartLocal(string id)
    {
        if (!Parts.TryGetValue(id, out var p)) return;
        Destroy(p.root);
        Parts.Remove(id);
    }

    void ApplyWires(JArray wires)
    {
        var seen = new HashSet<string>();
        if (wires != null)
            foreach (var w in wires)
            {
                var id = w.Value<string>("id");
                var a = w.Value<string>("a");
                var b = w.Value<string>("b");
                seen.Add(id);
                if (!Wires.TryGetValue(id, out _))
                    SpawnWire(id, a, b, w["color"]);
                SetFree(a, false); SetFree(b, false);
            }
        var gone = new List<string>();
        foreach (var kv in Wires) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
        foreach (var id in gone) RemoveWireLocal(id);
    }

    void SpawnWire(string id, string a, string b, JToken colorTok)
    {
        var pa = FindTarget(a); var pb = FindTarget(b);
        if (pa == null || pb == null) return;
        var c = BenchTheme.Cyan;
        if (colorTok is JArray arr && arr.Count >= 3)
            c = new Color32((byte)(int)arr[0], (byte)(int)arr[1],
                            (byte)(int)arr[2], 255);
        var go = new GameObject("wire");
        go.transform.SetParent(Desk, false);
        var wire = go.AddComponent<WireRenderer>();
        wire.wireId = id; wire.nodeA = a; wire.nodeB = b;
        wire.Init(pa.transform.position, pb.transform.position, c);
        Wires[id] = wire;
    }

    void RemoveWireLocal(string id)
    {
        if (!Wires.TryGetValue(id, out var w)) return;
        var a = w.nodeA; var b = w.nodeB;
        Destroy(w.gameObject);
        Wires.Remove(id);
        SetFree(a, true); SetFree(b, true);
    }

    public PinTarget FindTarget(string node)
    {
        foreach (var t in AllTargets) if (t.node == node) return t;
        return null;
    }

    void SetFree(string node, bool free)
    {
        foreach (var t in AllTargets) if (t.node == node) t.free = free;
    }

    bool IsWired(string node)
    {
        foreach (var w in Wires.Values)
            if (w.nodeA == node || w.nodeB == node) return true;
        return false;
    }

    void ApplyGpio(JArray gpio)
    {
        if (gpio == null) return;
        GpioLine.Clear();
        foreach (var g in gpio)
        {
            var bcm = g.Value<int>("bcm");
            var lvl = g["level"];
            GpioLine[bcm] = lvl.Type == JTokenType.Null
                ? (bool?)null : lvl.Value<bool>();
        }
    }

    void ApplyProbes(JArray probes) => LatestProbes = probes;

    // ── filter pill ──────────────────────────────────────────────
    public void SetFilter(PinCategory c)
    {
        Filter = c;
        foreach (var b in boardBadges)
            if (b.target != null) b.SetDim(!b.target.Matches(Filter));
        foreach (var p in Parts.Values) ApplyFilterTo(p);
    }

    void ApplyFilterTo(PartProxy p)
    {
        foreach (var b in p.badges)
            if (b.target != null) b.SetDim(!b.target.Matches(Filter));
    }

    // ── commands (route over whichever link is active) ───────────
    /// Push a part's desk-local pose (drag handler calls this).
    public void MovePart(string id, Vector3 worldPos, float rotYUnity)
    {
        if (Desk != null)
            Link?.MovePart(id, Desk.InverseTransformPoint(worldPos), rotYUnity);
    }

    public void AddPart(string kind) => Link?.AddPart(kind);
    public void RemovePart(string id) => Link?.RemovePart(id);
    public void AddWire(string a, string b) => Link?.AddWire(a, b);
    public void RemoveWire(string id) => Link?.RemoveWire(id);
    public void PressButton(string id, bool held) => Link?.PressButton(id, held);
    public void SetGpio(int bcm, bool level) => Link?.SetGpio(bcm, level);
    public void SerialSend(string text) => Link?.SerialSend(text);
    public void SetProbes(int[] pins, float window = 2f) =>
        Link?.SetProbes(pins, window);
    public void Power(bool on) => Link?.Power(on);
    public void Restart() => Link?.Restart();

    void ClearDesk()
    {
        foreach (var p in Parts.Values) if (p.root != null) Destroy(p.root);
        Parts.Clear();
        foreach (var w in Wires.Values) if (w != null) Destroy(w.gameObject);
        Wires.Clear();
        // drop pin targets whose part proxies just died
        AllTargets.RemoveAll(t => t == null);
    }
}

/// Lets a badge's collider pinch-select the same node as its pin.
public class PinBadgeTarget : MonoBehaviour
{
    public PinTarget target;
}

/// Gaze/pinch target on a spawned part (drag to move).
public class PartTarget : MonoBehaviour
{
    public string partId;
}

/// Tiny world-space TextMeshPro label for live part one-liners.
public class WorldLabel : MonoBehaviour
{
    TextMeshPro tmp;

    void Awake()
    {
        tmp = gameObject.AddComponent<TextMeshPro>();
        tmp.fontSize = 14f;
        tmp.font = BenchUI.FontBold ?? BenchUI.Font;
        tmp.color = BenchTheme.CyanLight;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        transform.localScale = Vector3.one * 0.00075f;
        tmp.text = "";
    }

    public void SetText(string s) { if (tmp != null) tmp.text = s; }
}
