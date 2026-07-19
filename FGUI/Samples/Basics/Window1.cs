#if CLIENT
using SCEFGUI;
using SCEFGUI.UI;

namespace SCEFGUI.Samples;

/// <summary>
/// Window1 - Ported from Unity FairyGUI Examples/Basics/Window1.cs
/// </summary>
public class Window1 : FGUIWindow
{
    public Window1()
    {
    }

    protected override void OnInit()
    {
        ContentPane = FGUIManager.CreateObject("Basics", "WindowA") as FGUIComponent;
        Center();
    }

    protected override void OnShown()
    {
        var list = ContentPane?.GetChild("n6") as FGUIList;
        if (list == null) return;

        list.RemoveChildrenToPool();

        for (int i = 0; i < 6; i++)
        {
            var item = list.AddItemFromPool() as FGUIButton;
            if (item != null)
            {
                item.Title = i.ToString();
                item.Icon = FGUIManager.GetItemURL("Basics", "r4");
            }
        }
    }
}
#endif
