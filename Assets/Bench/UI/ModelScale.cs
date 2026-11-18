// ModelScale.cs — mesh-space measure + fit shared by the tile
// builder (edit mode), TurntableSpin (GLB cards) and placed-part
// real sizing (runtime).
using UnityEngine;

namespace Bench.UI
{
    public static class ModelScale
    {
        /// <summary>Exact world bounds from mesh data. Renderer.bounds
        /// can lag transforms by a frame and misplaces fits.</summary>
        public static bool Measure(Transform holder, out Bounds b)
        {
            b = new Bounds();
            bool init = false;
            var filters = holder.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in filters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                Accumulate(mf.sharedMesh.bounds,
                    mf.transform.localToWorldMatrix, ref b, ref init);
            }
            var skinned = holder.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in skinned)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                Accumulate(smr.sharedMesh.bounds,
                    smr.transform.localToWorldMatrix, ref b, ref init);
            }
            return init;
        }

        private static void Accumulate(Bounds mb, Matrix4x4 m,
            ref Bounds b, ref bool init)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + new Vector3(
                    ((i & 1) == 0 ? -1f : 1f) * mb.extents.x,
                    ((i & 2) == 0 ? -1f : 1f) * mb.extents.y,
                    ((i & 4) == 0 ? -1f : 1f) * mb.extents.z);
                Vector3 p = m.MultiplyPoint3x4(c);
                if (!init) { b = new Bounds(p, Vector3.zero); init = true; }
                else b.Encapsulate(p);
            }
        }

        public static float MaxExtent(Bounds b) =>
            Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));

        /// <summary>Largest world extent of a hierarchy, or 0 when
        /// it has no meshes.</summary>
        public static float MaxExtent(Transform holder)
        {
            Bounds b;
            if (holder == null || !Measure(holder, out b)) return 0f;
            return MaxExtent(b);
        }

        /// <summary>Scale holder so its max extent is size metres,
        /// centred on centreWorld. Flat PCBs tip back so a front
        /// camera sees the top. False when nothing measurable.</summary>
        public static bool FitToSize(Transform holder, float size,
            Vector3 centreWorld, bool tipFlat = true)
        {
            if (holder == null || size <= 0f) return false;
            Bounds b;
            if (!Measure(holder, out b)) return false;
            float max = MaxExtent(b);
            if (max <= 0f) return false;
            if (tipFlat && b.size.y < 0.3f * Mathf.Max(b.size.x, b.size.z))
                holder.localRotation = Quaternion.Euler(-55f, 0f, 0f);
            if (!Measure(holder, out b)) return false;
            max = MaxExtent(b);
            if (max <= 0f) return false;
            holder.localScale = holder.localScale * (size / max);
            if (!Measure(holder, out b)) return false;
            holder.position += centreWorld - b.center;
            return true;
        }
    }
}
