#if CLIENT
using System.Drawing;
using FairyGUI;

namespace FairyGUI.Render;

public interface ISCEAdapter
{
    // Control Creation
    object CreatePanel();
    object CreateLabel();
    object CreateButton();
    object CreateScrollablePanel();
    object CreateInput();
    object CreateVirtualizingPanel();
    object CreateCanvas();
    object CreateCanvas(float width, float height);
    object CreateFillImageControl();
    
    // Basic Properties
    void SetPosition(object control, float x, float y);
    void SetSize(object control, float width, float height);
    void SetVisible(object control, bool visible);
    void SetOpacity(object control, float opacity);
    void SetRotation(object control, float rotation);
    void SetScale(object control, float scaleX, float scaleY);
    void SetTouchable(object control, bool touchable);
    void SetGrayed(object control, bool grayed);

    /// <summary>
    /// 设置控件是否拦截指针事件（消费掉，不再冒泡到父级/场景）。
    /// Control 框架默认 RoutedEvents=All（穿透到父级），交互控件需显式拦截，
    /// 否则点击会一路漏到游戏场景。
    /// </summary>
    void SetBlockPointerEvents(object control, bool block);

    /// <summary>设置可滚动面板的滚动条拖杆尺寸（0 表示隐藏）。</summary>
    void SetScrollBarSize(object control, float size);

    /// <summary>
    /// 在布局位置基础上叠加一个临时平移，用于滚动到边缘继续拖时的橡皮筋过界效果。
    /// 传 (0,0) 归位。不会污染控件的布局基准位置。
    /// </summary>
    void SetScrollOverscroll(object control, float dx, float dy);
    void SetBackgroundColor(object control, Color color);
    void SetBackgroundImage(object control, string imagePath);
    bool TrySetImageFill(object control, FillMethod fillMethod, int fillOrigin, bool fillClockwise, float fillAmount);
    void SetCanvasImage(object control, string imagePath);
    void SetCanvasEllipse(object control, Color fillColor);
    void ClearCanvasRenderState(object control);
    void SetSlicedImage(object control, string imagePath, int left, int right, int top, int bottom);
    void SetImageRegion(object control, string atlasPath, RectangleF region, bool rotated);
    void SetTintColor(object control, Color color);  // 图片着色（与背景色不同）
    void SetText(object control, string text);
    void SetTextColor(object control, Color color);
    void SetFontSize(object control, int size);
    void SetFontName(object control, string fontFamily);
    void SetBold(object control, bool bold);
    void SetItalic(object control, bool italic);
    void SetTextAlign(object control, TextAlign align);
    void SetTextVerticalAlign(object control, TextVerticalAlign align);
    
    // 输入框相关
    void SetInputPlaceholder(object control, string placeholder);
    void SetInputPassword(object control, bool isPassword);
    void SetInputMaxLength(object control, int maxLength);
    void SetInputEditable(object control, bool editable);
    void OnInputTextChanged(object control, Action<string> handler);
    void SetCornerRadius(object control, float radius);
    void SetZIndex(object control, int zIndex);
    void SetClipContent(object control, bool clip);
    void SetMaskControl(object control, object? maskControl, bool inverted);
    void ConfigureScrollable(object control, bool enabled, bool horizontal);
    void SetScrollValue(object control, float value);
    void OnScrollChanged(object control, Action<float> handler);
    
    // Hierarchy
    void AddChild(object parent, object child);
    void RemoveChild(object parent, object child);
    void AddToRoot(object control);
    void AddToRootWithFixedSize(object control, float width, float height);
    void RemoveFromParent(object control);
    
    // Basic Events
    void OnClick(object control, Action handler);
    void OnPointerEnter(object control, Action handler);
    void OnPointerLeave(object control, Action handler);
    void OnPointerPress(object control, Action handler);
    void OnPointerPressWithPosition(object control, Action<float, float> handler);
    void OnPointerRelease(object control, Action handler);
    void OnMouseWheel(object control, Action<float> handler);
    
    // Touch Behavior (Gestures)
    void EnableTouchBehavior(object control, TouchBehaviorConfig config);
    void DisableTouchBehavior(object control);
    void OnLongPress(object control, Action handler);
    void OnDoubleClick(object control, Action handler);
    
    // Pointer Capture (for Drag/Swipe)
    void CapturePointer(object control);
    void ReleasePointer(object control);
    void OnPointerCapturedMove(object control, Action<float, float> handler);
    
    // Virtualizing Panel
    void SetVirtualizingPanelConfig(object panel, VirtualPanelConfig config);
    void SetVirtualizingPanelItems(object panel, int itemCount, Action<int, object> itemRenderer);
    void RefreshVirtualizingPanel(object panel);
    
    // Utilities
    void Dispose(object control);
    byte[]? LoadTexture(string path);
    SizeF GetScreenSize();
}

public enum TextAlign { Left, Center, Right }
public enum TextVerticalAlign { Top, Middle, Bottom }

/// <summary>
/// Configuration for TouchBehavior
/// </summary>
public class TouchBehaviorConfig
{
    public float ScaleFactor { get; set; } = 0.95f;
    public bool EnablePressAnimation { get; set; } = true;
    public bool EnableLongPress { get; set; } = true;
    public int AnimationDurationMs { get; set; } = 150;
    public int LongPressDurationMs { get; set; } = 500;
    
    public static TouchBehaviorConfig Default => new();
    public static TouchBehaviorConfig Quick => new() { ScaleFactor = 0.97f, AnimationDurationMs = 80, LongPressDurationMs = 400 };
    public static TouchBehaviorConfig Emphasized => new() { ScaleFactor = 0.90f, AnimationDurationMs = 150, LongPressDurationMs = 500 };
    public static TouchBehaviorConfig Gentle => new() { ScaleFactor = 0.98f, AnimationDurationMs = 120, LongPressDurationMs = 600 };
}

/// <summary>
/// Configuration for VirtualizingPanel
/// </summary>
public class VirtualPanelConfig
{
    public float ItemWidth { get; set; }
    public float ItemHeight { get; set; }
    public bool IsHorizontal { get; set; }
    public bool UseRecycling { get; set; } = true;
    public float CachePages { get; set; } = 1.5f;
}
#endif

