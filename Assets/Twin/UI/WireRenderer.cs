// WireRenderer.cs — pin-to-pin wires, drawn like the concept: a
// cyan cubic-bezier tube sagging between two pins, white endpoint
// rings, pulsing green dash rings on valid targets while wiring.
// The twin drives colour/shape; wires are pinch-removable.

using System.Collections.Generic;
using UnityEngine;

public class WireRenderer : MonoBehaviour
{
    public string wireId;          // twin wire id (removeWire)
    public string nodeA, nodeB;   // "gpio:18"…

    MeshFilter filter;
    MeshRenderer rend;
    readonly List<GameObject> endpointRings = new();
    float pulse;

    const int Rings = 24;    // tube segments
    const int Sides = 8;     // tube sides

    /// World-space radius of the wire (concept stroke ≈ 5 px).
    const float WireRadiusM = 0.0025f;

    public void Init(Vector3 a, Vector3 b, Color c)
    {
        gameObject.name = "wire_" + wireId;
        filter = gameObject.AddComponent<MeshFilter>();
        rend = gameObject.AddComponent<MeshRenderer>();
        rend.sharedMaterial = PartModels.GlowMat(c);
        BuildTube(a, b);

        // white endpoint rings (concept: r17 white stroke circles)
        AddRing(a, BenchTheme.Text, 0.017f);
        AddRing(b, BenchTheme.Text, 0.017f);

        // pinch target: invisible collider chain along the sag
        var mid = Bezier(a, b, 0.5f) + Vector3.up * 0.01f;
        var cap = new GameObject("WireGrab");
        cap.transform.SetParent(transform, false);
        cap.transform.position = mid;
        var col = cap.AddComponent<BoxCollider>();
        col.size = new Vector3(
            Vector3.Distance(a, b) * 0.5f, 0.02f, 0.02f);
        cap.transform.rotation = Quaternion.LookRotation(
            (b - a).normalized == Vector3.zero ? Vector3.up : (b - a).normalized);
        var grab = cap.AddComponent<WireGrab>();
        grab.wire = this;
        // aim the collider's length along the wire
        cap.transform.rotation = Quaternion.LookRotation(b - a) *
                                  Quaternion.Euler(0, 90, 0);
    }

    void AddRing(Vector3 at, Color c, float radius)
    {
        var go = new GameObject("ring");
        go.transform.SetParent(transform, false);
        go.transform.position = at;
        var q = go.AddComponent<Quad>();
        q.Init(Art.Ring(c), radius * 2f);
        endpointRings.Add(go);
    }

    // ── mesh ──────────────────────────────────────────────────────
    void BuildTube(Vector3 a, Vector3 b)
    {
        var mesh = new Mesh { name = "wireTube" };
        var verts = new Vector3[Rings * Sides];
        var norms = new Vector3[Rings * Sides];
        var tris = new List<int>();

        // per-ring orthonormal frame around the bezier
        Vector3 prevT = Vector3.up;
        for (int i = 0; i < Rings; i++)
        {
            float t = i / (float)(Rings - 1);
            Vector3 p = Bezier(a, b, t);
            Vector3 tangent = BezierTangent(a, b, t);
            Vector3 side = Vector3.Cross(tangent, prevT);
            if (side.sqrMagnitude < 1e-6f)
                side = Vector3.Cross(tangent, Vector3.up);
            side.Normalize();
            Vector3 up2 = Vector3.Cross(side, tangent).normalized;
            prevT = tangent;
            for (int s = 0; s < Sides; s++)
            {
                float ang = s / (float)Sides * Mathf.PI * 2f;
                Vector3 o = (Mathf.Cos(ang) * side + Mathf.Sin(ang) * up2)
                            * WireRadiusM;
                verts[i * Sides + s] = p + o;
                norms[i * Sides + s] = o.normalized;
            }
        }
        for (int i = 0; i < Rings - 1; i++)
            for (int s = 0; s < Sides; s++)
            {
                int s2 = (s + 1) % Sides;
                int v0 = i * Sides + s, v1 = i * Sides + s2,
                    v2 = (i + 1) * Sides + s, v3 = (i + 1) * Sides + s2;
                tris.AddRange(new[] { v0, v1, v2, v1, v3, v2 });
            }
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        filter.sharedMesh = mesh;
    }

    /// Cubic bezier with a gentle upward sag (concept path
    /// C 950,338 950,420 …): control points above the endpoints.
    public static Vector3 Bezier(Vector3 a, Vector3 b, float t)
    {
        float dist = Vector3.Distance(a, b);
        Vector3 c1 = a + Vector3.up * (0.02f + dist * 0.12f);
        Vector3 c2 = b + Vector3.up * (0.02f + dist * 0.12f);
        float u = 1f - t;
        return u * u * u * a + 3f * u * u * t * c1
             + 3f * u * t * t * c2 + t * t * t * b;
    }

    static Vector3 BezierTangent(Vector3 a, Vector3 b, float t)
    {
        float dist = Vector3.Distance(a, b);
        Vector3 c1 = a + Vector3.up * (0.02f + dist * 0.12f);
        Vector3 c2 = b + Vector3.up * (0.02f + dist * 0.12f);
        float u = 1f - t;
        return (3f * u * u * (c1 - a) + 6f * u * t * (c2 - c1)
                + 3f * t * t * (b - c2)).normalized;
    }

    void LateUpdate()
    {
        // endpoint rings billboard toward the viewer
        if (Camera.main == null) return;
        foreach (var r in endpointRings)
            if (r != null)
                r.transform.rotation = Quaternion.LookRotation(
                    r.transform.position - Camera.main.transform.position);
    }
}

/// Pinch a wire to remove it (concept: pinch gestures edit wiring).
public class WireGrab : MonoBehaviour
{
    public WireRenderer wire;
}

/// Camera-facing textured quad (rings, glows) in world space.
public class Quad : MonoBehaviour
{
    public void Init(Sprite sprite, float worldSize)
    {
        var filter = gameObject.AddComponent<MeshFilter>();
        var rend = gameObject.AddComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", sprite.texture);
        if (mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", sprite.texture);
        rend.sharedMaterial = mat;

        // camera-facing quad mesh (unit size, scaled)
        var m = new Mesh { name = "quad" };
        m.SetVertices(new List<Vector3> {
            new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0),
            new(-0.5f, 0.5f, 0), new(0.5f, 0.5f, 0) });
        m.SetUVs(0, new List<Vector2> {
            new(0, 0), new(1, 0), new(0, 1), new(1, 1) });
        m.SetTriangles(new List<int> { 0, 1, 2, 2, 1, 3 }, 0);
        m.RecalculateNormals();
        filter.sharedMesh = m;
        transform.localScale = Vector3.one * worldSize;
    }
}
