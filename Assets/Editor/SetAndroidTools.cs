// SetAndroidTools.cs — point Unity at SDK/JDK/NDK (edit-mode ok).
using UnityEditor.Android;

public static class SetAndroidTools
{
    public static string Run()
    {
        var sb = new System.Text.StringBuilder();
        string home = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.Personal);
        string sdk = home + "/Library/Android/sdk";
        string jdk = "/opt/homebrew/opt/openjdk@17";
        string ndk = sdk + "/ndk/27.2.12479018";
        try
        {
            AndroidExternalToolsSettings.sdkRootPath = sdk;
            sb.AppendLine("sdk=" + AndroidExternalToolsSettings.sdkRootPath);
        }
        catch (System.Exception e) { sb.AppendLine("sdk FAIL " + e.Message); }
        try
        {
            AndroidExternalToolsSettings.jdkRootPath = jdk;
            sb.AppendLine("jdk=" + AndroidExternalToolsSettings.jdkRootPath);
        }
        catch (System.Exception e) { sb.AppendLine("jdk FAIL " + e.Message); }
        try
        {
            if (System.IO.Directory.Exists(ndk))
            {
                AndroidExternalToolsSettings.ndkRootPath = ndk;
                sb.AppendLine("ndk=" + AndroidExternalToolsSettings.ndkRootPath);
            }
            else sb.AppendLine("ndk=not downloaded yet, skipping");
        }
        catch (System.Exception e) { sb.AppendLine("ndk FAIL " + e.Message); }
        return sb.ToString();
    }
}
