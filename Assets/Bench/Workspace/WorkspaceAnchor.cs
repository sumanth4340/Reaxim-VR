// WorkspaceAnchor.cs — spatial anchor save/restore + layout persist.
// On device: a real OVRSpatialAnchor (world-locked across sessions).
// In the editor (or when anchoring fails): a local PlayerPrefs pose
// fallback, honestly reported. Restores project parts on open.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Bench.Core;
using Bench.Projects;

namespace Bench.Workspace
{
    public class WorkspaceAnchor : MonoBehaviour
    {
        public static WorkspaceAnchor Instance { get; private set; }

        public const string LocalPrefix = "local:";
        public string LastAnchorUuid { get; private set; } = "";
        public string LastAnchorKind { get; private set; } = "none";

        private GameObject _origin;
        private float _syncT;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            _origin = GameObject.Find("WorkspaceOrigin");
            if (_origin == null)
            {
                _origin = new GameObject("WorkspaceOrigin");
                _origin.transform.position = new Vector3(0f, 0.75f, 0f);
            }
            var mgr = ProjectManager.Instance;
            if (mgr != null) mgr.Opened += OnProjectOpened;
            var machine = AppStateMachine.Instance;
            if (machine != null) machine.StateChanged += OnState;
        }

        private void OnDestroy()
        {
            var mgr = ProjectManager.Instance;
            if (mgr != null) mgr.Opened -= OnProjectOpened;
            var machine = AppStateMachine.Instance;
            if (machine != null) machine.StateChanged -= OnState;
            if (Instance == this) Instance = null;
        }

        private ProjectFile _pendingProject;

        private void OnProjectOpened(ProjectFile p)
        {
            var machine = AppStateMachine.Instance;
            if (machine != null && machine.Current == AppState.Workspace)
                RestoreLayout(p);
            else
                _pendingProject = p; // restore on entering Workspace
        }

        private void OnState(AppState from, AppState to)
        {
            if (to == AppState.Workspace && _pendingProject != null)
            {
                var p = _pendingProject;
                _pendingProject = null;
                RestoreLayout(p);
            }
        }

        private void Update()
        {
            _syncT += Time.unscaledDeltaTime;
            if (_syncT < 2f) return;
            _syncT = 0f;
            SyncToProject();
        }

        /// <summary>Copy live part poses into the open project file
        /// (in memory; the existing autosave persists it).</summary>
        public void SyncToProject()
        {
            var mgr = ProjectManager.Instance;
            var cur = mgr != null ? mgr.Current : null;
            if (cur == null) return;
            var keep = new Dictionary<string, ProjectPart>();
            foreach (var e in cur.parts ?? new List<ProjectPart>())
                if (!string.IsNullOrEmpty(e.id)) keep[e.id] = e;
            var rebuilt = new List<ProjectPart>();
            foreach (var p in WorkspacePhysics.PartsSnapshot())
            {
                if (p == null) continue;
                ProjectPart entry = keep.TryGetValue(p.partId, out var old) ? old
                    : new ProjectPart { id = p.partId };
                entry.type = p.type;
                entry.position = p.transform.position;
                entry.rotationEuler = p.transform.rotation.eulerAngles;
                entry.parentId = "";
                rebuilt.Add(entry);
            }
            cur.parts = rebuilt;
        }

        /// <summary>Save the workspace anchor for a project file.</summary>
        public IEnumerator SaveAnchor(ProjectFile project,
            Action<string> done = null)
        {
            string uuid = "";
            string kind = "local";
            // Try a real spatial anchor first (device only). Yields stay
            // outside try/catch (CS1626) and results use TryGetResult.
            OVRSpatialAnchor anchor = null;
            OVRTask<OVRResult<OVRAnchor.SaveResult>> task = default;
            bool started = false;
            string setupError = null;
            try
            {
                anchor = _origin.GetComponent<OVRSpatialAnchor>();
                if (anchor == null) anchor = _origin.AddComponent<OVRSpatialAnchor>();
                task = anchor.SaveAnchorAsync();
                started = true;
            }
            catch (Exception e)
            {
                setupError = e.GetType().Name;
            }
            if (setupError != null)
                Debug.Log("[Bench] anchor: spatial save unavailable ("
                    + setupError + "), using local pose.");
            if (started)
            {
                float t = 0f;
                while (!task.IsCompleted && t < 8f)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (task.IsCompleted && task.TryGetResult(out var r)
                    && r.Success)
                {
                    uuid = anchor.Uuid.ToString();
                    kind = "spatial";
                }
                else if (anchor != null)
                {
                    Destroy(anchor);
                }
            }
            if (kind == "local")
            {
                uuid = LocalPrefix + Guid.NewGuid().ToString("N");
                var p = _origin.transform.position;
                var r = _origin.transform.rotation;
                PlayerPrefs.SetString("bench.anchor." + uuid,
                    p.x + "," + p.y + "," + p.z + "|"
                    + r.x + "," + r.y + "," + r.z + "," + r.w);
                PlayerPrefs.Save();
            }
            project.anchorUuid = uuid;
            LastAnchorUuid = uuid;
            LastAnchorKind = kind;
            Debug.Log("[Bench] anchor saved (" + kind + "): " + uuid);
            done?.Invoke(uuid);
        }

        /// <summary>Restore the workspace origin from an anchor uuid.</summary>
        public IEnumerator LoadAnchor(string uuid, Action<bool> done = null)
        {
            bool ok = false;
            Guid guid = Guid.Empty;
            bool wantSpatial = !string.IsNullOrEmpty(uuid)
                && !uuid.StartsWith(LocalPrefix) && Guid.TryParse(uuid, out guid);
            var list = new List<OVRSpatialAnchor.UnboundAnchor>();
            bool loadStarted = false;
            string loadError = null;
            if (wantSpatial)
            {
                try
                {
                    OVRSpatialAnchor.LoadUnboundAnchorsAsync(
                        new[] { guid }, list, null);
                    loadStarted = true;
                }
                catch (Exception e) { loadError = e.Message; }
                if (loadError != null)
                    Debug.LogWarning("[Bench] anchor load failed: " + loadError);
            }
            if (loadStarted)
            {
                // Poll without a captured task handle: completion is
                // signalled by results arriving in the list.
                float t = 0f;
                while (list.Count == 0 && t < 8f)
                {
                    t += Time.unscaledDeltaTime;
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                if (list.Count > 0)
                {
                    try
                    {
                        var comp = _origin.GetComponent<OVRSpatialAnchor>();
                        if (comp == null)
                            comp = _origin.AddComponent<OVRSpatialAnchor>();
                        list[0].BindTo(comp);
                        ok = true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[Bench] anchor bind failed: " + e.Message);
                    }
                }
            }
            if (!ok && !string.IsNullOrEmpty(uuid) && uuid.StartsWith(LocalPrefix))
            {
                string s = PlayerPrefs.GetString("bench.anchor." + uuid, "");
                var halves = s.Split('|');
                if (halves.Length == 2)
                {
                    var pp = halves[0].Split(',');
                    var rr = halves[1].Split(',');
                    if (pp.Length == 3 && rr.Length == 4
                        && float.TryParse(pp[0], out var x)
                        && float.TryParse(pp[1], out var y)
                        && float.TryParse(pp[2], out var z)
                        && float.TryParse(rr[0], out var qx)
                        && float.TryParse(rr[1], out var qy)
                        && float.TryParse(rr[2], out var qz)
                        && float.TryParse(rr[3], out var qw))
                    {
                        _origin.transform.position = new Vector3(x, y, z);
                        _origin.transform.rotation = new Quaternion(qx, qy, qz, qw);
                        ok = true;
                    }
                }
            }
            if (ok)
            {
                LastAnchorUuid = uuid;
                LastAnchorKind = uuid.StartsWith(LocalPrefix) ? "local" : "spatial";
            }
            Debug.Log("[Bench] anchor load " + (ok ? "ok" : "FAILED")
                + ": " + uuid);
            done?.Invoke(ok);
        }

        /// <summary>Instantiate the project's parts at their saved poses.</summary>
        public void RestoreLayout(ProjectFile project)
        {
            foreach (var t in GameObject.FindObjectsOfType<Transform>(true))
                if (t.parent == null && t.name.StartsWith("Placed_"))
                    Destroy(t.gameObject);
            if (project == null || project.parts == null) return;
            int n = 0;
            foreach (var part in project.parts)
            {
                var src = FindModelSource(part.type);
                if (src == null)
                {
                    Debug.LogWarning("[Bench] restore: no model for kind "
                        + part.type);
                    continue;
                }
                bool board = part.type.StartsWith("Board");
                PlacedPart.Spawn(part.type, DisplayFor(part.type), src,
                    part.position, Quaternion.Euler(part.rotationEuler),
                    board, part.id);
                n++;
            }
            WorkspacePhysics.ApplyAll();
            Debug.Log("[Bench] layout restored: " + n + " parts.");
        }

        private static string DisplayFor(string type) => type switch
        {
            "BoardRPi4B" => "Raspberry Pi 4B",
            "BoardJetsonNano" => "Jetson Nano",
            "Breadboard" => "Breadboard",
            "LED" => "LED red",
            _ => type,
        };

        public static Transform FindModelSource(string type)
        {
            string card, tile;
            if (type == "BoardRPi4B") card = "BoardCard_RaspberryPi4B";
            else if (type == "BoardJetsonNano") card = "BoardCard_JetsonNano";
            else card = null;
            tile = "Tile_" + type;
            foreach (var d in GameObject.FindObjectsOfType<Bench.UI.Draggable3D>(true))
            {
                var root = d.gameObject;
                while (root.transform.parent != null
                    && !root.name.StartsWith("BoardCard_")
                    && !root.name.StartsWith("Tile_"))
                    root = root.transform.parent.gameObject;
                if ((card != null && root.name == card)
                    || root.name == tile)
                {
                    var spin = d.GetComponentInChildren<TurntableSpin>(true);
                    if (spin == null) continue;
                    var pivot = spin.transform.Find("Pivot");
                    if (pivot == null || pivot.childCount == 0) continue;
                    return pivot.GetChild(0);
                }
            }
            return null;
        }
    }
}
