#if CLIENT
using SCEFGUI.UI;

namespace SCEFGUI.Samples;

/// <summary>
/// MyGLoader - Ported from Unity FairyGUI Examples/Bag/MyGLoader.cs
/// Extend the ability of GLoader to load external icons.
/// </summary>
public class MyGLoader : FGUILoader
{
    protected override void LoadExternal()
    {
        // In Unity: IconManager.inst.LoadIcon(this.url, OnLoadSuccess, OnLoadFail)
        // Here we implement simplified external loading
        
        if (string.IsNullOrEmpty(Url))
        {
            OnExternalLoadFailed();
            return;
        }

        // Try to load from external path
        // For now, just use the url as icon name (e.g., "i0" -> "i0.png")
        try
        {
            // In a real implementation, this would load from an external source
            // For demo purposes, we'll try to load from a predefined path
            string iconPath = $"icons/{Url}.png";
            
            if (File.Exists(iconPath))
            {
                var bytes = File.ReadAllBytes(iconPath);
                // Note: Actual texture creation would depend on SCE's image loading API
                OnExternalLoadSuccess(bytes);
            }
            else
            {
                // Icon not found, let base class handle
                OnExternalLoadFailed();
            }
        }
        catch (Exception ex)
        {
            Game.Logger.LogWarning($"[FGUI] MyGLoader failed to load {Url}: {ex.Message}");
            OnExternalLoadFailed();
        }
    }

    protected override void FreeExternal()
    {
        // In Unity: texture.refCount--
        // Release any external resources here
    }

    private void OnExternalLoadSuccess(byte[] data)
    {
        if (string.IsNullOrEmpty(Url))
            return;

        // In Unity: this.onExternalLoadSuccess(texture)
        // For SCE, we would create a texture and set it
        // This is a simplified version
        Game.Logger.LogInformation($"[FGUI] MyGLoader loaded external: {Url}");
    }

    private void OnExternalLoadFailed()
    {
        Game.Logger.LogWarning($"[FGUI] MyGLoader load failed: {Url}");
        // In Unity: this.onExternalLoadFailed()
    }
}
#endif
