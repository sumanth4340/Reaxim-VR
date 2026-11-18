// UiClick.cs — pinch/click handler for bench widgets. The
// GazePinchInputModule (and any EventSystem input) dispatches
// pointer-click; widgets attach this and expose a callback.

using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class UiClick : MonoBehaviour, IPointerClickHandler
{
    public Action on;

    public void OnPointerClick(PointerEventData e) => on?.Invoke();
}
