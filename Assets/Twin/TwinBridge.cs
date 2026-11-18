// TwinBridge.cs — drop-in Unity client for the rpi-hvf VR bridge.
// Unity 6 + XR Simulator, Newtonsoft JSON package
// (com.unity.nuget.newtonsoft-json, included with Unity by default).
//
// Setup: add this component to any GameObject in your scene, press Play
// with the bridge started from the twin's VR view. See UNITY_SETUP.md.
//
// Threading: the socket lives on a background thread; received messages
// are queued and dispatched from Update() so handlers may touch Unity API.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class TwinBridge : MonoBehaviour, ITwinLink
{
    [Tooltip("Twin bridge URL. XR Simulator (same Mac): 127.0.0.1. " +
             "Physical Quest: ws://<mac-lan-ip>:17890/ with the server " +
             "bound to 0.0.0.0 (see Bridge README §6).")]
    public string url = "ws://127.0.0.1:17890/";

    public int protocolVersion = 1;

    /// Fired on the Unity thread for every snapshot (~15 Hz).
    public event Action<JObject> OnSnapshot;
    /// Fired on the Unity thread for every command ack.
    public event Action<JObject> OnAck;
    /// Fired on the Unity thread for every display frame (~5 Hz).
    public event Action<DisplayFrame> OnDisplay;

    public bool Connected => _ws != null && _ws.State == WebSocketState.Open;

    /// Twin host-uptime clock tracking (§8, time sync). Updated from every
    /// snapshot/ack `t`; ServerNow extrapolates with the local clock.
    public double LastServerTime { get; private set; } = double.NaN;
    public double LastServerTimeLocalRecv { get; private set; } = double.NaN;
    public double ServerNow => double.IsNaN(LastServerTime) ? double.NaN
        : LastServerTime + (Time.realtimeSinceStartupAsDouble - LastServerTimeLocalRecv);

    /// Clock offset in seconds to ADD to the client clock so it reads as
    /// the server clock: offset = tServer + rtt/2 - tClientRecv.
    /// Pure math — EditMode-testable. See Bridge README §8.
    public static double EstimateClockOffset(double serverT, double clientSent, double clientRecv) =>
        serverT + (clientRecv - clientSent) * 0.5 - clientRecv;

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private readonly ConcurrentQueue<JObject> _incoming = new ConcurrentQueue<JObject>();
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
    private int _gen;  // reconnect generation: stale loops must not resurrect
    private float _lastFailLog = -100f;

    // ── Lifecycle ────────────────────────────────────────────────

    private async void Start()
    {
        // Remembered address wins (loopback for Simulator, LAN IP for
        // hardware); the inspector value is only the first default.
        try
        {
            string last = TwinSettings.LastGoodUrl;
            if (!string.IsNullOrEmpty(last)) url = last;
        }
        catch { /* keep inspector default */ }
        _cts = new CancellationTokenSource();
        await ConnectWithRetry(_cts.Token);
    }

    private void OnDestroy()
    {
        _gen++;
        try { _cts?.Cancel(); } catch { /* shutting down */ }
        try { _ws?.Abort(); } catch { /* shutting down */ }
        try { _ws?.Dispose(); } catch { /* shutting down */ }
    }

    /// Drop the current socket (if any) and reconnect to `url` from
    /// scratch. Safe to call when already connected or never connected.
    public void Reconnect()
    {
        _gen++;
        try { _cts?.Cancel(); } catch { /* already gone */ }
        try { _ws?.Abort(); } catch { /* already gone */ }
        try { _ws?.Dispose(); } catch { /* already gone */ }
        _cts = new CancellationTokenSource();
        _ = ConnectWithRetry(_cts.Token);
    }

    private async Task ConnectWithRetry(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(url), token);
                Debug.Log($"[TwinBridge] connected to {url}");
                TwinSettings.LastGoodUrl = url;
                _ = ReceiveLoop(token, _gen);  // runs until cancelled/error
                return;
            }
            catch (OperationCanceledException) { /* superseded/shutdown */ }
            catch (Exception e)
            {
                // Throttled: unthrottled 2 s warnings flood the log
                // in Demo Mode / with no hardware attached.
                if (Time.realtimeSinceStartup - _lastFailLog > 30f)
                {
                    _lastFailLog = Time.realtimeSinceStartup;
                    Debug.LogWarning($"[TwinBridge] connect failed ({e.Message}), " +
                                     "retrying in 2s — is the bridge started in the twin VR view?");
                }
                try { await Task.Delay(2000, token); }
                catch (OperationCanceledException) { /* superseded */ }
            }
        }
    }

    /// Reconnect one generation later unless superseded. Fired when a
    /// live session drops (server close, error) — previously the link
    /// stayed dead while the overlay claimed to be retrying.
    private async Task AutoReconnect(int gen)
    {
        try { await Task.Delay(2000); } catch { /* shutting down */ }
        if (gen == _gen && !Connected) Reconnect();
    }

    private async Task ReceiveLoop(CancellationToken token, int gen)
    {
        var buffer = new byte[65536];
        var sb = new StringBuilder();
        try
        {
            while (!token.IsCancellationRequested && Connected)
            {
                sb.Clear();
                WebSocketReceiveResult r;
                do
                {
                    r = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.LogWarning("[TwinBridge] server closed the connection");
                        _ = AutoReconnect(gen);
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                } while (!r.EndOfMessage);
                try
                {
                    _incoming.Enqueue(JObject.Parse(sb.ToString()));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TwinBridge] ignoring bad JSON: {e.Message}");
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception e)
        {
            if (!token.IsCancellationRequested)
            {
                Debug.LogWarning($"[TwinBridge] receive loop ended: {e.Message}");
                _ = AutoReconnect(gen);
            }
        }
    }

    private void Update()
    {
        while (_incoming.TryDequeue(out var msg))
        {
            var stamp = msg.Value<double?>("t");
            if (stamp.HasValue)
            {
                LastServerTime = stamp.Value;
                LastServerTimeLocalRecv = Time.realtimeSinceStartupAsDouble;
            }
            var type = msg.Value<string>("type");
            if (type == "welcome")
            {
                var v = msg.Value<int>("v");
                if (v != protocolVersion)
                    Debug.LogError($"[TwinBridge] protocol mismatch: server v{v}, " +
                                   $"client v{protocolVersion}");
                else
                    Debug.Log("[TwinBridge] welcome, protocol v1");
            }
            else if (type == "snapshot")
            {
                OnSnapshot?.Invoke(msg);
            }
            else if (type == "display")
            {
                try
                {
                    OnDisplay?.Invoke(ParseDisplay(msg));
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        "[TwinBridge] ignoring bad display frame: "
                        + e.Message);
                }
            }
            else if (type == "ack")
            {
                if (msg.Value<bool>("ok") != true)
                    Debug.LogWarning($"[TwinBridge] ack failed: {msg}");
                OnAck?.Invoke(msg);
            }
        }
    }

    // ── Send ─────────────────────────────────────────────────────

    /// Thread-safe: one in-flight SendAsync at a time.
    public async void Send(JObject cmd)
    {
        if (!Connected)
        {
            Debug.LogWarning("[TwinBridge] not connected, dropping command");
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(cmd.ToString(Newtonsoft.Json.Formatting.None));
        await _sendLock.WaitAsync();
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text, endOfMessage: true,
                CancellationToken.None);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TwinBridge] send failed: {e.Message}");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ── Commands (mirror Bridge README §3) ───────────────────────

    public void Hello() => Send(new JObject { ["cmd"] = "hello" });

    public void MovePart(string id, Vector3 unityMeters, float rotYUnity = 0f)
    {
        var mm = UnityToTwin(unityMeters);
        Send(new JObject
        {
            ["cmd"] = "movePart",
            ["id"] = id,
            ["pos"] = new JArray(mm.x, mm.y, mm.z),
            ["rot"] = -rotYUnity,
        });
    }

    public void SetGpio(int bcm, bool level) => Send(new JObject
    {
        ["cmd"] = "setGpio",
        ["bcm"] = bcm,
        ["level"] = level,
    });

    public void PressButton(string id, bool held) => Send(new JObject
    {
        ["cmd"] = "pressButton",
        ["id"] = id,
        ["held"] = held,
    });

    public void AddPart(string kind) => Send(new JObject
    {
        ["cmd"] = "addPart",
        ["kind"] = kind,  // LED|Button|Servo|BME280|MCP3008|GPS
    });

    public void RemovePart(string id) => Send(new JObject
    {
        ["cmd"] = "removePart",
        ["id"] = id,
    });

    public void AddWire(string a, string b) => Send(new JObject
    {
        ["cmd"] = "addWire",
        ["a"] = a,  // node strings, e.g. "gpio:18", "comp:<uuid>:0"
        ["b"] = b,
    });

    public void RemoveWire(string id) => Send(new JObject
    {
        ["cmd"] = "removeWire",
        ["id"] = id,
    });

    public void SerialSend(string text) => Send(new JObject
    {
        ["cmd"] = "serialSend",
        ["text"] = text,  // newline appended server-side when missing
    });

    public void SetProbes(int[] pins, float windowSeconds = 2f) => Send(new JObject
    {
        ["cmd"] = "setProbes",
        ["pins"] = new JArray(pins),
        ["window"] = windowSeconds,
    });

    public void Power(bool on) => Send(new JObject
    {
        ["cmd"] = "power",
        ["on"] = on,
    });

    public void SetDisplay(bool on, int maxWidth = 960) => Send(new JObject
    {
        ["cmd"] = "setDisplay",
        ["on"] = on,
        ["maxWidth"] = maxWidth,
    });

    public void DisplayInput(DisplayInputState input)
    {
        var cmd = new JObject { ["cmd"] = "displayInput" };
        if (input.x.HasValue && input.y.HasValue)
        {
            cmd["x"] = input.x.Value;
            cmd["y"] = input.y.Value;
        }
        if (input.button.HasValue)
        {
            cmd["button"] = input.button.Value;
            cmd["pressed"] = input.pressed;
        }
        if (input.scroll.HasValue) cmd["scroll"] = input.scroll.Value;
        if (input.key.HasValue)
        {
            cmd["key"] = input.key.Value;
            cmd["keyPressed"] = input.keyPressed;
        }
        Send(cmd);
    }

    public void Restart() => Send(new JObject
    {
        ["cmd"] = "restart",
    });

    // ── Coordinates (Bridge README §5) ───────────────────────────
    // Twin: millimetres, Y-up, right-handed. Unity: metres, Y-up,
    // left-handed → scale + flip Z, negate Y rotation.

    public static Vector3 TwinToUnity(float xMm, float yMm, float zMm) =>
        new Vector3(xMm / 1000f, yMm / 1000f, -zMm / 1000f);

    public static Vector3 UnityToTwin(Vector3 unityMeters) =>
        new Vector3(unityMeters.x * 1000f, unityMeters.y * 1000f,
                    -unityMeters.z * 1000f);

    /// Convenience: parse one snapshot part entry.
    public struct PartView
    {
        public string id;
        public string kind;
        public Vector3 position;  // Unity metres
        public float rotY;        // Unity radians about Y
        public string[] labels;
        public bool?[] levels;    // null = floating
        public string extra;      // live one-liner ("ON 100%", "90°", …)
    }

    public static PartView ParsePart(JToken p)
    {
        var pos = p["pos"];
        var labels = p["labels"].ToObject<string[]>();
        var lv = (JArray)p["levels"];
        var levels = new bool?[lv.Count];
        for (int i = 0; i < lv.Count; i++)
            levels[i] = lv[i].Type == JTokenType.Null ? (bool?)null : lv[i].Value<bool>();
        return new PartView
        {
            id = p.Value<string>("id"),
            kind = p.Value<string>("kind"),
            position = TwinToUnity(pos[0].Value<float>(), pos[1].Value<float>(),
                                   pos[2].Value<float>()),
            rotY = -p.Value<float>("rot"),
            labels = labels,
            levels = levels,
            extra = p.Value<string>("extra"),
        };
    }

    /// Convenience: parse one display frame (see Bridge README §11).
    public struct DisplayFrame
    {
        public int w;
        public int h;
        public string source; // "guest" or "testcard"
        public byte[] jpg;
    }

    public static DisplayFrame ParseDisplay(JObject msg)
    {
        return new DisplayFrame
        {
            w = msg.Value<int>("w"),
            h = msg.Value<int>("h"),
            source = msg.Value<string>("source"),
            jpg = Convert.FromBase64String(
                msg.Value<string>("jpg")),
        };
    }

    /// Convenience: parse one snapshot probe entry (see Bridge README §1).
    /// Edge ages are seconds before the snapshot's `t` — plot `t - age`.
    public struct ProbeView
    {
        public int pin;
        public List<Vector2> edges;  // x = age seconds, y = 0/1 level
        public float? duty;          // null unless a steady train
        public float? freqHz;        // null unless ≥2 rising edges
        public float? dcVolts;       // null when the pin never toggled
    }

    public static ProbeView ParseProbe(JToken p)
    {
        var edges = new List<Vector2>();
        foreach (var e in (JArray)p["edges"])
            edges.Add(new Vector2(e[0].Value<float>(),
                                  e[1].Value<bool>() ? 1f : 0f));
        return new ProbeView
        {
            pin = p.Value<int>("pin"),
            edges = edges,
            duty = p["duty"].Type == JTokenType.Null
                ? (float?)null : p["duty"].Value<float>(),
            freqHz = p["freqHz"].Type == JTokenType.Null
                ? (float?)null : p["freqHz"].Value<float>(),
            dcVolts = p["dcVolts"].Type == JTokenType.Null
                ? (float?)null : p["dcVolts"].Value<float>(),
        };
    }
}
