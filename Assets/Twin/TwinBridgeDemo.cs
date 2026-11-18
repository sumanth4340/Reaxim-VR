// TwinBridgeDemo.cs — zero-UI sanity check for the twin link.
// Attach next to TwinBridge, press Play: snapshots log to Console,
// Space toggles GPIO 18 (wire an LED to GPIO 18 and GND first).

using Newtonsoft.Json.Linq;
using UnityEngine;

public class TwinBridgeDemo : MonoBehaviour
{
    private TwinBridge _bridge;
    private bool _on;

    private void Awake() => _bridge = GetComponent<TwinBridge>();

    private void OnEnable()
    {
        _bridge.OnSnapshot += Log;
        _bridge.OnAck += LogAck;
    }

    private void OnDisable()
    {
        _bridge.OnSnapshot -= Log;
        _bridge.OnAck -= LogAck;
    }

    private float _lastLog;

    private void Log(JObject snap)
    {
        // Throttled: unthrottled 15 Hz logging floods the editor log.
        if (Time.unscaledTime - _lastLog < 1f) return;
        _lastLog = Time.unscaledTime;
        var parts = (JArray)snap["parts"];
        var wires = (JArray)snap["wires"];
        Debug.Log($"[Twin] phase={snap["phase"]} parts={parts.Count} " +
                  $"wires={wires.Count} powered={snap["powered"]}");
    }

    private void LogAck(JObject ack)
    {
        Debug.Log($"[TwinAck] cmd={ack["cmd"]} ok={ack["ok"]}");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            _on = !_on;
            _bridge.SetGpio(18, _on);
            Debug.Log($"[Twin] GPIO 18 -> {(_on ? "HIGH" : "LOW")}");
        }
    }
}
