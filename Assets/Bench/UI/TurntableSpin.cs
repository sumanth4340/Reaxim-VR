// TurntableSpin.cs — slow turntable rotation for concept 3D cards,
// plus optional glTFast .glb loading (project-relative glbPath).
using UnityEngine;

public class TurntableSpin : MonoBehaviour
{
    public Transform target;
    [Tooltip("Degrees per second.")]
    public float speed = 24f;
    [Tooltip("Project-relative .glb to load, e.g. Assets/Models/LED.glb")]
    public string glbPath;
    [Tooltip("Skip .glb load (procedural model already parented).")]
    public bool skipLoad = false;
    [Tooltip("Largest model extent after auto-fit, in metres.")]
    public float fitSize = 0.06f;
    [Tooltip("Viewport rect (px) the model centres on.")]
    public Vector2 viewportSizePx = new Vector2(90f, 72f);
    public System.Text.StringBuilder Log;

    private void LogLine(string s)
    {
        if (Log != null) Log.AppendLine(s);
        else Debug.Log("[Turntable] " + s);
    }

    private bool _loadStarted;

    private async void Start()
    {
        await LoadNow();
    }

    /// <summary>Load + fit the .glb now (also callable for
    /// inactive objects, e.g. from a startup preloader).</summary>
    public async System.Threading.Tasks.Task LoadNow()
    {
        if (_loadStarted) return;
        _loadStarted = true;
        if (target == null) target = transform;
        if (!skipLoad && !string.IsNullOrEmpty(glbPath))
            await LoadGlb();
    }

    private async System.Threading.Tasks.Task LoadGlb()
    {
        try
        {
            string abs = System.IO.Path.Combine(Application.dataPath,
                glbPath.StartsWith("Assets/")
                    ? glbPath.Substring("Assets/".Length)
                    : glbPath);
            if (!System.IO.File.Exists(abs))
            {
                LogLine("turntable missing glb: " + glbPath);
                return;
            }
            var import = new GLTFast.GltfImport();
            bool ok = await import.Load("file://" + abs);
            if (!ok)
            {
                LogLine("turntable glb load failed: " + glbPath);
                return;
            }
            // Pivot at the viewport centre: Update spins this, so the
            // fitted model rotates in place with no orbit.
            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(target, false);
            pivot.transform.localPosition = new Vector3(
                viewportSizePx.x / 2f, -viewportSizePx.y / 2f, 0f);
            target = pivot.transform;
            var holder = new GameObject("Model");
            holder.transform.SetParent(pivot.transform, false);
            await import.InstantiateMainSceneAsync(holder.transform);
            Fit(holder.transform, pivot.transform.position);
            LogLine("turntable loaded: " + glbPath);
        }
        catch (System.Exception e)
        {
            LogLine("turntable error " + glbPath + ": " + e.Message);
        }
    }

    private void Fit(Transform holder, Vector3 centreWorld)
    {
        Bounds b;
        if (!Bench.UI.ModelScale.Measure(holder, out b)) return;
        // Tip flat PCBs back so a front camera sees the top, then
        // fit: proportions change once standing.
        if (b.size.y < 0.3f * Mathf.Max(b.size.x, b.size.z))
            holder.localRotation = Quaternion.Euler(-55f, 0f, 0f);
        if (Bench.UI.ModelScale.FitToSize(holder, fitSize, centreWorld,
            tipFlat: false))
            LogLine("fit size=" + fitSize);
    }

    private void Update()
    {
        if (target != null) target.Rotate(Vector3.up, speed * Time.deltaTime,
            Space.Self);
    }
}
