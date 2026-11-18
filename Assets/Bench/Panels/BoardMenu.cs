// BoardMenu.cs — contextual glass pill above a board (Task 6).
// Touching/pinching a board body shows: On/Off toggle with status
// light, Restart, Pin Mode. Follows the board, faces the user, closes
// on pinch elsewhere or after 6 s idle. Power logic: safe shutdown
// (confirm dialog, "Shutting down..."), never a hard cut; Restart does
// shutdown + boot ("Restarting..."); buttons lock during transitions.
// Fires PowerChanged (Task 9 Display window) and PinModeRequested
// (Task 7 Pin Mode). Twin power calls go through ITwinLink when a
// live link exists, otherwise the transition is simulated.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Bench.Core;
using Bench.UI;

namespace Bench.Panels
{
    public class BoardMenu : MonoBehaviour
    {
        public enum PowerState { Off, Booting, On, ShuttingDown, Restarting }

        public static event Action<PlacedPartShim> PinModeRequested;
        public static event Action<PlacedPartShim, bool> PowerChanged;

        // Shim keeps menu code decoupled for tests (real type below).
        public class PlacedPartShim
        {
            public Workspace.PlacedPart Part;
            public PlacedPartShim(Workspace.PlacedPart p) { Part = p; }
        }

        private static BoardMenu _open;
        private Workspace.PlacedPart _board;
        private PowerState _state = PowerState.Off;
        private float _idleT;
        private bool _confirming;
        private Coroutine _transition;

        private GameObject _powerBtn;
        private GameObject _restartBtn;
        private GameObject _pinBtn;
        private GameObject _confirmRow;
        private GameObject _normalRow;
        private TMPro.TMP_Text _titleTxt;
        private TMPro.TMP_Text _statusTxt;
        private Image _lightImg;
        private ITwinLink _twin;

        public static bool IsOpen => _open != null;

        public static void HideAll()
        {
            if (_open != null) _open.Hide();
        }
        public PowerState State => _state;
        public Workspace.PlacedPart Board => _board;

        private void Awake()
        {
            _powerBtn = transform.Find("Card/PowerBtn")?.gameObject;
            _restartBtn = transform.Find("Card/RestartBtn")?.gameObject;
            _pinBtn = transform.Find("Card/PinBtn")?.gameObject;
            _confirmRow = transform.Find("Card/ConfirmRow")?.gameObject;
            _normalRow = transform.Find("Card/NormalRow")?.gameObject;
            var title = transform.Find("Card/Title/Txt")?.GetComponent<TMPro.TMP_Text>();
            if (title != null) _titleTxt = title;
            var status = transform.Find("Card/Status/Txt")?.GetComponent<TMPro.TMP_Text>();
            if (status != null) _statusTxt = status;
            _lightImg = transform.Find("Card/Light")?.GetComponent<Image>();
            Wire(_powerBtn, OnPowerPressed);
            Wire(_restartBtn, OnRestartPressed);
            Wire(_pinBtn, OnPinPressed);
            var confirm = transform.Find("Card/ConfirmRow/ConfirmBtn")?.gameObject;
            var cancel = transform.Find("Card/ConfirmRow/CancelBtn")?.gameObject;
            Wire(confirm, OnConfirmShutdown);
            Wire(cancel, () => { _confirming = false; Refresh(); });
            try
            {
                _twin = UnityEngine.Object.FindFirstObjectByType<TwinBridge>() as ITwinLink;
            }
            catch { _twin = null; }
        }

        private static void Wire(GameObject go, Action onClick)
        {
            if (go == null) return;
            var b = go.GetComponent<Button>();
            if (b == null) b = go.AddComponent<Button>();
            b.onClick.AddListener(() =>
            {
                SoundBank.Play(BenchSound.Press);
                onClick?.Invoke();
            });
        }

        public static void Show(Workspace.PlacedPart board)
        {
            var menu = FindFirstObjectByType<BoardMenu>(
                FindObjectsInactive.Include);
            if (menu == null || board == null) return;
            if (_open != null && _open != menu) _open.Hide();
            _open = menu;
            // Resync: the template is reused across boards, so a
            // previous board's On state would mislabel this one.
            if (menu._transition != null)
            {
                menu.StopCoroutine(menu._transition);
                menu._transition = null;
            }
            menu._state = board.powered ? PowerState.On : PowerState.Off;
            menu._board = board;
            menu._confirming = false;
            menu._idleT = 0f;
            menu.gameObject.SetActive(true);
            menu.Refresh();
            SoundBank.Play(BenchSound.Hover);
        }

        public void Hide()
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = null;
            _confirming = false;
            gameObject.SetActive(false);
            if (_open == this) _open = null;
        }

        private void Update()
        {
            if (!gameObject.activeInHierarchy || _board == null)
            {
                if (gameObject.activeInHierarchy && _board == null) Hide();
                return;
            }
            // Follow the board, billboard toward the user (damped).
            var cam = Camera.main;
            Vector3 head = cam != null ? cam.transform.position
                : new Vector3(0f, 1.6f, -1f);
            transform.position = _board.transform.position + Vector3.up * 0.22f;
            var look = head - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-6f)
            {
                var want = Quaternion.LookRotation(-look.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    want, 90f * Time.unscaledDeltaTime);
            }
            _idleT += Time.unscaledDeltaTime;
            if (_idleT >= 6f) Hide(); // 6 s inactivity (gaze-away on device)
        }

        public void Poke() // any interaction resets the idle timer
        {
            _idleT = 0f;
        }

        private void OnPowerPressed()
        {
            Poke();
            if (_transition != null || _board == null) return;
            if (_state == PowerState.Off)
                _transition = StartCoroutine(PowerOnFlow());
            else if (_state == PowerState.On && !_confirming)
            {
                _confirming = true; // ask first: never a hard cut
                Refresh();
            }
        }

        private void OnConfirmShutdown()
        {
            Poke();
            if (_transition != null || _state != PowerState.On) return;
            _confirming = false;
            _transition = StartCoroutine(PowerOffFlow());
        }

        private void OnRestartPressed()
        {
            Poke();
            if (_transition != null || _board == null) return;
            if (_state == PowerState.Off || _state == PowerState.On)
                _transition = StartCoroutine(RestartFlow());
        }

        private void OnPinPressed()
        {
            Poke();
            if (_transition != null || _board == null) return;
            PinModeRequested?.Invoke(new PlacedPartShim(_board));
            SoundBank.PlayAt(BenchSound.Snap, _board.transform.position);
        }

        private IEnumerator PowerOnFlow()
        {
            _state = PowerState.Booting;
            Refresh();
            TwinPower(true);
            yield return new WaitForSecondsRealtime(0.8f);
            if (_board == null) { _transition = null; Hide(); yield break; }
            _state = PowerState.On;
            _board.powered = true;
            _transition = null;
            Refresh();
            PowerChanged?.Invoke(new PlacedPartShim(_board), true);
            SoundBank.PlayAt(BenchSound.PowerOn, _board.transform.position);
        }

        private IEnumerator PowerOffFlow()
        {
            _state = PowerState.ShuttingDown;
            Refresh();
            TwinPower(false);
            yield return new WaitForSecondsRealtime(1.2f);
            if (_board == null) { _transition = null; Hide(); yield break; }
            _state = PowerState.Off;
            _board.powered = false;
            _transition = null;
            Refresh();
            PowerChanged?.Invoke(new PlacedPartShim(_board), false);
        }

        private IEnumerator RestartFlow()
        {
            _state = PowerState.Restarting;
            Refresh();
            TwinRestart();
            yield return new WaitForSecondsRealtime(1.5f);
            if (_board == null) { _transition = null; Hide(); yield break; }
            _state = PowerState.On;
            _board.powered = true;
            _transition = null;
            Refresh();
            PowerChanged?.Invoke(new PlacedPartShim(_board), true);
            SoundBank.PlayAt(BenchSound.PowerOn, _board.transform.position);
        }

        private void TwinPower(bool on)
        {
            try
            {
                if (_twin != null && !Bench.Core.BenchSettings.DemoMode)
                {
                    _twin.Power(on);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Bench] twin power failed, simulated: "
                    + e.Message);
            }
            Debug.Log("[Bench] power " + (on ? "ON" : "OFF (safe shutdown)")
                + " (simulated" + (_twin == null ? ", no link" : ", demo") + ")");
        }

        private void TwinRestart()
        {
            try
            {
                if (_twin != null && !Bench.Core.BenchSettings.DemoMode)
                {
                    _twin.Restart();
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Bench] twin restart failed, simulated: "
                    + e.Message);
            }
            Debug.Log("[Bench] restart (simulated)");
        }

        private void Refresh()
        {
            if (_board != null && _titleTxt != null)
                _titleTxt.text = _board.displayName;
            string status = _state switch
            {
                PowerState.On => "On",
                PowerState.Off => "Off",
                PowerState.Booting => "Booting…",
                PowerState.ShuttingDown => "Shutting down…",
                PowerState.Restarting => "Restarting…",
                _ => ""
            };
            if (_statusTxt != null) _statusTxt.text = status;
            if (_lightImg != null)
                _lightImg.color = _state == PowerState.On
                    ? new Color(0.29f, 0.87f, 0.5f)
                    : _state == PowerState.Off
                        ? new Color(1f, 1f, 1f, 0.35f)
                        : new Color(1f, 0.71f, 0.33f);
            SetLabel(_powerBtn,
                _state == PowerState.On ? "Turn Off" : "Turn On");
            bool busy = _transition != null;
            SetInteractable(_powerBtn, !busy && _board != null);
            SetInteractable(_restartBtn, !busy && _board != null);
            SetInteractable(_pinBtn, !busy && _board != null);
            if (_confirmRow != null) _confirmRow.SetActive(_confirming);
            if (_normalRow != null) _normalRow.SetActive(!_confirming);
            if (_pinBtn != null) _pinBtn.SetActive(!_confirming);
        }

        private static void SetLabel(GameObject btn, string text)
        {
            var t = btn?.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (t != null) t.text = text;
        }

        private static void SetInteractable(GameObject btn, bool on)
        {
            var b = btn?.GetComponent<Button>();
            if (b != null) b.interactable = on;
        }
    }
}
