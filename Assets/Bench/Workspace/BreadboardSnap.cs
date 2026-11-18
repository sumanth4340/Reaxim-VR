// BreadboardSnap.cs — magnetic snap onto a placed breadboard.
// Grid: 63 columns × 10 rows (top strip 5, bottom strip 5) mapped
// proportionally onto the placed model's own bounds, so any
// real-size breadboard model works. Logical net id follows the twin
// convention (letter a–j + 1-based column, e.g. "c12").
using UnityEngine;

namespace Bench.Workspace
{
    public static class BreadboardSnap
    {
        public static bool TrySnap(Vector3 worldPos, out Vector3 snapped,
            out string netId)
        {
            snapped = worldPos;
            netId = null;
            PlacedPart bestBoard = null;
            Bounds bestLocal = new Bounds();
            float bestD = float.MaxValue;
            foreach (var p in WorkspacePhysics.PartsSnapshot())
            {
                if (p == null || p.type != "Breadboard") continue;
                // Exact board-local bounds (rotation-safe): measure mesh
                // boxes through the board's own inverse matrix.
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
                if (!init) continue;
                var lp = p.transform.InverseTransformPoint(worldPos);
                float d = Vector3.Distance(lp, lb.center);
                if (d < bestD)
                {
                    bestD = d;
                    bestBoard = p;
                    bestLocal = lb;
                }
            }
            if (bestBoard == null) return false;
            var local = bestBoard.transform.InverseTransformPoint(worldPos);
            float sx = bestLocal.size.x;
            float sz = bestLocal.size.z;
            float lx = local.x - bestLocal.min.x;
            float lz = local.z - bestLocal.min.z;
            const float margin = 0.03f; // 3 cm magnetic radius
            if (lx < -margin || lx > sx + margin || lz < -margin || lz > sz + margin)
                return false;
            float pitchEff = sx / Twin.TwinBreadboard.Columns;
            int col = Mathf.Clamp(Mathf.FloorToInt(lx / pitchEff),
                0, Twin.TwinBreadboard.Columns - 1);
            // 10 terminal rows: 0-4 top strip, 5-9 bottom strip,
            // middle 20% is the channel (no holes).
            float topFrac = 0.4f, chanFrac = 0.2f;
            int row;
            float rz0, rz1;
            if (lz < topFrac * sz)
            {
                row = Mathf.Clamp((int)(lz / (topFrac * sz) * 5f), 0, 4);
                rz0 = topFrac * sz * row / 5f;
                rz1 = topFrac * sz * (row + 1) / 5f;
            }
            else if (lz > (topFrac + chanFrac) * sz)
            {
                float fz = lz - (topFrac + chanFrac) * sz;
                row = 5 + Mathf.Clamp((int)(fz / (topFrac * sz) * 5f), 0, 4);
                rz0 = (topFrac + chanFrac) * sz + topFrac * sz * (row - 5) / 5f;
                rz1 = (topFrac + chanFrac) * sz + topFrac * sz * (row - 4) / 5f;
            }
            else
            {
                // In the channel: snap to the nearest strip edge.
                if (lz - topFrac * sz < (topFrac + chanFrac) * sz - lz)
                {
                    row = 4;
                    rz0 = topFrac * sz * 4 / 5f;
                    rz1 = topFrac * sz;
                }
                else
                {
                    row = 5;
                    rz0 = (topFrac + chanFrac) * sz;
                    rz1 = (topFrac + chanFrac) * sz + topFrac * sz / 5f;
                }
            }
            float colX = bestLocal.min.x + (col + 0.5f) * pitchEff;
            float rowZ = bestLocal.min.z + (rz0 + rz1) / 2f;
            float topY = bestLocal.max.y + 0.002f;
            snapped = bestBoard.transform.TransformPoint(
                new Vector3(colX, topY, rowZ));
            netId = Twin.TwinBreadboard.NetId(row, col);
            return true;
        }

    }
}
