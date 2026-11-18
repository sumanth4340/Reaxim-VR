// BenchSceneSetup.cs — one-shot Editor setup for the MR Bench UI.
// Menu: Twin ▸ Setup Bench UI (also runnable headless:
//   Unity -batchmode -executeMethod BenchSceneSetup.Run -quit)
//
// Adds the BenchRoot composition object to the open scene (it
// self-builds everything else at Play: screens, input, backdrop),
// bakes the Arial TMP font assets into Resources for device builds,
// and reports what it found (rig, bridge). Idempotent.

using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BenchSceneSetup
{
    const string FontsDir = "Assets/Twin/UI/Fonts/Resources";

    [MenuItem("Twin/Setup Bench UI")]
    public static void Run()
    {
        BakeFonts();
        var root = Object.FindObjectOfType<BenchRoot>();
        if (root == null)
        {
            var go = new GameObject("BenchRoot");
            go.AddComponent<BenchRoot>();
            Undo.RegisterCreatedObjectUndo(go, "Setup Bench UI");
        }
        else Debug.Log("[BenchSetup] BenchRoot already present");

        // sanity report
        var rig = Object.FindObjectOfType<OVRCameraRig>();
        var bridge = Object.FindObjectOfType<TwinBridge>();
        Debug.Log($"[BenchSetup] OVRCameraRig: {(rig ? "ok" : "MISSING")} · " +
                  $"TwinBridge: {(bridge ? bridge.url : "MISSING")}");

        EditorSceneManager.MarkSceneDirty(
            EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[BenchSetup] done — press Play; mock twin drives " +
                  "everything until the Swift bridge connects.");
    }

    /// Bake dynamic Arial TMP assets into Resources (device builds
    /// can't use OS fonts). Falls back to the TMP package's
    /// LiberationSans SDF when OS fonts won't load (macOS font face
    /// restrictions); BenchUI.EnsureFonts has the same fallback.
    static void BakeFonts()
    {
        ImportTmpEssentials();
        if (Directory.Exists(FontsDir) &&
            Resources.Load<TMP_FontAsset>("BenchArial") != null)
            return; // already baked
        Directory.CreateDirectory(FontsDir);
        Bake("Arial", "BenchArial");
        Bake("Arial Bold", "BenchArialBold");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void ImportTmpEssentials()
    {
        if (Resources.Load<TMP_FontAsset>("LiberationSans SDF") != null)
            return;
        var pkg = Directory.GetFiles("Library/PackageCache",
            "TMP Essential Resources.unitypackage",
            SearchOption.AllDirectories);
        if (pkg.Length > 0)
        {
            AssetDatabase.ImportPackage(pkg[0], false);
            Debug.Log("[BenchSetup] imported TMP Essential Resources");
        }
        else Debug.LogWarning("[BenchSetup] TMP essentials package " +
            "not found under Library/PackageCache");
    }

    static void Bake(string osFamily, string name)
    {
        if (Resources.Load<TMP_FontAsset>(name) != null) return;
        TMP_FontAsset fa = null;
        try
        {
            fa = TMP_FontAsset.CreateFontAsset(
                Font.CreateDynamicFontFromOSFont(osFamily, 44));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[BenchSetup] OS font '" + osFamily +
                "' failed: " + e.Message);
        }
        if (fa == null)
        {
            Debug.LogWarning("[BenchSetup] '" + osFamily + "' unloadable —" +
                " BenchUI falls back to LiberationSans SDF at runtime.");
            return;
        }
        AssetDatabase.CreateAsset(fa, $"{FontsDir}/{name}.asset");
    }
}
