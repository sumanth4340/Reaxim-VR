// PlacedPart.cs — a part resting in the workspace (Task 6).
// Rigidbody + convex mesh colliders so parts collide with each other
// and the fallback floor. Carry = ray follow with throw velocity;
// release drops (optionally snapped to the breadboard grid).
// Board taps open the board menu; component taps carry.
// Interaction: runtime PointableUnityEventWrapper (no editor baking
// needed) plus direct test hooks that drive the same code paths.
using System;
using UnityEngine;
using Oculus.Interaction;

namespace Bench.Workspace
{
    [RequireComponent(typeof(Rigidbody))]
    public class PlacedPart : MonoBehaviour
    {
        public string partId = "";
        public string type = "";
        public string displayName = "";
        public bool isBoard;
        public bool powered; // board power state (BoardMenu edits this)

        public static event Action<PlacedPart> BoardTapped;
        public static event Action<PlacedPart> BoardDestroyed;

        internal bool Carried => _carried;

        private Rigidbody _rb;
        private bool _carried;
        private bool _trackPointer;
        private bool _testDrive;
        private Ray _carryRay;
        private const float CarryDistance = 0.6f;
        private const float MaxThrowSpeed = 3f;
        private Vector3 _lastPos;
        private float _lastTime;
        private Vector3 _throwVel;

        /// <summary>Spawn from a card model source (see Draggable3D).</summary>
        public static PlacedPart Spawn(string type, string display,
            Transform modelSrc, Vector3 pos, Quaternion rot, bool isBoard,
            string id = null)
        {
            var go = new GameObject("Placed_" + display);
            go.transform.position = pos;
            var copy = Instantiate(modelSrc.gameObject, go.transform);
            copy.name = "Model";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            go.transform.localScale = modelSrc.lossyScale;
            // True physical size, measured unrotated (a yawed AABB
            // reads up to 41% large and would shrink the part).
            float real = Bench.Twin.TwinCatalog.RealSizeM(type);
            if (real > 0f)
                Bench.UI.ModelScale.FitToSize(copy.transform, real, pos,
                    tipFlat: false); // land flat, never tipped
            // Yaw only, applied after the fit: card models carry
            // the tile tip (-55 deg X) and a turntable spin angle.
            go.transform.rotation =
                Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
            var part = go.AddComponent<PlacedPart>();
            part.partId = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id;
            part.type = type;
            part.displayName = display;
            part.isBoard = isBoard;
            go.AddComponent<Bench.Hands.PartHandGrab>();
            if (type == "LED") go.AddComponent<LedGlow>();
            part.EnsurePhysics();
            // NOTE: no ISDK interactable here on purpose. Poke/Grab
            // interactables need editor-baked companions (Surface Patch,
            // Pointable) that cannot be configured correctly with pure
            // AddComponent (verified: both assert in Play). The device
            // input binding for spawned parts lands in Task 10 as baked
            // per-kind prefabs; behavior below is driven by OnMouseDown
            // (editor) and the Test hooks (harness) through identical
            // OnSelected/OnUnselected paths.
            WorkspacePhysics.Register(part);
            WorkspacePhysics.ApplyAll();
            if (Bench.PinMode.PinModeController.Instance != null
                && Bench.PinMode.PinModeController.Instance.Active)
                Bench.PinMode.PinModeController.RefreshPins();
            return part;
        }

        private void EnsurePhysics()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true; // released explicitly by drop/carry end
            _rb.useGravity = false;
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null)
                    continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.convex = true;
            }
            if (GetComponent<Collider>() == null)
            {
                var b = MeshBounds();
                var box = gameObject.AddComponent<BoxCollider>();
                box.center = transform.InverseTransformPoint(b.center);
                box.size = b.size / Mathf.Max(1e-6f, transform.lossyScale.x);
            }
        }

        private Bounds MeshBounds()
        {
            Bounds b;
            if (Bench.UI.ModelScale.Measure(transform, out b)) return b;
            return new Bounds(transform.position, Vector3.zero);
        }

        private void OnSelected()
        {
            if (isBoard)
            {
                Bench.Panels.BoardMenu.Show(this);
                return;
            }
            BeginCarry(WorkspacePlacer.CenterEyeRay(), trackPointer: false);
        }

        private void OnUnselected()
        {
            if (_carried) EndCarry(drop: true);
        }

        private void OnMouseDown()
        {
            if (isBoard)
            {
                Bench.Panels.BoardMenu.Show(this);
                return;
            }
            var cam = Camera.main;
            if (cam == null) return;
            BeginCarry(cam.ScreenPointToRay(Input.mousePosition), trackPointer: true);
        }

        private void OnMouseUp()
        {
            if (_carried && !_trackPointer) return;
            if (_carried) EndCarry(drop: true);
        }

        // ── carry (test hooks drive the same path) ────────────────

        public void TestBeginCarry(Vector3 target)
        {
            _testDrive = true; // programmatic carry: Update must not steer
            BeginCarry(new Ray(target, Vector3.forward), false);
            MoveCarry(target);
        }
        public void TestMoveCarry(Vector3 target) => MoveCarry(target);
        public void TestEndCarry(bool drop, Vector3 throwVel = default) =>
            EndCarry(drop, throwVel);

        private void BeginCarry(Ray ray, bool trackPointer)
        {
            if (_carried) return;
            _carried = true;
            _trackPointer = trackPointer;
            _carryRay = ray;
            _rb.isKinematic = true;
            _rb.linearVelocity = Vector3.zero;
            _lastPos = transform.position;
            _lastTime = Time.unscaledTime;
            _throwVel = Vector3.zero;
            Bench.UI.SoundBank.PlayAt(Bench.UI.BenchSound.Press,
                transform.position);
        }

        private void MoveCarry(Vector3 target)
        {
            if (!_carried) return;
            float dt = Mathf.Max(1e-4f, Time.unscaledTime - _lastTime);
            _throwVel = Vector3.Lerp(_throwVel,
                (target - _lastPos) / dt, 0.5f);
            transform.position = target;
            _lastPos = target;
            _lastTime = Time.unscaledTime;
        }

        private void EndCarry(bool drop, Vector3? overrideVel = null)
        {
            if (!_carried) return;
            _carried = false;
            _testDrive = false;
            if (!drop)
            {
                WorkspacePhysics.ApplyAll();
                return;
            }
            // Breadboard magnetic snap first (small parts only).
            if (!isBoard && BreadboardSnap.TrySnap(transform.position,
                out var snapped, out _))
            {
                transform.position = snapped;
                _rb.isKinematic = true;
                _rb.linearVelocity = Vector3.zero;
                Bench.UI.SoundBank.PlayAt(Bench.UI.BenchSound.Snap,
                    transform.position);
                WorkspacePhysics.ApplyAll();
                return;
            }
            bool dyn = !WorkspacePhysics.FreezeAll;
            _rb.isKinematic = !dyn;
            var v = overrideVel ?? _throwVel;
            if (v.magnitude > MaxThrowSpeed)
                v = v.normalized * MaxThrowSpeed;
            _rb.linearVelocity = dyn ? v : Vector3.zero;
            _rb.useGravity = dyn && WorkspacePhysics.GravityOn;
            Bench.UI.SoundBank.PlayAt(Bench.UI.BenchSound.Snap,
                transform.position);
            WorkspacePhysics.ApplyAll();
        }

        private void Update()
        {
            if (!_carried || _testDrive) return;
            Ray ray = _trackPointer && Camera.main != null
                ? Camera.main.ScreenPointToRay(Input.mousePosition)
                : WorkspacePlacer.CenterEyeRay();
            _carryRay = ray;
            MoveCarry(ray.origin + ray.direction * CarryDistance);
        }

        private void OnEnable() => WorkspacePhysics.Register(this);
        private void OnDisable() => WorkspacePhysics.Unregister(this);
        private void OnDestroy()
        {
            if (isBoard)
            {
                try { BoardDestroyed?.Invoke(this); } catch { }
            }
            WorkspacePhysics.Unregister(this);
            if (!gameObject.scene.isLoaded) return; // teardown, skip
            if (Bench.PinMode.PinModeController.Instance != null
                && Bench.PinMode.PinModeController.Instance.Active)
                Bench.PinMode.PinModeController.RefreshPins();
        }
    }
}
