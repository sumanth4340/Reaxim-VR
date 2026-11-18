// ITwinLink.cs — contract for the twin bridge client.
// Implemented by TwinBridge; lets twin-view code depend on the link
// abstraction instead of the concrete MonoBehaviour.

using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

public interface ITwinLink
{
    event Action<JObject> OnSnapshot;
    event Action<JObject> OnAck;

    bool Connected { get; }
    double LastServerTime { get; }
    double LastServerTimeLocalRecv { get; }
    double ServerNow { get; }

    void Send(JObject cmd);
    void Hello();
    void MovePart(string id, Vector3 unityMeters, float rotYUnity = 0f);
    void SetGpio(int bcm, bool level);
    void PressButton(string id, bool held);
    void AddPart(string kind);
    void RemovePart(string id);
    void AddWire(string a, string b);
    void RemoveWire(string id);
    void SerialSend(string text);
    void SetProbes(int[] pins, float windowSeconds = 2f);
    void Power(bool on);
    void Restart();
    void SetDisplay(bool on, int maxWidth = 960);
    void DisplayInput(DisplayInputState input);

    event Action<TwinBridge.DisplayFrame> OnDisplay;
}

/// One guest-display input event. Only set fields are sent.
public struct DisplayInputState
{
    public float? x; // 0..1 (with y)
    public float? y; // 0..1 (with x)
    public int? button; // evdev: 0x110 left, 0x111 right, 0x112 middle
    public bool pressed;
    public int? scroll; // REL_WHEEL count
    public int? key; // linux input code
    public bool keyPressed;
}
