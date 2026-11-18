// ProjectData.cs — on-disk schema for MR Bench projects.
// One JSON file per project under <persistentDataPath>/projects,
// plus a small index file. Written with atomic temp+rename writes,
// versioned for migrations, and tolerant of corrupt files (skip +
// report, never crash). Units: metres, Unity left-handed (matches
// the twin bridge mapping).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bench.Projects
{
    public static class ProjectSchema
    {
        public const int CurrentVersion = 1;
        public const string FolderName = "projects";
        public const string IndexFileName = "index.json";
        public const int MaxRecent = 10;
        public const int MaxNameLength = 40;
    }

    [Serializable]
    public class ProjectPart
    {
        public string id = Guid.NewGuid().ToString();
        /// <summary>Kind key, e.g. BoardRPi4B, BoardJetsonNano,
        /// Breadboard, LED, Button, Servo, BME280, MCP3008, GPS.
        /// Must stay within Bench.Twin.TwinCatalog component kinds.</summary>
        public string type = "";
        public Vector3 position;
        public Vector3 rotationEuler;
        /// <summary>Empty = parented to the workspace anchor.</summary>
        public string parentId = "";
        /// <summary>Free-form per-part JSON (colour, value...).</summary>
        public string paramsJson = "";
    }

    [Serializable]
    public class ProjectWire
    {
        public string id = Guid.NewGuid().ToString();
        /// <summary>Node strings, same vocabulary as the twin bridge
        /// (gpio:18, power:2, term:row:col, rail:side:+/-:idx,
        /// comp:&lt;uuid&gt;:&lt;pin&gt;).</summary>
        public string fromPinId = "";
        public string toPinId = "";
        public Color colour = Color.red;
    }

    [Serializable]
    public class ProjectFile
    {
        public int schemaVersion = ProjectSchema.CurrentVersion;
        public string appVersion = Application.version;
        public string id = Guid.NewGuid().ToString();
        public string name = "Untitled 1";
        public long createdAtTicks;
        public long modifiedAtTicks;
        public string template = "blank";
        public string boardType = "RaspberryPi4B";
        public List<ProjectPart> parts = new List<ProjectPart>();
        public List<ProjectWire> wires = new List<ProjectWire>();
        /// <summary>Spatial anchor UUID, empty until placed.</summary>
        public string anchorUuid = "";

        public DateTime CreatedAt
        {
            get => new DateTime(createdAtTicks, DateTimeKind.Utc);
            set => createdAtTicks = value.Ticks;
        }

        public DateTime ModifiedAt
        {
            get => new DateTime(modifiedAtTicks, DateTimeKind.Utc);
            set => modifiedAtTicks = value.Ticks;
        }
    }

    [Serializable]
    public class ProjectIndexEntry
    {
        public string id = "";
        public string name = "";
        public long modifiedAtTicks;
        public string boardType = "";
        public int partCount;
    }

    [Serializable]
    public class ProjectIndexFile
    {
        public int schemaVersion = ProjectSchema.CurrentVersion;
        public List<ProjectIndexEntry> entries = new List<ProjectIndexEntry>();
    }
}
