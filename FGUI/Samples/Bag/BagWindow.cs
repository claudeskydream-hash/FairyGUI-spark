#if CLIENT
using SCEFGUI;
using SCEFGUI.Core;
using SCEFGUI.UI;
using SCEFGUI.Event;
using SCEFGUI.Tween;

namespace SCEFGUI.Samples;

/// <summary>
/// BagWindow - Ported from Unity FairyGUI Examples/Bag/BagWindow.cs
/// </summary>
public class BagWindow : FGUIWindow
{
    private FGUIList? _list;

    public BagWindow()
    {
    }

    protected override void OnInit()
    {
        ContentPane = FGUIManager.CreateObject("Bag", "BagWin") as FGUIComponent;
        Center();
        Modal = true;

        _list = ContentPane?.GetChild("list") as FGUIList;
        if (_list != null)
        {
            _list.OnClick.Add(ClickItem);
            _list.ItemRenderer = RenderListItem;
            _list.NumItems = 45;
        }
    }

    private void RenderListItem(int index, FGUIObject obj)
    {
        var button = obj as FGUIButton;
        if (button == null) return;

        var random = new Random();
        button.Icon = "i" + random.Next(0, 10);
        button.Title = random.Next(0, 100).ToString();
    }

    protected override void DoShowAnimation()
    {
        SetScale(0.1f, 0.1f);
        SetPivot(0.5f, 0.5f);
        
        // this.TweenScale(new Vector2(1, 1), 0.3f).OnComplete(this.OnShown)
        GTween.To(0.1f, 1f, 0.3f)
            .SetTarget(this)
            .OnUpdate(t =>
            {
                var target = t.Target as FGUIWindow;
                target?.SetScale(t.Value.X, t.Value.X);
            })
            .OnComplete(t => OnShown());
    }

    protected override void DoHideAnimation()
    {
        // this.TweenScale(new Vector2(0.1f, 0.1f), 0.3f).OnComplete(this.HideImmediately)
        GTween.To(1f, 0.1f, 0.3f)
            .SetTarget(this)
            .OnUpdate(t =>
            {
                var target = t.Target as FGUIWindow;
                target?.SetScale(t.Value.X, t.Value.X);
            })
            .OnComplete(t => HideImmediately());
    }

    private void ClickItem(EventContext context)
    {
        var item = context.Data as FGUIButton;
        if (item == null || ContentPane == null) return;

        var loader = ContentPane.GetChild("n11") as FGUILoader;
        if (loader != null)
            loader.Url = item.Icon ?? "";

        var text = ContentPane.GetChild("n13") as FGUITextField;
        if (text != null)
            text.Text = item.Icon ?? "";
    }
}
#endif
