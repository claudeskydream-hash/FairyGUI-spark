#if CLIENT
namespace FairyGUI;

/// <summary>
/// 全屏根节点容器。原生控件由渲染适配器拉伸到视口，逻辑尺寸用于驱动子节点关系布局。
/// </summary>
public class FGUIRoot : GComponent
{
    private GComponent? _content;

    internal void SetContent(GComponent content)
    {
        if (_content == content)
        {
            return;
        }

        _content?.RemoveFromParent();
        AddChild(content);
        _content = content;
    }

    internal void ApplyViewportSize(float width, float height)
    {
        var safeWidth = MathF.Max(1f, width);
        var safeHeight = MathF.Max(1f, height);
        SetXY(0f, 0f);
        SetSize(safeWidth, safeHeight, true);

        if (_content != null && !_content.Disposed)
        {
            _content.SetXY(0f, 0f);
            _content.SetSize(safeWidth, safeHeight, true);
        }
    }

    public override void Dispose()
    {
        UIRuntime.UnregisterFullScreenRoot(this);
        base.Dispose();
    }
}
#endif
