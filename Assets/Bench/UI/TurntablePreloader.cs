// TurntablePreloader.cs — kicks every concept turntable's .glb load
// at startup, including on inactive stages, so the 3D models are
// already fitted and spinning when the user navigates to a panel.
using UnityEngine;

public class TurntablePreloader : MonoBehaviour
{
    private async void Start()
    {
        var spins = GetComponentsInChildren<TurntableSpin>(true);
        foreach (var s in spins)
        {
            if (s == null) continue;
            try { await s.LoadNow(); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Turntable] preload " + s.glbPath + ": " + e.Message);
            }
        }
        Debug.Log("[Turntable] preloaded " + spins.Length + " turntables");
    }
}
