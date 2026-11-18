// ProjectStore.cs — JSON file I/O for projects: atomic writes
// (temp file + rename), versioned loads with a migration hook, and
// corrupt-file recovery (bad files are skipped with a warning, never
// crash). Keeps the index file in sync. All paths under
// Application.persistentDataPath/projects. Synchronous and small;
// projects are KB-sized.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Bench.Projects
{
    public static class ProjectStore
    {
        public static string ProjectsDir =>
            Path.Combine(Application.persistentDataPath, ProjectSchema.FolderName);

        public static string PathFor(string id) =>
            Path.Combine(ProjectsDir, id + ".json");

        public static string IndexPath =>
            Path.Combine(ProjectsDir, ProjectSchema.IndexFileName);

        public static void EnsureDir() => Directory.CreateDirectory(ProjectsDir);

        public static bool Save(ProjectFile project, out string error)
        {
            error = null;
            try
            {
                EnsureDir();
                project.modifiedAtTicks = DateTime.UtcNow.Ticks;
                project.schemaVersion = ProjectSchema.CurrentVersion;
                string json = JsonUtility.ToJson(project, prettyPrint: true);
                string tmp = PathFor(project.id) + ".tmp";
                File.WriteAllText(tmp, json);
                string dst = PathFor(project.id);
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(tmp, dst);
                UpsertIndex(project);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogWarning("[Bench] project save failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Null when missing or corrupt (warning logged).</summary>
        public static ProjectFile Load(string id)
        {
            string path = PathFor(id);
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path);
                var p = JsonUtility.FromJson<ProjectFile>(json);
                if (p == null || string.IsNullOrEmpty(p.id))
                {
                    Debug.LogWarning($"[Bench] project file {id} has no id — skipped.");
                    return null;
                }
                if (p.schemaVersion != ProjectSchema.CurrentVersion)
                    Migrate(p);
                p.parts ??= new List<ProjectPart>();
                p.wires ??= new List<ProjectWire>();
                return p;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Bench] corrupt project file {id} skipped: {e.Message}");
                return null;
            }
        }

        public static bool Delete(string id)
        {
            try
            {
                string path = PathFor(id);
                if (File.Exists(path)) File.Delete(path);
                var index = LoadIndex();
                index.entries.RemoveAll(e => e.id == id);
                SaveIndex(index);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Bench] project delete failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Recents, newest first, capped at MaxRecent.
        /// Corrupt files are skipped (and pruned from the index).</summary>
        public static List<ProjectIndexEntry> ListRecents()
        {
            var index = LoadIndex();
            var valid = new List<ProjectIndexEntry>();
            bool pruned = false;
            foreach (var e in index.entries)
            {
                if (Load(e.id) != null) valid.Add(e);
                else pruned = true;
            }
            if (pruned)
            {
                index.entries = valid;
                SaveIndex(index);
            }
            valid.Sort((a, b) => b.modifiedAtTicks.CompareTo(a.modifiedAtTicks));
            if (valid.Count > ProjectSchema.MaxRecent)
                valid.RemoveRange(ProjectSchema.MaxRecent,
                    valid.Count - ProjectSchema.MaxRecent);
            return valid;
        }

        /// <summary>All stored ids (files on disk, for reconciliation).</summary>
        public static List<string> ListIdsOnDisk()
        {
            var ids = new List<string>();
            if (!Directory.Exists(ProjectsDir)) return ids;
            foreach (var f in Directory.GetFiles(ProjectsDir, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (name == ProjectSchema.IndexFileName.Replace(".json", "") ||
                    name == "index" || name.EndsWith(".tmp"))
                    continue;
                ids.Add(name);
            }
            return ids;
        }

        // ── Internals ────────────────────────────────────────────

        private static void UpsertIndex(ProjectFile project)
        {
            var index = LoadIndex();
            index.entries.RemoveAll(e => e.id == project.id);
            index.entries.Add(new ProjectIndexEntry
            {
                id = project.id,
                name = project.name,
                modifiedAtTicks = project.modifiedAtTicks,
                boardType = project.boardType,
                partCount = project.parts != null ? project.parts.Count : 0,
            });
            SaveIndex(index);
        }

        private static ProjectIndexFile LoadIndex()
        {
            try
            {
                if (File.Exists(IndexPath))
                {
                    var index = JsonUtility.FromJson<ProjectIndexFile>(
                        File.ReadAllText(IndexPath));
                    if (index != null)
                    {
                        index.entries ??= new List<ProjectIndexEntry>();
                        return index;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Bench] corrupt project index, rebuilding: " + e.Message);
            }
            return new ProjectIndexFile();
        }

        private static void SaveIndex(ProjectIndexFile index)
        {
            try
            {
                EnsureDir();
                string tmp = IndexPath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(index, prettyPrint: true));
                if (File.Exists(IndexPath)) File.Delete(IndexPath);
                File.Move(tmp, IndexPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Bench] project index save failed: " + e.Message);
            }
        }

        private static void Migrate(ProjectFile p)
        {
            // v1 is the first shipped schema; future versions upgrade here.
            Debug.Log($"[Bench] migrating project {p.id} v{p.schemaVersion} -> v{ProjectSchema.CurrentVersion}");
            p.schemaVersion = ProjectSchema.CurrentVersion;
        }
    }
}
