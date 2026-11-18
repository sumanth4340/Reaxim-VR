// TwinSettings.cs — tiny persisted settings for the twin link.
// Remembers the last URL that connected so the client can default to
// it next session (loopback for Simulator, LAN IP for hardware).
// PlayerPrefs works in the Editor and in Android builds.

using UnityEngine;

public static class TwinSettings
{
    private const string LastGoodUrlKey = "twin.lastGoodUrl";
    private const string DefaultUrl = "ws://127.0.0.1:17890/";

    public static string LastGoodUrl
    {
        get => PlayerPrefs.GetString(LastGoodUrlKey, DefaultUrl);
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            PlayerPrefs.SetString(LastGoodUrlKey, value);
            PlayerPrefs.Save();
        }
    }
}
