#if CLIENT
using SCEFGUI;
using SCEFGUI.UI;
using SCEFGUI.Tween;

namespace SCEFGUI.Samples;

/// <summary>
/// Window2 - Ported from Unity FairyGUI Examples/Basics/Window2.cs
/// </summary>
public class Window2 : FGUIWindow
{
    public Window2()
    {
    }

    protected override void OnInit()
    {
        ContentPane = FGUIManager.CreateObject("Basics", "WindowB") as FGUIComponent;
        Center();
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

    protected override void OnShown()
    {
        ContentPane?.GetTransition("t1")?.Play();
    }

    protected override void OnHide()
    {
        ContentPane?.GetTransition("t1")?.Stop();
    }
}
#endif
