// BenchRoot.cs — composition root for the MR bench UI. Builds the
// room backdrop (Editor/simulator preview; passthrough replaces it
// on device), the UI stage, the input module and all four screens,
// then runs the mode flow from the concepts:
//   Home → (open project) → Workbench ⇄ Testing, Library overlay.
// Screens live in fixed slots (left dock / top pill / right rail /
// bottom pill / center overlay) so windows never overlap.

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BenchRoot : MonoBehaviour
{
    public static BenchRoot Instance { get; private set; }

    [Tooltip("Show the virtual room (Editor/sim). Disable on device" +
             " where passthrough is the backdrop.")]
    public bool showBackdrop = true;

    TwinMirror mirror;
    HomeScreen home;
    LibraryScreen library;
    ProjectPanel dock;
    WorkbenchUI workbench;
    TestingUI testing;

    GameObject uiRoot, backdrop;
    TextMeshProUGUI bannerText;
    RectTransform banner;
    BenchMode mode = BenchMode.Home;
    bool libraryOpen;

    // ── boot ─────────────────────────────────────────────────────
    void Awake()
    {
        Instance = this;
        BenchUI.EnsureFonts();
        // Verification (and background automation) needs the player
        // loop to keep ticking when the Editor is unfocused — with
        // runInBackground off, the UI follow freezes mid-lerp and
        // the mock twin stops animating the moment focus is lost.
        Application.runInBackground = true;
        uiRoot = new GameObject("UIRoot");
        uiRoot.transform.SetParent(transform, false);

        // mirror + mock on a dedicated object (Desk parent set first)
        var mirrorGo = new GameObject("TwinMirror");
        mirrorGo.transform.SetParent(transform, false);
        mirrorGo.SetActive(false);
        var desk = new GameObject("Desk");
        desk.transform.SetParent(mirrorGo.transform, false);
        desk.transform.localPosition = new Vector3(0, 0.72f, 0.72f);
        mirror = mirrorGo.AddComponent<TwinMirror>();
        mirror.Desk = desk.transform;
        mirrorGo.AddComponent<TwinMockSource>();
        mirror.Bridge = FindObjectOfType<TwinBridge>();
        mirrorGo.SetActive(true);

        // input: EventSystem with the gaze-pinch module (replaces any
        // default StandaloneInputModule so pinch/mouse both route here)
        var esGo = new GameObject("EventSystem",
            typeof(EventSystem), typeof(GazePinchInputModule));
        esGo.transform.SetParent(transform, false);
        var standalone = esGo.GetComponent<StandaloneInputModule>();
        if (standalone != null) Destroy(standalone);

        BuildBackdrop();
        BuildScreens();
        BuildBanner();
        Invoke(nameof(ShowHome), 0.05f);
    }

    void Start()
    {
        // Anchor the UI stage at head height (the concepts are drawn
        // from a seated user's point of view; the rig/tracking space
        // owns the horizontal origin).
        var cam = Camera.main;
        if (cam != null && uiRoot != null)
            uiRoot.transform.position = new Vector3(0f,
                cam.transform.position.y, 0f);
    }

    void BuildScreens()
    {
        home = CreateScreen<HomeScreen>(uiRoot.transform);
        home.Build(uiRoot.transform);
        home.OnOpenProject += ShowWorkbench;
        home.OnProjectPicked += _ => ShowWorkbench();

        dock = CreateScreen<ProjectPanel>(uiRoot.transform);
        dock.Build(uiRoot.transform);
        dock.OnTab += DockTab;
        dock.OnInstrument += _ => { };

        workbench = CreateScreen<WorkbenchUI>(uiRoot.transform);
        workbench.Build(uiRoot.transform, mirror);
        workbench.OnExit += ShowHome;

        testing = CreateScreen<TestingUI>(uiRoot.transform);
        testing.Build(uiRoot.transform, mirror);
        testing.OnPinMode += _ => ShowWorkbench();

        library = CreateScreen<LibraryScreen>(uiRoot.transform);
        library.Build(uiRoot.transform);
        library.OnTab += DockTab;
        library.OnAddPart += kind =>
        {
            mirror.AddPart(kind);
            FlashBanner(kind + " added to the bench");
        };

        mirror.OnSnapshotApplied += () =>
        {
            if (dock != null) dock.RefreshParts(mirror);
        };
    }

    static T CreateScreen<T>(Transform parent) where T : Component
    {
        var go = new GameObject(typeof(T).Name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<T>();
    }

    // ── mode flow ────────────────────────────────────────────────
    /// Scriptable/shortcut mode switch: "home" | "workbench" |
    /// "testing" | "boards" | "components" (library tabs).
    public void Show(string what) => (what switch
    {
        "home" => (System.Action)ShowHome,
        "testing" => ShowTesting,
        "boards" => () => DockTab("Boards"),
        "components" => () => DockTab("Components"),
        _ => ShowWorkbench,
    }).Invoke();

    void ShowHome()
    {
        mode = BenchMode.Home;
        libraryOpen = false;
        home.Show(true);
        dock.Show(false);
        workbench.Show(false);
        testing.Show(false);
        library.Show(false);
    }

    void ShowWorkbench()
    {
        mode = BenchMode.Workbench;
        libraryOpen = false;
        home.Show(false);
        dock.Show(true);
        dock.SetMode(BenchMode.Workbench);
        dock.SetActiveTab("Boards");
        workbench.Show(true);
        testing.Show(false);
        library.Show(false);
    }

    void ShowTesting()
    {
        mode = BenchMode.Testing;
        libraryOpen = false;
        home.Show(false);
        dock.Show(true);
        dock.SetMode(BenchMode.Testing);
        dock.SetActiveTab("Testing");
        workbench.Show(false);
        testing.Show(true);
        library.Show(false);
    }

    void DockTab(string tab)
    {
        if (tab == "Testing") { ShowTesting(); return; }
        if (mode == BenchMode.Home) ShowWorkbench();
        mode = BenchMode.Workbench;
        dock.SetMode(BenchMode.Workbench);
        dock.SetActiveTab(tab);
        libraryOpen = true;
        home.Show(false);
        workbench.Show(true);
        testing.Show(false);
        library.Show(true);
        library.SetTab(tab);
    }

    // ── banner (link state) ──────────────────────────────────────
    void BuildBanner()
    {
        var canvas = BenchUI.WorldCanvas(uiRoot.transform, "Banner",
            40, 20, 330, 46);
        banner = BenchUI.Panel(canvas.transform, "pill", 0, 0, 330, 46,
            dark: true, radius: 23);
        banner.anchoredPosition = new Vector2(0, 0);
        bannerText = BenchUI.Label(banner, "t", "connecting…", 18, 13,
            300, 14, BenchTheme.TextSubtle);
    }

    float flashUntil;
    void FlashBanner(string msg)
    {
        flashUntil = Time.time + 2.5f;
        bannerText.text = msg;
        bannerText.color = BenchTheme.CyanLight;
    }

    void Update()
    {
        // The UI stage rides the head (tracking space decides the
        // height — the simulator stands at 1.66 m, the Editor sits
        // at 0): follow the camera pose smoothly so the concept
        // layout is always framed in front of the user. The desk
        // rides along: ~95 cm below the eyes, ~45 cm ahead.
        var head = Camera.main;
        if (head != null && uiRoot != null)
        {
            Vector3 target = head.transform.position;
            float yaw = head.transform.eulerAngles.y;
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            // snap when far (editor resume / teleports), ease when close
            Vector3 pos = Vector3.Distance(uiRoot.transform.position,
                target) > 0.3f
                ? target
                : Vector3.Lerp(uiRoot.transform.position, target,
                    1f - Mathf.Exp(-12f * Time.deltaTime));
            uiRoot.transform.SetPositionAndRotation(pos,
                Quaternion.Euler(0f, yaw, 0f));
            if (mirror != null && mirror.Desk != null)
                mirror.Desk.position = head.transform.position
                    + fwd * 0.5f + Vector3.down * 0.5f;
        }

        // bench hotkeys (also used for Editor/simulator verification):
        // F5 home · F6 workbench · F7 testing · F8 components · F9 shot
        if (Input.GetKeyDown(KeyCode.F5)) Show("home");
        if (Input.GetKeyDown(KeyCode.F6)) Show("workbench");
        if (Input.GetKeyDown(KeyCode.F7)) Show("testing");
        if (Input.GetKeyDown(KeyCode.F8)) Show("components");
        if (Input.GetKeyDown(KeyCode.F9))
        {
            var name = mode.ToString().ToLower() +
                       (libraryOpen ? "_library" : "");
            CaptureBench("/tmp/bench_shots/cap_" + name + ".png");
        }
        if (Time.time < flashUntil) return;
        bool live = mirror != null && mirror.Live;
        string txt = live
            ? "<color=#4ADE80>●</color> Live · " + (mirror.Powered ? "powered" : "halted") +
              " · " + mirror.Phase
            : "<color=#FFB454>●</color> Mock twin · start the Swift bridge for live data";
        if (bannerText != null && bannerText.text != txt)
        {
            bannerText.text = txt;
            bannerText.color = BenchTheme.Text;
        }
    }

    // ── capture helper (F9) ───────────────────────────────────────
    /// Render the head view to PNG (camera pitch leans down over
    /// the desk so the board is in frame for workbench/testing).
    public void CaptureBench(string path)
    {
        var main = Camera.main;
        if (main == null) { Debug.LogWarning("[BenchRoot] no camera"); return; }
        const int w = 1600, h = 900;
        var go = new GameObject("BenchCap");
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(main);
        bool deskView = mode == BenchMode.Workbench || mode == BenchMode.Testing;
        cam.transform.position = main.transform.position;
        // pitch to frame the desk when showing the bench; the desk
        // rides below the head, so derive the angle from geometry.
        // Straight-ahead framing keeps the whole 1280×720 stage
        // (±29.5° at 1.15 m) inside a 70° capture FOV.
        float pitch = 0f;
        if (deskView && mirror != null && mirror.Desk != null)
        {
            var d = mirror.Desk.position;
            float dy = main.transform.position.y - d.y;
            float dist = Mathf.Max(0.25f,
                Vector2.Distance(new Vector2(main.transform.position.x,
                    main.transform.position.z), new Vector2(d.x, d.z)));
            pitch = Mathf.Clamp(Mathf.Atan2(dy, dist) * Mathf.Rad2Deg
                * 0.55f, 0f, 45f);
        }
        cam.transform.rotation = Quaternion.Euler(
            pitch, main.transform.eulerAngles.y, 0);
        cam.fieldOfView = 70f;
        cam.enabled = false;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        // Manual Render() under an active XR display breaks lit
        // materials (they come out error-magenta); stop the XR
        // displays for a mono capture and restore after.
        var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
        UnityEngine.SubsystemManager.GetSubsystems(displays);
        foreach (var d in displays) d.Stop();
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        try
        {
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        }
        finally
        {
            foreach (var d in displays) d.Start();
            cam.targetTexture = null;
            Destroy(tex);
            Destroy(rt);
            Destroy(go);
        }
        Debug.Log("[BenchRoot] captured " + path);
    }

    // ── backdrop (concept room, Editor/sim only) ─────────────────
    void BuildBackdrop()
    {
        backdrop = new GameObject("Backdrop");
        backdrop.transform.SetParent(transform, false);
        backdrop.SetActive(showBackdrop);

        // room gradient quad behind the UI plane (1280×720 stage size)
        var room = MakeQuad("Room", BenchTheme.StageW * BenchTheme.Px * 2.6f,
            BenchTheme.StageH * BenchTheme.Px * 2.6f, RoomTexture());
        room.transform.SetParent(backdrop.transform, false);
        room.transform.position = new Vector3(0, 0.15f, 2.6f);

        // the concepts dim the whole room behind the glass with a
        // rgba(28,38,54,.4) overlay — without it the scene reads
        // bright/washed-out next to the mockups
        var dim = MakeQuad("DimOverlay",
            BenchTheme.StageW * BenchTheme.Px * 2.6f,
            BenchTheme.StageH * BenchTheme.Px * 2.6f,
            SolidTex(new Color(28f / 255f, 38f / 255f, 54f / 255f, 0.40f)));
        dim.transform.SetParent(backdrop.transform, false);
        dim.transform.position = new Vector3(0, 0.15f, 2.55f);

        // desk surface under the board
        var desk = MakeQuad("DeskTop", 1.3f, 0.9f,
            SolidTex(BenchTheme.RoomDesk * 0.8f));
        desk.transform.SetParent(backdrop.transform, false);
        desk.transform.rotation = Quaternion.Euler(90, 0, 0);
        desk.transform.position = new Vector3(0, 0.72f, 0.72f);
        var desk2 = MakeQuad("DeskFront", 1.3f, 0.4f,
            SolidTex(BenchTheme.RoomBottom));
        desk2.transform.SetParent(backdrop.transform, false);
        desk2.transform.position = new Vector3(0, 0.52f, 1.16f);

        // warm lamp glow (concept: #ffd9a0 blob top-left)
        var lamp = MakeQuad("Lamp", 0.55f, 0.55f, GlowTex(BenchTheme.LampGlow));
        lamp.transform.SetParent(backdrop.transform, false);
        lamp.transform.position = new Vector3(-0.85f, 0.5f, 2.55f);
    }

    static GameObject MakeQuad(string name, float w, float h, Texture2D tex)
    {
        var go = new GameObject(name);
        var filter = go.AddComponent<MeshFilter>();
        var rend = go.AddComponent<MeshRenderer>();
        var m = new Mesh();
        m.SetVertices(new System.Collections.Generic.List<Vector3> {
            new(-w/2, -h/2, 0), new(w/2, -h/2, 0),
            new(-w/2, h/2, 0), new(w/2, h/2, 0) });
        m.SetUVs(0, new System.Collections.Generic.List<Vector2> {
            new(0, 0), new(1, 0), new(0, 1), new(1, 1) });
        m.SetTriangles(new System.Collections.Generic.List<int>
            { 0, 1, 2, 2, 1, 3 }, 0);
        m.RecalculateNormals();
        filter.sharedMesh = m;
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        rend.sharedMaterial = mat;
        return go;
    }

    static Texture2D SolidTex(Color c)
    {
        var t = new Texture2D(4, 4);
        for (int i = 0; i < 16; i++) t.SetPixel(i % 4, i / 4, c);
        t.Apply();
        return t;
    }

    /// 4-stop room gradient (concept: #6d8199 → #4a5a6d →
    /// #3b3128 → #241d17, wall→desk split at 52%).
    static Texture2D RoomTexture()
    {
        const int h = 256;
        var t = new Texture2D(4, h);
        for (int y = 0; y < h; y++)
        {
            float f = y / (float)(h - 1);
            Color c = f < 0.52f
                ? Color.Lerp(BenchTheme.RoomTop, BenchTheme.RoomMid,
                    f / 0.52f)
                : f < 0.62f
                    ? Color.Lerp(BenchTheme.RoomMid, BenchTheme.RoomDesk,
                        (f - 0.52f) / 0.10f)
                    : Color.Lerp(BenchTheme.RoomDesk, BenchTheme.RoomBottom,
                        (f - 0.62f) / 0.38f);
            for (int x = 0; x < 4; x++) t.SetPixel(x, y, c);
        }
        t.Apply();
        return t;
    }

    static Texture2D GlowTex(Color c)
    {
        const int n = 64;
        var t = new Texture2D(n, n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y),
                    Vector2.one * (n / 2f)) / (n / 2f);
                t.SetPixel(x, y, new Color(c.r, c.g, c.b,
                    0.55f * Mathf.Clamp01(1 - d)));
            }
        t.Apply();
        return t;
    }
}
