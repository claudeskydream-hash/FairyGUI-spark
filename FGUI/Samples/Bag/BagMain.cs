#if CLIENT
using SCEFGUI;
using SCEFGUI.Core;
using SCEFGUI.UI;

namespace SCEFGUI.Samples;

/// <summary>
/// BagMain - Ported from Unity FairyGUI Examples/Bag/BagMain.cs
/// A game bag demo, demonstrates how to customize loader to load icons not in the UI package.
/// </summary>
public class BagMain
{
    private FGUIComponent? _mainView;
    private BagWindow? _bagWindow;

    public void Start()
    {
        Game.Logger.LogInformation("[FGUI] Starting BagMain...");

        // Register custom loader class
        // UIObjectFactory.SetLoaderExtension(typeof(MyGLoader))
        FGUIObjectFactory.SetLoaderExtension<MyGLoader>();

        // 标准做法：先将 FGUIRoot 添加到舞台
        FGUIRoot.Instance.AddToStage();

        // Load packages
        FGUIManager.AddPackage("ui/Bag", LoadResource);

        // _mainView = this.GetComponent<UIPanel>().ui
        var mainObj = FGUIManager.CreateObject("Bag", "Main");
        if (mainObj == null)
        {
            Game.Logger.LogError("[FGUI] Failed to create Bag Main!");
            return;
        }

        _mainView = mainObj as FGUIComponent;
        if (_mainView == null)
        {
            Game.Logger.LogError("[FGUI] Bag Main is not a FGUIComponent!");
            return;
        }

        // 标准做法：将组件添加为 FGUIRoot 的子组件
        _mainView.SetXY(0, 0);
        FGUIRoot.Instance.AddChild(_mainView);

        // Setup bag button
        _bagWindow = new BagWindow();
        _mainView.GetChild("bagBtn")?.OnClick.Add(ctx =>
        {
            _bagWindow.Show();
        });

        Game.Logger.LogInformation("[FGUI] BagMain started successfully!");
    }

    private byte[]? LoadResource(string name, string extension)
    {
        var prefixes = new[] { "", "AppBundle/", "ui/AppBundle/" };
        foreach (var prefix in prefixes)
        {
            string path = prefix + name + extension;
            try
            {
                if (File.Exists(path))
                    return File.ReadAllBytes(path);
            }
            catch { }
        }
        return null;
    }

    public void Dispose()
    {
        _mainView?.Dispose();
        _bagWindow?.Dispose();
        FGUIManager.RemoveAllPackages();
    }
}
#endif
