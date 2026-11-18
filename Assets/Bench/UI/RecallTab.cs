// RecallTab.cs — edge drag-slides on the workspace screen that
// pull the side windows back: PanelTab (left) recalls the project
// panel, DisplayTab (right) recalls the display. Tap toggles; an
// inward swipe past 60 px does the same (edge-drawer gesture).
// DisplayTab shows only while the display window is showing.
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Bench.Panels;

namespace Bench.UI
{
    public class RecallTab : MonoBehaviour, IBeginDragHandler,
        IDragHandler, IEndDragHandler, IPointerDownHandler
    {
        private bool _display;
        private float _dragX;
        private bool _moved;

        private void Awake()
        {
            _display = gameObject.name == "DisplayTab";
            var b = GetComponent<Button>();
            if (b == null) b = gameObject.AddComponent<Button>();
            b.onClick.AddListener(() =>
            {
                if (_moved) return; // a real drag handles itself
                SoundBank.Play(BenchSound.Press);
                Activate();
            });
        }

        public void OnPointerDown(PointerEventData e)
        {
            _moved = false;
        }

        public void OnBeginDrag(PointerEventData e) => _dragX = 0f;

        public void OnDrag(PointerEventData e)
        {
            _dragX += e.delta.x;
            if (Mathf.Abs(_dragX) > 20f) _moved = true;
        }

        public void OnEndDrag(PointerEventData e)
        {
            bool inward = _display ? _dragX < -60f : _dragX > 60f;
            _dragX = 0f;
            if (inward)
            {
                SoundBank.Play(BenchSound.Press);
                Activate();
            }
        }

        private void Activate()
        {
            if (_display) DisplayWindow.Instance?.ToggleWide();
            else ConceptPanelWiring.Instance?.ToggleRecall();
        }

        /// <summary>Harness: same swipe decision, no pointer needed.</summary>
        public void TestDrag(float totalDeltaX)
        {
            bool inward = _display
                ? totalDeltaX < -60f : totalDeltaX > 60f;
            if (inward) Activate();
        }
    }
}
