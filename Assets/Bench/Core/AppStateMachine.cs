// AppStateMachine.cs — HOME -> PROJECT_PANEL -> WORKSPACE (+ back).
// Every transition is named and tweened; no hard cuts. The machine
// owns the glass layer (overlay dissolve + passthrough styling) and
// fires StateChanged for UI. Windows themselves are built in later
// tasks and registered here (null-safe until then). Saving goes
// through ISaveService (Task 3 implements it; NullSaveService logs).
using System;
using System.Collections;
using UnityEngine;

namespace Bench.Core
{
    public enum AppState { Home, ProjectPanel, Workspace }

    public interface ISaveService
    {
        void Save(string reason);
    }

    public sealed class NullSaveService : ISaveService
    {
        public void Save(string reason) =>
            Debug.Log("[Bench] Save('" + reason + "') — no ProjectManager yet (Task 3).");
    }

    public class AppStateMachine : MonoBehaviour
    {
        public static AppStateMachine Instance { get; private set; }

        public AppState Current { get; private set; } = AppState.Home;

        /// <summary>Windows owned by later tasks (may be null until built).</summary>
        [Header("Windows (Tasks 4+)")]
        public UI.GlassWindow homeWindow;
        public UI.GlassWindow projectPanel;

        public ISaveService Saver { get; set; } = new NullSaveService();

        public event Action<AppState, AppState> StateChanged;

        private Glass.GlassOverlay _overlay;
        private Glass.PassthroughStyler _styler;
        private float _autosaveT;
        private Coroutine _transition;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _overlay = FindAnyObjectByType<Glass.GlassOverlay>(
                FindObjectsInactive.Include);
            _styler = FindAnyObjectByType<Glass.PassthroughStyler>(
                FindObjectsInactive.Include);
        }

        private void Start()
        {
            // Glass on, no transition on cold boot.
            ApplyGlassLayer(glassOn: true, instant: true);
            StateChanged?.Invoke(Current, Current);
        }

        private void Update()
        {
            _autosaveT += Time.unscaledDeltaTime;
            if (_autosaveT >= 20f)
            {
                _autosaveT = 0f;
                Saver.Save("autosave");
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Saver.Save("pause");
        }

        /// <summary>Home button: save first, then go HOME.</summary>
        public void RequestHome()
        {
            Saver.Save("home-button");
            TransitionTo(AppState.Home);
        }

        public void TransitionTo(AppState next)
        {
            if (_transition != null) StopCoroutine(_transition);
            var from = Current;
            Current = next;
            Saver.Save("state-change");
            _transition = StartCoroutine(RunTransition(from, next));
        }

        private IEnumerator RunTransition(AppState from, AppState to)
        {
            if (from == to) yield break;
            if (from == AppState.Home && to == AppState.ProjectPanel)
                yield return HomeToPanel();
            else if (from == AppState.ProjectPanel && to == AppState.Workspace)
                yield return PanelToWorkspace();
            else if (from == AppState.Workspace && to == AppState.Home)
                yield return WorkspaceToHome();
            else if (from == AppState.ProjectPanel && to == AppState.Home)
                yield return PanelToHome();
            else if (from == AppState.Home && to == AppState.Workspace)
                yield return HomeToWorkspace();
            else if (from == AppState.Workspace && to == AppState.ProjectPanel)
                yield return WorkspaceToPanel();
            StateChanged?.Invoke(from, to);
        }

        // ── Named transitions ────────────────────────────────────

        private IEnumerator HomeToPanel()
        {
            if (homeWindow != null)
            {
                homeWindow.Hide();
                yield return new WaitForSecondsRealtime(0.25f);
            }
            if (projectPanel != null) projectPanel.Show();
            ApplyGlassLayer(glassOn: true, instant: false);
            yield break;
        }

        private IEnumerator PanelToHome()
        {
            if (projectPanel != null)
            {
                projectPanel.Hide();
                yield return new WaitForSecondsRealtime(0.25f);
            }
            if (homeWindow != null) homeWindow.Show();
            ApplyGlassLayer(glassOn: true, instant: false);
            yield break;
        }

        private IEnumerator PanelToWorkspace()
        {
            // Panel glides to its LEFT dock (320 ms) while the glass
            // layer dissolves (700 ms) — the signature transition.
            var layout = Windows.WindowLayoutManager.Instance;
            if (layout != null && projectPanel != null)
                layout.DockToSlot(projectPanel,
                    Windows.SlotKind.Left, animate: true);
            else if (projectPanel != null)
                projectPanel.Hide();
            ApplyGlassLayer(glassOn: false, instant: false);
            yield return new WaitForSecondsRealtime(0.75f);
        }

        private IEnumerator WorkspaceToHome()
        {
            ApplyGlassLayer(glassOn: true, instant: false);
            if (projectPanel != null) projectPanel.Hide();
            if (homeWindow != null) homeWindow.Show();
            yield return new WaitForSecondsRealtime(0.75f);
        }

        private IEnumerator HomeToWorkspace()
        {
            if (homeWindow != null) homeWindow.Hide();
            ApplyGlassLayer(glassOn: false, instant: false);
            yield return new WaitForSecondsRealtime(0.75f);
        }

        private IEnumerator WorkspaceToPanel()
        {
            ApplyGlassLayer(glassOn: true, instant: false);
            if (projectPanel != null) projectPanel.Show();
            yield return new WaitForSecondsRealtime(0.75f);
        }

        private void ApplyGlassLayer(bool glassOn, bool instant)
        {
            var theme = UI.BenchTheme.Current;
            float dur = (theme != null ? theme.dissolveDuration : 0.7f);
            if (instant || BenchSettings.ReduceMotion) dur = 0f;
            if (_overlay != null)
            {
                if (dur <= 0f) _overlay.SetDissolved(glassOn ? 0f : 1f);
                else _overlay.DissolveTo(glassOn ? 0f : 1f);
            }
            if (_styler != null)
            {
                if (dur <= 0f)
                {
                    if (glassOn) _styler.ApplySoft(); else _styler.ApplyNormal();
                }
                else _styler.TransitionTo(glassOn);
            }
        }
    }
}
