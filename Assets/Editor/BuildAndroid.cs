// BuildAndroid.cs — one-shot device/store build for MR Electronics Bench.
// Runs inside the Editor (NOT in Play): unity command script run
// --file Assets/Editor/BuildAndroid.cs --entry BuildAndroid.Build
// Env overrides: MRBENCH_APK (output path), MRBENCH_KEYSTORE,
// MRBENCH_STOREPASS, MRBENCH_KEYALIAS, MRBENCH_KEYPASS.
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildAndroid
{
    public static string Build()
    {
        var sb = new System.Text.StringBuilder();
        if (EditorApplication.isPlaying)
            return "ABORT: exit Play mode first";
        StringBuilderLog(sb, "target=" + EditorUserBuildSettings.activeBuildTarget);

        // ── identity (must be set before first store upload; it is
        // permanent afterwards) ──
        const string pkg = "com.twinvr.mrbench";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, pkg);
        PlayerSettings.bundleVersion = "1.0.0";
        PlayerSettings.Android.bundleVersionCode = 1;
        StringBuilderLog(sb, "package=" + PlayerSettings.GetApplicationIdentifier(
            BuildTargetGroup.Android));

        // ── platform switch (slow first time: reimports for Android) ──
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            StringBuilderLog(sb, "switching platform to Android…");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android, BuildTarget.Android))
                return sb + "ABORT: platform switch failed";
        }

        // ── XR check: Android must have an XR loader ──
        // (reflection: the ad-hoc compile has no XR Management ref)
        var loaders = new System.Collections.Generic.List<string>();
        try
        {
            var t = System.Type.GetType(
                "UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget,"
                + " Unity.XR.Management.Editor");
            var m = t?.GetMethod("XRGeneralSettingsForBuildTarget",
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static);
            var general = m?.Invoke(null,
                new object[] { (object)BuildTargetGroup.Android });
            var assigned = general?.GetType()
                .GetProperty("AssignedSettings")?.GetValue(general);
            var list = assigned?.GetType().GetProperty("activeLoaders")
                ?.GetValue(assigned) as System.Collections.IEnumerable;
            if (list != null)
                foreach (var l in list)
                    if (l != null) loaders.Add((l as UnityEngine.Object).name);
        }
        catch (System.Exception e) { loaders.Add("ERR:" + e.Message); }
        StringBuilderLog(sb, "xrLoaders=[" + string.Join(",", loaders) + "]");

        // ── keystore (release; debug falls back automatically) ──
        string ks = System.Environment.GetEnvironmentVariable("MRBENCH_KEYSTORE");
        if (!string.IsNullOrEmpty(ks) && File.Exists(ks))
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = ks;
            PlayerSettings.Android.keystorePass =
                System.Environment.GetEnvironmentVariable("MRBENCH_STOREPASS") ?? "";
            PlayerSettings.Android.keyaliasName =
                System.Environment.GetEnvironmentVariable("MRBENCH_KEYALIAS") ?? "";
            PlayerSettings.Android.keyaliasPass =
                System.Environment.GetEnvironmentVariable("MRBENCH_KEYPASS") ?? "";
            StringBuilderLog(sb, "keystore=custom");
        }
        else StringBuilderLog(sb, "keystore=debug");

        // ── build ──
        string apk = System.Environment.GetEnvironmentVariable("MRBENCH_APK");
        if (string.IsNullOrEmpty(apk))
            apk = "Builds/MRBench.apk";
        string full = Path.IsPathRooted(apk) ? apk
            : Path.Combine(Application.dataPath, "..", apk);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/VR_scene.unity" },
            locationPathName = full,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };
        StringBuilderLog(sb, "building " + full + " …");
        BuildReport rep = BuildPipeline.BuildPlayer(opts);
        StringBuilderLog(sb, "result=" + rep.summary.result
            + " errors=" + rep.summary.totalErrors
            + " size=" + (File.Exists(full)
                ? new FileInfo(full).Length.ToString() : "missing"));
        return sb.ToString();
    }

    private static void StringBuilderLog(System.Text.StringBuilder sb, string s)
    {
        sb.AppendLine(s);
        Debug.Log("[BuildAndroid] " + s);
    }
}
