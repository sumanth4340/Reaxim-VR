// ProjectManager.cs — runtime project API used by the Home screen
// (Task 4), the panel (Task 5) and the state machine's autosave.
// Create validates (1-40 chars, trimmed, unique with numeric suffix),
// Open loads data and fires OnProjectOpened for the workspace to
// spawn parts (Task 5/6). Rename/Duplicate/Delete are the UI's
// building blocks (the confirm dialog lives in Task 4). Implements
// Core.ISaveService so AppStateMachine can call Save(reason).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bench.Projects
{
    public class ProjectManager : MonoBehaviour, Core.ISaveService
    {
        public static ProjectManager Instance { get; private set; }

        public ProjectFile Current { get; private set; }

        public event Action<ProjectFile> Opened;
        public event Action ProjectsChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            var machine = Core.AppStateMachine.Instance;
            if (machine != null) machine.Saver = this;
        }

        // ── ISaveService ─────────────────────────────────────────

        public void Save(string reason)
        {
            if (Current == null) return;
            if (ProjectStore.Save(Current, out _)) ProjectsChanged?.Invoke();
            else Debug.LogWarning("[Bench] autosave ('" + reason + "') failed.");
        }

        // ── API ──────────────────────────────────────────────────

        public List<ProjectIndexEntry> Recents() => ProjectStore.ListRecents();

        /// <summary>Create + open a project. Returns null on invalid input.</summary>
        public ProjectFile Create(string rawName, string template, string boardType = null)
        {
            string name = (rawName ?? "").Trim();
            if (name.Length == 0 || name.Length > ProjectSchema.MaxNameLength)
                return null;
            if (!IsValidTemplate(template)) template = ProjectTemplates.Blank;
            name = UniqueName(name);
            var now = DateTime.UtcNow;
            var p = new ProjectFile
            {
                name = name,
                createdAtTicks = now.Ticks,
                modifiedAtTicks = now.Ticks,
                template = template,
                boardType = string.IsNullOrEmpty(boardType)
                    ? ProjectTemplates.DefaultBoard(template) : boardType,
                parts = ProjectTemplates.StarterParts(template),
            };
            if (!ProjectStore.Save(p, out _)) return null;
            Current = p;
            ProjectsChanged?.Invoke();
            Opened?.Invoke(p);
            return p;
        }

        /// <summary>Load by id and make current. Null when missing/corrupt.</summary>
        public ProjectFile Open(string id)
        {
            var p = ProjectStore.Load(id);
            if (p == null) return null;
            Current = p;
            ProjectsChanged?.Invoke();
            Opened?.Invoke(p);
            return p;
        }

        public void Close()
        {
            Current = null;
            ProjectsChanged?.Invoke();
        }

        public bool Rename(string id, string rawName)
        {
            string name = (rawName ?? "").Trim();
            if (name.Length == 0 || name.Length > ProjectSchema.MaxNameLength)
                return false;
            var p = id == Current?.id ? Current : ProjectStore.Load(id);
            if (p == null) return false;
            p.name = UniqueName(name, exceptId: id);
            if (!ProjectStore.Save(p, out _)) return false;
            ProjectsChanged?.Invoke();
            return true;
        }

        public ProjectFile Duplicate(string id)
        {
            var src = ProjectStore.Load(id);
            if (src == null) return null;
            var now = DateTime.UtcNow;
            var copy = new ProjectFile
            {
                name = UniqueName(src.name + " copy"),
                createdAtTicks = now.Ticks,
                modifiedAtTicks = now.Ticks,
                template = src.template,
                boardType = src.boardType,
                parts = new List<ProjectPart>(src.parts ?? new List<ProjectPart>()),
                wires = new List<ProjectWire>(src.wires ?? new List<ProjectWire>()),
                anchorUuid = "",
            };
            if (!ProjectStore.Save(copy, out _)) return null;
            ProjectsChanged?.Invoke();
            return copy;
        }

        public bool Delete(string id)
        {
            if (!ProjectStore.Delete(id)) return false;
            if (Current != null && Current.id == id) Current = null;
            ProjectsChanged?.Invoke();
            return true;
        }

        public void Touch()
        {
            if (Current == null) return;
            Current.modifiedAtTicks = DateTime.UtcNow.Ticks;
            ProjectStore.Save(Current, out _);
            ProjectsChanged?.Invoke();
        }

        // ── Helpers ──────────────────────────────────────────────

        private string UniqueName(string baseName, string exceptId = null)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in ProjectStore.ListRecents())
                if (e.id != exceptId) taken.Add(e.name);
            // Fall back to a disk scan when the index lags.
            foreach (var id in ProjectStore.ListIdsOnDisk())
            {
                if (id == exceptId) continue;
                var p = ProjectStore.Load(id);
                if (p != null) taken.Add(p.name);
            }
            if (!taken.Contains(baseName)) return baseName;
            for (int i = 2; ; i++)
            {
                string candidate = $"{baseName} {i}";
                if (candidate.Length <= ProjectSchema.MaxNameLength &&
                    !taken.Contains(candidate))
                    return candidate;
                if (candidate.Length > ProjectSchema.MaxNameLength)
                {
                    string cut = baseName.Substring(
                        0, Math.Max(1, ProjectSchema.MaxNameLength -
                                     (" " + i).Length));
                    candidate = $"{cut} {i}";
                    if (!taken.Contains(candidate)) return candidate;
                }
            }
        }

        private static bool IsValidTemplate(string t) =>
            t == ProjectTemplates.Blank ||
            t == ProjectTemplates.PiStarter ||
            t == ProjectTemplates.JetsonStarter;
    }
}
