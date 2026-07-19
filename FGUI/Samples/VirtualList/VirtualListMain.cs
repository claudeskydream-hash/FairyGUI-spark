#if CLIENT
using SCEFGUI;
using SCEFGUI.Core;
using SCEFGUI.UI;

namespace SCEFGUI.Samples;

/// <summary>
/// VirtualListMain - Ported from Unity FairyGUI Examples/VirtualList/VirtualListMain.cs
/// </summary>
public class VirtualListMain
{
    private FGUIComponent? _mainView;
    private FGUIList? _list;

    public void Start()
    {
        Game.Logger.LogInformation("[FGUI] Starting VirtualListMain...");

        // 标准做法：先将 FGUIRoot 添加到舞台
        FGUIRoot.Instance.AddToStage();

        // UIPackage.AddPackage("UI/VirtualList")
        FGUIManager.AddPackage("ui/VirtualList", LoadResource);

        // UIObjectFactory.SetPackageItemExtension("ui://VirtualList/mailItem", typeof(MailItem))
        FGUIObjectFactory.SetPackageItemExtension("ui://VirtualList/mailItem", typeof(MailItem));

        // _mainView = this.GetComponent<UIPanel>().ui
        var mainObj = FGUIManager.CreateObject("VirtualList", "Main");
        if (mainObj == null)
        {
            Game.Logger.LogError("[FGUI] Failed to create VirtualList Main!");
            return;
        }

        _mainView = mainObj as FGUIComponent;
        if (_mainView == null)
        {
            Game.Logger.LogError("[FGUI] VirtualList Main is not a FGUIComponent!");
            return;
        }

        // 标准做法：将组件添加为 FGUIRoot 的子组件
        _mainView.SetXY(0, 0);
        FGUIRoot.Instance.AddChild(_mainView);

        // Setup buttons
        _mainView.GetChild("n6")?.OnClick.Add(ctx =>
        {
            _list?.AddSelection(500, true);
        });

        _mainView.GetChild("n7")?.OnClick.Add(ctx =>
        {
            _list?.ScrollPane?.ScrollTop();
        });

        _mainView.GetChild("n8")?.OnClick.Add(ctx =>
        {
            _list?.ScrollPane?.ScrollBottom();
        });

        // Setup list
        _list = _mainView.GetChild("mailList") as FGUIList;
        if (_list != null)
        {
            _list.SetVirtual();
            _list.ItemRenderer = RenderListItem;
            _list.NumItems = 1000;
        }

        Game.Logger.LogInformation("[FGUI] VirtualListMain started successfully!");
    }

    private void RenderListItem(int index, FGUIObject obj)
    {
        var item = obj as MailItem;
        if (item == null) return;

        item.SetFetched(index % 3 == 0);
        item.SetRead(index % 2 == 0);
        item.SetTime("5 Nov 2015 16:24:33");
        item.Title = $"{index} Mail title here";
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
        FGUIManager.RemoveAllPackages();
    }
}
#endif
