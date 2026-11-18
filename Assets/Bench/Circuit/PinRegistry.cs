// PinRegistry.cs — addressable pins for Pin Mode (Task 7).
// Board header pins come from TwinCatalog (physical/BCM/alt);
// positions map twin millimetres onto the placed model's own bounds
// (unrotated-board assumption, documented). Component pins come from
// catalog pinTags, laid out on model edges. Breadboard holes resolve
// on demand (TryHole) to keep the registry small.
using System.Collections.Generic;
using UnityEngine;
using Bench.Twin;
using Bench.Workspace;

namespace Bench.Circuit
{
    public enum PinRole { Power5V, Power3V3, Ground, Gpio, I2c, Spi, Uart, Other }

    public class PinDef
    {
        public string node;      // twin vocabulary id
        public PlacedPart part;  // null for breadboard holes
        public PlacedPart board; // breadboard owner for holes
        public string label;
        public string sub;       // alt function / net hint
        public PinRole role;
        public Vector3 localPos; // in part space (or board space)
        public Vector3 WorldPos => part != null
            ? part.transform.TransformPoint(localPos)
            : (board != null ? board.transform.TransformPoint(localPos)
                : localPos);
    }

    public static class PinRegistry
    {
        public static readonly List<PinDef> Pins = new List<PinDef>();

        public static void Rebuild()
        {
            Pins.Clear();
            foreach (var p in WorkspacePhysics.PartsSnapshot())
            {
                if (p == null) continue;
                if (p.isBoard) AddBoardPins(p);
                else if (p.type == "Breadboard") { /* holes on demand */ }
                else AddComponentPins(p);
            }
        }

        private static Bounds LocalBounds(Transform root)
        {
            var b = new Bounds();
            bool init = false;
            var inv = root.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = inv * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 q = m.MultiplyPoint3x4(mb.center + new Vector3(
                        ((i & 1) == 0 ? -1f : 1f) * mb.extents.x,
                        ((i & 2) == 0 ? -1f : 1f) * mb.extents.y,
                        ((i & 4) == 0 ? -1f : 1f) * mb.extents.z));
                    if (!init) { b = new Bounds(q, Vector3.zero); init = true; }
                    else b.Encapsulate(q);
                }
            }
            return b;
        }

        private static void AddBoardPins(PlacedPart board)
        {
            var b = LocalBounds(board.transform);
            if (b.size.x <= 0f || b.size.z <= 0f) return;
            // Twin header geometry (BoardGeometry): pin1 x=7.5mm,
            // odd row z=3.6mm, even row z=6.14mm, pitch 2.54mm,
            // board 85.6 x 56.5mm. Map proportionally onto the model.
            foreach (var tp in TwinCatalog.Header40)
            {
                bool odd = tp.physical % 2 == 1;
                int idx = (tp.physical - (odd ? 1 : 2)) / 2;
                float xmm = 7.5f + idx * 2.54f;
                float zmm = odd ? 3.6f : 6.14f;
                var local = new Vector3(
                    b.min.x + xmm / 85.6f * b.size.x,
                    b.max.y,
                    b.min.z + zmm / 56.5f * b.size.z);
                string node = tp.kind == TwinPinKind.Gpio
                    ? "gpio:" + tp.bcm : "power:" + tp.physical;
                Pins.Add(new PinDef
                {
                    node = node,
                    part = board,
                    label = tp.name,
                    sub = tp.alt,
                    role = RoleFor(tp),
                    localPos = local,
                });
            }
        }

        private static PinRole RoleFor(TwinPin p)
        {
            switch (p.kind)
            {
                case TwinPinKind.Power5V: return PinRole.Power5V;
                case TwinPinKind.Power3V3: return PinRole.Power3V3;
                case TwinPinKind.Ground: return PinRole.Ground;
            }
            if (p.alt != null)
            {
                if (p.alt.StartsWith("I2C")) return PinRole.I2c;
                if (p.alt.StartsWith("SPI")) return PinRole.Spi;
                if (p.alt.StartsWith("UART")) return PinRole.Uart;
            }
            return PinRole.Gpio;
        }

        private static void AddComponentPins(PlacedPart part)
        {
            TwinComponent? found = null;
            foreach (var c in TwinCatalog.Components)
                if (c.kind == part.type) { found = c; break; }
            if (found == null || found.Value.pinTags.Length == 0) return;
            var tags = found.Value.pinTags;
            var b = LocalBounds(part.transform);
            if (b.size.x <= 0f) return;
            int n = tags.Length;
            for (int i = 0; i < n; i++)
            {
                Vector3 local;
                if (n <= 4)
                {
                    float x = b.min.x + (i + 0.5f) / n * b.size.x;
                    local = new Vector3(x, b.max.y, b.center.z);
                }
                else // DIP: two rows (MCP3008: 14 → 7+7)
                {
                    int per = n / 2;
                    int r = i < per ? 0 : 1;
                    int k = i < per ? i : i - per;
                    float x = b.min.x + (k + 0.5f) / per * b.size.x;
                    float z = r == 0 ? b.min.z + b.size.z * 0.15f
                        : b.min.z + b.size.z * 0.85f;
                    local = new Vector3(x, b.max.y, z);
                }
                Pins.Add(new PinDef
                {
                    node = "comp:" + part.partId + ":" + i,
                    part = part,
                    label = part.displayName + " " + tags[i],
                    sub = tags[i],
                    role = PinRole.Other,
                    localPos = local,
                });
            }
        }

        /// <summary>Nearest breadboard hole within 1.5 cm, else null.</summary>
        public static PinDef TryHole(Vector3 worldPos)
        {
            PinDef best = null;
            float bestD = 0.015f;
            foreach (var p in WorkspacePhysics.PartsSnapshot())
            {
                if (p == null || p.type != "Breadboard") continue;
                var inv = p.transform.worldToLocalMatrix;
                var lb = new Bounds();
                bool init = false;
                foreach (var mf in p.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mb = mf.sharedMesh.bounds;
                    var m = inv * mf.transform.localToWorldMatrix;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 q = m.MultiplyPoint3x4(mb.center + new Vector3(
                            ((i & 1) == 0 ? -1f : 1f) * mb.extents.x,
                            ((i & 2) == 0 ? -1f : 1f) * mb.extents.y,
                            ((i & 4) == 0 ? -1f : 1f) * mb.extents.z));
                        if (!init) { lb = new Bounds(q, Vector3.zero); init = true; }
                        else lb.Encapsulate(q);
                    }
                }
                if (!init || lb.size.x <= 0f) continue;
                var lp = p.transform.InverseTransformPoint(worldPos);
                float px = (lp.x - lb.min.x) / lb.size.x
                    * TwinBreadboard.Columns;
                float pz = (lp.z - lb.min.z) / lb.size.z
                    * TwinBreadboard.Rows;
                int col = Mathf.Clamp(Mathf.FloorToInt(px), 0,
                    TwinBreadboard.Columns - 1);
                int row = Mathf.Clamp(Mathf.FloorToInt(pz), 0,
                    TwinBreadboard.Rows - 1);
                var hole = new Vector3(
                    lb.min.x + (col + 0.5f) / TwinBreadboard.Columns * lb.size.x,
                    lb.max.y,
                    lb.min.z + (row + 0.5f) / TwinBreadboard.Rows * lb.size.z);
                float d = Vector3.Distance(
                    p.transform.TransformPoint(hole), worldPos);
                if (d < bestD)
                {
                    bestD = d;
                    best = new PinDef
                    {
                        node = "term:" + row + ":" + col,
                        board = p,
                        label = TwinBreadboard.NetId(row, col),
                        sub = "breadboard",
                        role = PinRole.Other,
                        localPos = hole,
                    };
                }
            }
            return best;
        }

        public static PinDef Nearest(Vector3 worldPos, float maxDist,
            System.Func<PinDef, bool> filter = null)
        {
            PinDef best = null;
            float bestD = maxDist;
            foreach (var pin in Pins)
            {
                if (filter != null && !filter(pin)) continue;
                float d = Vector3.Distance(pin.WorldPos, worldPos);
                if (d < bestD) { bestD = d; best = pin; }
            }
            return best;
        }
    }
}
