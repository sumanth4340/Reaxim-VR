// GazePinchInput.cs — look-and-pinch input for the MR bench.
// "Look to aim · pinch to open": the head ray aims (hover), a pinch
// (index+thumb, or a mouse click in the Editor) selects, and a pinch
// HELD on a part drags it around the desk. One module drives both
// uGUI widgets (EventSystem pointer events → BenchButton hover/press)
// and 3D targets (pins, wires, parts) via raycasts, so every screen
// behaves identically in the Editor and in the headset.
//
// Design rule from the project: gaze+pinch first; controller trigger
// and Editor mouse are fallbacks only. A pinch-release grace period
// keeps brief tracking losses from dropping drags.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class GazePinchInputModule : BaseInputModule
{
    public static GazePinchInputModule Instance { get; private set; }

    [Header("Aim")]
    [Tooltip("Head gaze distance for the UI ray (metres).")]
    public float aimDistance = 1.6f;

    [Header("Pinch (hand)")]
    public float pinchOn = 0.7f;      // strength to start a pinch
    public float pinchOff = 0.35f;    // strength to end it (hysteresis)
    public float graceSeconds = 0.25f;

    [Header("Fallback input")]
    [Tooltip("Mouse = gaze + click in the Editor / desktop fallback.")]
    public bool mouseFallback = true;
    [Tooltip("Controller trigger / A button as pinch fallback.")]
    public bool controllerFallback = true;

    // ── public state for screens ─────────────────────────────────
    public Ray GazeRay { get; private set; }
    public Vector3 GazePoint { get; private set; }
    public bool Pinching { get; private set; }
    public GameObject HoveredUi { get; private set; }
    public Collider Hovered3d { get; private set; }

    public event Action<PinTarget> OnPinPinched;
    public event Action<WireGrab> OnWirePinched;
    public event Action<PartTarget> OnPartDragStart;
    public event Action<PartTarget> OnPartDragEnd;

    readonly List<RaycastResult> raycastCache = new();
    PointerEventData pointer;
    GameObject pointerEnter, pressObject;
    float pinchStartedAt, pinchHeldAt;
    bool wasPinching;
    Camera eye;
    OVRHand[] hands;
    PartTarget dragging;
    static readonly Plane DeskPlane = new(Vector3.up, 0.726f);

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
    }

    void Update()
    {
        if (hands == null || hands.Length == 0)
            hands = FindObjectsOfType<OVRHand>();
    }

    // ── aim + pinch sources ───────────────────────────────────────
    Camera Eye
    {
        get
        {
            if (eye == null)
            {
                var anchor = GameObject.Find("CenterEyeAnchor");
                eye = anchor != null ? anchor.GetComponent<Camera>() : Camera.main;
            }
            return eye;
        }
    }

    static bool XRActive() => UnityEngine.XR.XRSettings.enabled;
    static bool MouseAim => Input.mousePresent && !XRActive();

    Ray AimRay()
    {
        var cam = Eye;
        if (cam == null) return new Ray(Vector3.zero, Vector3.forward);
        if (MouseAim) return cam.ScreenPointToRay(Input.mousePosition);
        return new Ray(cam.transform.position, cam.transform.forward);
    }

    bool ReadPinch()
    {
        if (hands != null)
            foreach (var h in hands)
            {
                if (h == null) continue;
                var s = h.GetFingerPinchStrength(OVRHand.HandFinger.Index);
                if (s >= pinchOn) return true;
                if (s >= pinchOff) return true; // hysteresis band keeps it
            }
        if (controllerFallback &&
            (OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger) ||
             OVRInput.Get(OVRInput.Button.One)))
            return true;
        if (mouseFallback && !XRActive() && Input.GetMouseButton(0))
            return true;
        return false;
    }

    // ── the frame ────────────────────────────────────────────────
    public override void Process()
    {
        if (pointer == null)
            pointer = new PointerEventData(eventSystem)
            { button = PointerEventData.InputButton.Left };

        GazeRay = AimRay();
        GazePoint = GazeRay.GetPoint(aimDistance * 0.72f);

        bool raw = ReadPinch();
        if (!Pinching && raw)
        {
            Pinching = true;
            pinchStartedAt = pinchHeldAt = Time.time;
        }
        else if (Pinching && raw)
        {
            pinchHeldAt = Time.time;
        }
        else if (Pinching && !raw &&
                 Time.time - pinchHeldAt > graceSeconds)
        {
            Pinching = false;
            if (dragging != null)
            {
                OnPartDragEnd?.Invoke(dragging);
                dragging = null;
            }
        }

        ProcessUi();
        Process3d();
        if (Pinching) HandleDrag();
    }

    // ── uGUI pointer pipeline ─────────────────────────────────────
    void ProcessUi()
    {
        var cam = Eye;
        if (cam == null) return;
        // screen position the raycaster should test: the mouse
        // directly, or the gaze point projected onto the UI plane
        Vector3 aimPos = GazeRay.origin +
                         GazeRay.direction * aimDistance;
        pointer.position = MouseAim
            ? (Vector2)Input.mousePosition
            : (Vector2)cam.WorldToScreenPoint(aimPos);
        pointer.delta = Vector2.zero;

        raycastCache.Clear();
        eventSystem.RaycastAll(pointer, raycastCache);
        RaycastResult? first = null;
        foreach (var r in raycastCache) { first = r; break; }

        // hover enter/exit
        var newEnter = first?.gameObject;
        if (newEnter != pointerEnter)
        {
            if (pointerEnter != null)
                ExecuteEvents.Execute(pointerEnter, pointer,
                    ExecuteEvents.pointerExitHandler);
            pointerEnter = newEnter;
            HoveredUi = newEnter;
            if (pointerEnter != null)
                ExecuteEvents.Execute(pointerEnter, pointer,
                    ExecuteEvents.pointerEnterHandler);
        }

        // press / release / click
        if (Pinching && !wasPinching)
        {
            pointer.pressPosition = pointer.position;
            pointer.pointerPressRaycast = first ?? default;
            pressObject = pointerEnter;
            if (pressObject != null)
                ExecuteEvents.Execute(pressObject, pointer,
                    ExecuteEvents.pointerDownHandler);
        }
        else if (!Pinching && wasPinching)
        {
            if (pressObject != null)
            {
                ExecuteEvents.Execute(pressObject, pointer,
                    ExecuteEvents.pointerUpHandler);
                if (pressObject == pointerEnter &&
                    Time.time - pinchStartedAt < 0.55f)
                    ExecuteEvents.Execute(pressObject, pointer,
                        ExecuteEvents.pointerClickHandler);
            }
            pressObject = null;
        }
        wasPinching = Pinching;
    }

    // ── 3D targets (pins / wires / parts) ─────────────────────────
    void Process3d()
    {
        var hits = Physics.RaycastAll(GazeRay, aimDistance, ~0,
                                       QueryTriggerInteraction.Collide);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        Collider newHover = null;
        foreach (var h in hits)
        {
            if (h.collider.GetComponent<PinTarget>() ||
                h.collider.GetComponent<PinBadgeTarget>() ||
                h.collider.GetComponent<WireGrab>() ||
                h.collider.GetComponent<PartTarget>())
            { newHover = h.collider; break; }
        }
        Hovered3d = newHover;

        if (Pinching && !wasPinching)
            PinchDown3d(newHover);
    }

    void PinchDown3d(Collider hit)
    {
        if (hit == null) return;
        var pin = hit.GetComponent<PinTarget>() ??
                  hit.GetComponent<PinBadgeTarget>()?.target;
        if (pin != null) { OnPinPinched?.Invoke(pin); return; }

        var wire = hit.GetComponentInParent<WireGrab>();
        if (wire != null) { OnWirePinched?.Invoke(wire); return; }

        var part = hit.GetComponentInParent<PartTarget>();
        if (part != null)
        {
            OnPartDragStart?.Invoke(part);
            dragging = part;
        }
    }

    // ── part dragging ────────────────────────────────────────────
    void HandleDrag()
    {
        if (dragging == null) return;
        if (GazeRay.direction.y > -0.05f) return; // aiming level/up: hold
        if (DeskPlane.Raycast(GazeRay, out float enter))
            dragging.transform.position = GazeRay.GetPoint(enter);
    }
}
