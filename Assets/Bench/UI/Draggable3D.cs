// Draggable3D.cs — Task 5 drag-out (and tap select/place).
// Drag a 3D card out of the panel: past 6 cm from the card the panel
// glides LEFT, the glass dissolves (via AppStateMachine), and the
// item scales up to real size at the pointer. Release over the panel
// returns it; release elsewhere places a real-size copy (kinematic
// until Task 6 physics). Tap selects a ghost preview; next tap off
// the panel places it. AddComponent order follows the safe pattern
// (renderable first). Test hooks mirror the pointer handlers.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Bench.Core;
using Bench.Panels;
using Bench.UI;

namespace Bench.UI
{
    public class Draggable3D : MonoBehaviour, IBeginDragHandler,
        IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        [Tooltip("Catalogue name of this part, e.g. LED red.")]
        public string partName;
        [Tooltip("Twin kind key, e.g. LED, BoardRPi4B.")]
        public string partType = "";
        [Tooltip("True for board cards (no breadboard snap, board menu).")]
        public bool isBoardPart;
        [Tooltip("World metres the pointer must travel to trigger drag-out.")]
        public float dragOutMetres = 0.06f;

        private bool _dragging;
        private bool _moved;
        private bool _transitioned;
        private bool _weTransitioned;
        private ScrollRect _scroll;
        private Vector3 _cardWorld;
        private GameObject _held;
        private Vector3 _heldRealScale = Vector3.one;
        private GameObject _ghost;
        private bool _selected;

        private static Material _ghostMat;

        public GameObject HeldItem => _held;
        public GameObject GhostItem => _ghost;
        public bool IsSelected => _selected;

        // ── pointer handlers ──────────────────────────────────────

        public void OnBeginDrag(PointerEventData e)
        {
            TestBegin(CardCentreWorld());
            TestDragTo(PointerWorld(e));
        }

        public void OnDrag(PointerEventData e) => TestDragTo(PointerWorld(e));

        public void OnEndDrag(PointerEventData e)
        {
            var panel = ConceptPanelWiring.Instance;
            bool overPanel = panel != null && e != null &&
                panel.PointerOverPanel(e.position, e.pressEventCamera);
            TestEnd(overPanel);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (_dragging || _moved) return;
            if (!_selected) TestSelect();
            else
            {
                var panel = ConceptPanelWiring.Instance;
                bool overUI = panel != null && e != null &&
                    panel.PointerOverPanel(e.position, e.pressEventCamera);
                TestPlaceTap(!overUI);
            }
        }

        // ── test hooks (same code paths as the handlers) ─────────

        public void TestBegin(Vector3 cardWorld)
        {
            _dragging = true;
            _moved = false;
            _transitioned = false;
            _weTransitioned = false;
            _cardWorld = cardWorld;
            _scroll = GetComponentInParent<ScrollRect>();
            if (_scroll != null) _scroll.enabled = false;
            EnsureHeld();
            if (_held != null) _held.transform.position = cardWorld;
        }

        public void TestDragTo(Vector3 world)
        {
            if (!_dragging || _held == null) return;
            _held.transform.position = world;
            if ((world - _cardWorld).magnitude > 0.005f) _moved = true;
            if (!_transitioned && (world - _cardWorld).magnitude >= dragOutMetres)
            {
                _transitioned = true;
                var machine = AppStateMachine.Instance;
                if (machine != null && machine.Current != AppState.Workspace)
                {
                    _weTransitioned = true;
                    SoundBank.PlayAt(BenchSound.Snap, transform.position);
                    machine.TransitionTo(AppState.Workspace);
                }
                ConceptPanelWiring.Instance?.FocusAway();
                var held = _held;
                var target = _heldRealScale;
                MiniTween.Vector3(this, held.transform.localScale, target,
                    0.32f, v => { if (held != null) held.transform.localScale = v; },
                    null, Ease.OutCubic);
            }
        }

        public void TestEnd(bool overPanel)
        {
            if (!_dragging) return;
            _dragging = false;
            _moved = false;
            if (_scroll != null)
            {
                _scroll.enabled = true;
                _scroll.velocity = Vector2.zero;
                _scroll = null;
            }
            var machine = AppStateMachine.Instance;
            if (_held != null)
            {
                if (overPanel)
                {
                    Destroy(_held);
                    _held = null;
                    if (_weTransitioned && machine != null)
                        machine.TransitionTo(AppState.ProjectPanel);
                    else
                        SoundBank.Play(BenchSound.Press);
                }
                else
                {
                    PlaceHeld(_held.transform.position, _held.transform.rotation);
                    Destroy(_held);
                    _held = null;
                    _weTransitioned = false;
                }
            }
            _weTransitioned = false;
        }

        public void TestSelect()
        {
            if (_selected) return;
            _selected = true;
            EnsureGhost();
        }

        public void TestPlaceTap(bool offPanel)
        {
            if (!_selected) return;
            if (!offPanel) return;
            Vector3 at = GhostPose();
            ClearGhost();
            _selected = false;
            PlaceHeld(at, Quaternion.identity);
        }

        public void TestDeselect()
        {
            ClearGhost();
            _selected = false;
        }

        // ── internals ─────────────────────────────────────────────

        public Vector3 CardCentreWorld()
        {
            var rt = transform as RectTransform;
            if (rt == null) return transform.position;
            return rt.TransformPoint(rt.rect.center);
        }

        private Vector3 PointerWorld(PointerEventData e)
        {
            if (e == null) return _cardWorld;
            var rt = transform as RectTransform;
            if (rt == null) return _cardWorld;
            Vector3 world;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                rt, e.position, e.pressEventCamera, out world))
                return world;
            return _cardWorld;
        }

        private Transform FindModelSource()
        {
            var spin = GetComponentInChildren<TurntableSpin>(true);
            if (spin == null) return null;
            var pivot = spin.transform.Find("Pivot");
            if (pivot == null) return null;
            if (pivot.childCount > 0) return pivot.GetChild(0);
            return null;
        }

        private void EnsureHeld()
        {
            if (_held != null) return;
            var src = FindModelSource();
            if (src == null)
            {
                SoundBank.Play(BenchSound.Error);
                Debug.LogWarning("[Bench] drag: 3D not loaded yet for " + partName);
                return;
            }
            _held = new GameObject("Held_" + partName);
            var copy = Instantiate(src.gameObject, _held.transform);
            copy.name = "Model";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            // Match the card's apparent size first; scale up on transition.
            _held.transform.position = _cardWorld;
            _held.transform.rotation = src.rotation;
            _held.transform.localScale = src.lossyScale * 0.6f;
            // Held item grows to TRUE physical size on transition
            // (card src is tile-fit, e.g. 6 cm — not real size).
            _heldRealScale = RealWorldScale(src);
            SoundBank.Play(BenchSound.Press);
        }

        public string PartType =>
            !string.IsNullOrEmpty(partType) ? partType : partName;

        /// <summary>World scale that makes a copy of src measure the
        /// part's true physical size (falls back to card size when
        /// the catalogue has no size for this kind).</summary>
        private Vector3 RealWorldScale(Transform src)
        {
            float real = Bench.Twin.TwinCatalog.RealSizeM(PartType);
            float card = ModelScale.MaxExtent(src);
            if (real > 0f && card > 0f)
                return src.lossyScale * (real / card);
            return src.lossyScale;
        }

        private void PlaceHeld(Vector3 pos, Quaternion rot)
        {
            var src = FindModelSource();
            if (src == null)
            {
                SoundBank.Play(BenchSound.Error);
                return;
            }
            var part = Workspace.PlacedPart.Spawn(PartType, partName,
                src, pos, rot, isBoardPart);
            if (part == null) return;
            if (!isBoardPart && !Workspace.WorkspacePhysics.FreezeAll
                && Workspace.WorkspacePhysics.GravityOn)
            {
                // Drop with physics; breadboard snap handled on carry end.
                var rb = part.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.linearVelocity = Vector3.zero;
                }
            }
            SoundBank.PlayAt(BenchSound.Snap, pos);
            Debug.Log("[Bench] placed " + partName + " at " + pos);
        }

        private void EnsureGhost()
        {
            ClearGhost();
            var src = FindModelSource();
            if (src == null) return;
            if (_ghostMat == null)
            {
                _ghostMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                _ghostMat.SetFloat("_Surface", 1f);
                _ghostMat.SetFloat("_AlphaClip", 0f);
                _ghostMat.SetOverrideTag("RenderType", "Transparent");
                _ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _ghostMat.SetInt("_ZWrite", 0);
                _ghostMat.renderQueue = 3000;
                _ghostMat.color = new Color(0.36f, 0.88f, 0.9f, 0.4f);
            }
            _ghost = new GameObject("Ghost_" + partName);
            var copy = Instantiate(src.gameObject, _ghost.transform);
            copy.name = "Model";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            foreach (var r in _ghost.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = _ghostMat;
                r.materials = mats;
            }
            _ghost.transform.localScale = RealWorldScale(src);
            _ghost.transform.position = GhostPose();
        }

        private static Vector3 GhostPose()
        {
            var placer = Workspace.WorkspacePlacer.Instance;
            var ray = Workspace.WorkspacePlacer.CenterEyeRay();
            if (placer != null
                && placer.SurfacePoint(ray, out var p, out _, out _))
                return p;
            return ray.origin + ray.direction * 1.2f;
        }

        private void ClearGhost()
        {
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
        }

        private void Update()
        {
            if (_selected && _ghost != null) _ghost.transform.position = GhostPose();
        }
    }
}
