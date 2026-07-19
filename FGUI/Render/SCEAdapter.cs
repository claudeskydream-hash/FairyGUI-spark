#if CLIENT
using System.Drawing;
using System.Reflection;
using System.Linq.Expressions;
using GameUI.Control;
using GameUI.Control.Primitive;
using GameUI.Control.Extensions;
using GameUI.Control.Behavior;
using GameUI.Enum;
using GameCore.ResourceType;
using GameCore.Platform.SDL;
using FairyGUI;

namespace FairyGUI.Render;

/// <summary>
/// Real SCE adapter implementation using actual SCE UI API
/// </summary>
public class SCEAdapter : ISCEAdapter
{
    private const float MinNativeSize = 1f;
    private const float MinCanvasDrawSize = 2f;
    private const bool EnableImagePipelineInfoLogs = false;
    // Store TouchBehavior instances to manage lifecycle
    private readonly Dictionary<object, TouchBehavior> _touchBehaviors = new();
    // Store captured pointer buttons per control
    private readonly Dictionary<object, PointerButtons> _capturedPointers = new();
    private static readonly PointerButtons PrimaryCaptureButtons = BuildPrimaryCaptureButtons();
    // Store long press handlers
    private readonly Dictionary<object, Action> _longPressHandlers = new();
    // Store pointer move handlers
    private readonly Dictionary<object, Action<float, float>> _pointerMoveHandlers = new();
    // Track the latest size set by adapter to avoid relying on optional native width/height getters.
    private readonly Dictionary<object, SizeF> _controlSizes = new();
    // Track the requested logical size (can be zero), so canvas draw can skip safely.
    private readonly Dictionary<object, SizeF> _requestedControlSizes = new();
    // Track the unscaled base size so SetScale can restore/reapply consistently.
    private readonly Dictionary<object, SizeF> _baseControlSizes = new();
    // Track logical top-left position before scale fallback adjusts visual bounds.
    private readonly Dictionary<object, PointF> _controlPositions = new();
    // 滚动面板 → 其裁剪外壳(与视口等大、开启 ClipContent)。过界时平移面板本体、由外壳裁掉溢出。
    private readonly Dictionary<object, Panel> _scrollClipHost = new();
    // 过界平移上次实际写入的偏移，用于跳过界内滚动(dx=dy=0)每帧的冗余原生 Position 写入。
    private readonly Dictionary<object, PointF> _overscrollLastApplied = new();
    // Base rectangles for panel-subtree fallback scaling (button downEffect=scale).
    private readonly Dictionary<object, RectangleF> _fallbackScaleBaseRects = new();
    private readonly HashSet<object> _fallbackScaleActiveRoots = new();
    // Track last visible/alpha set through adapter.
    private readonly Dictionary<object, bool> _controlVisibleStates = new();
    private readonly Dictionary<object, float> _controlAlphaStates = new();
    private readonly HashSet<Type> _flipTypedUnsupportedTypes = new();
    private readonly HashSet<Type> _flipUnsupportedLoggedTypes = new();
    private readonly Dictionary<Type, TintCapability> _tintCapabilityCache = new();
    private readonly Dictionary<Type, ImageFillCapability> _imageFillCapabilityCache = new();
    private readonly HashSet<Type> _tintUnsupportedLoggedTypes = new();
    private bool _fillProgressFallbackLogged;
    private bool _fillProgressUnsupportedLogged;
    private bool _fillProgressControlFallbackLogged;
    private bool _fillProgressControlCreatedLogged;
    private bool _fillProgressImageCompatLogged;
    private static readonly Type? ProgressControlType = typeof(Control).Assembly.GetType("GameUI.Control.Primitive.Progress");
    private static readonly ConstructorInfo? ProgressControlCtor = ResolveProgressControlConstructor();
    // Keep hierarchy tracking in adapter as last-line idempotency protection.
    private readonly Dictionary<object, object> _attachedParentByChild = new();
    private int _hierarchyDiagLogCount;
    private const int HierarchyDiagLogLimit = 24;
    private int _invalidDrawDiagLogCount;
    private const int InvalidDrawDiagLogLimit = 40;
    private int _sizeGuardDiagLogCount;
    private const int SizeGuardDiagLogLimit = 40;
    private int _textureGuardDiagLogCount;
    private const int TextureGuardDiagLogLimit = 80;
    private int _tintDiagLogCount;
    private const int TintDiagLogLimit = 20;
    private int _flipDiagLogCount;
    private const int FlipDiagLogLimit = 20;
    private int _rotationDiagLogCount;
    private const int RotationDiagLogLimit = 20;
    private int _clipDiagLogCount;
    private const int ClipDiagLogLimit = 20;
    private int _maskDiagLogCount;
    private const int MaskDiagLogLimit = 20;
    private int _pointerFilterDiagLogCount;
    private const int PointerFilterDiagLogLimit = 80;
    private int _scaleDiagLogCount;
    private const int ScaleDiagLogLimit = 80;
    private readonly Dictionary<Canvas, CanvasImageRenderState> _canvasImageStates = new();
    private readonly Dictionary<string, Image> _canvasImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<VirtualizingPanel, VirtualizingPanelRenderState> _virtualizingPanelStates = new();

    private enum CanvasRenderMode
    {
        PlainImage,
        AtlasRegion,
        AtlasSliced,
        FilledEllipse
    }

    private sealed class AtlasSlicedRenderState
    {
        public RectangleF SpriteRect { get; set; }
        public int Left { get; set; }
        public int Right { get; set; }
        public int Top { get; set; }
        public int Bottom { get; set; }
        public float FallbackWidth { get; set; } = MinNativeSize;
        public float FallbackHeight { get; set; } = MinNativeSize;
    }
    
    private sealed class CanvasImageRenderState
    {
        public string ResolvedPath { get; set; } = string.Empty;
        public Image? CurrentImage { get; set; }
        public CanvasRenderMode RenderMode { get; set; } = CanvasRenderMode.PlainImage;
        public RectangleF SourceRegion { get; set; } = RectangleF.Empty;
        public bool Rotated { get; set; }
        public AtlasSlicedRenderState? AtlasSliced { get; set; }
        public Color FillColor { get; set; } = Color.Transparent;
    }

    private sealed class VirtualizingPanelRenderState
    {
        public bool Hooked { get; set; }
        public int ItemCount { get; set; }
        public Action<int, object>? ItemRenderer { get; set; }
    }

    private sealed class TintCapability
    {
        public MethodInfo? Method { get; init; }
        public PropertyInfo? Property { get; init; }
        public bool Unsupported { get; init; }
    }

    private sealed class ImageFillCapability
    {
        public PropertyInfo? FillMethod { get; init; }
        public PropertyInfo? FillOrigin { get; init; }
        public PropertyInfo? FillClockwise { get; init; }
        public PropertyInfo? FillAmount { get; init; }
        public PropertyInfo? ProgressValue { get; init; }
        public PropertyInfo? ProgressionMode { get; init; }
        public PropertyInfo? ProgressRotation { get; init; }
        public bool Unsupported { get; init; }
    }
    
    public object CreatePanel()
    {
        var panel = new Panel();
        panel.Background(Color.Transparent);
        return panel;
    }

    public object CreateFillImageControl()
    {
        if (ProgressControlType != null && ProgressControlCtor != null)
        {
            try
            {
                var control = ProgressControlCtor.Invoke(Array.Empty<object>()) as Control;
                if (control != null)
                {
                    control.Background(Color.Transparent);
                    if (!_fillProgressControlCreatedLogged)
                    {
                        _fillProgressControlCreatedLogged = true;
                        System.GC.KeepAlive(0);
                    }
                    return control;
                }
            }
            catch
            {
                // Fallback to Panel.
            }
        }

        if (!_fillProgressControlFallbackLogged)
        {
            _fillProgressControlFallbackLogged = true;
            System.GC.KeepAlive(0);
        }
        return CreatePanel();
    }

    public object CreateLabel()
    {
        var label = new Label();
        label.Background(Color.Transparent);
        return label;
    }

    public object CreateButton()
    {
        var button = new Button();
        button.Background(Color.Transparent);
        return button;
    }

    public object CreateScrollablePanel()
    {
        var panel = new PanelScrollable();
        panel.Background(Color.Transparent);
        panel.ScrollEnabled = true;
        panel.ScrollOrientation = Orientation.Vertical;
        panel.ScrollBarValue = 0f;
        panel.ClipContent = true;

        // 裁剪外壳：过界时平移“面板本体”不会与原生子级排布打架(不抖)，但面板一动会连自身裁剪框
        // 一起移走、挡不住溢出。故用一个与视口等大、开启 ClipContent 的外壳把面板包住：
        // 面板在外壳内平移，外壳把移出视口的部分裁掉——既不抖，也不会飞出容器。
        // 面板始终固定在外壳内 (0,0)；列表真实的位置/尺寸由外壳承载(见 SetPosition/SetSize/AddChild)。
        var clipHost = new Panel();
        clipHost.Background(Color.Transparent);
        clipHost.ClipContent = true;
        clipHost.AlignTop().AlignLeft();
        panel.AlignTop().AlignLeft();
        clipHost.Add(panel);
        _scrollClipHost[panel] = clipHost;
        return panel;
    }

    public object CreateInput()
    {
        var input = new Input();
        input.Background(Color.Transparent);
        return input;
    }
    public object CreateVirtualizingPanel() => new VirtualizingPanel();
    public object CreateCanvas()
    {
        var canvas = new Canvas();
        canvas.Size(1f, 1f);
        return canvas;
    }
    
    // Create Canvas for image rendering with atlas region support
    public object CreateCanvas(float width, float height)
    {
        var canvas = new Canvas();
        var safeWidth = ClampNativeDimension(width);
        var safeHeight = ClampNativeDimension(height);
        canvas.Size(safeWidth, safeHeight);
        return canvas;
    }

    public void SetPosition(object control, float x, float y)
    {
        if (control is not Control c)
        {
            return;
        }

        if (_scrollClipHost.TryGetValue(control, out var host))
        {
            // 有裁剪外壳：真实位置落到外壳上，面板在外壳内固定 (0,0)。
            // 面板基准记 (0,0)，过界时 SetScrollOverscroll 以此为基准在外壳内平移面板。
            host.Position(x, y);
            TrackControlPosition(host, x, y);
            c.Position(0, 0);
            TrackControlPosition(control, 0, 0);
            return;
        }

        c.Position(x, y);
        TrackControlPosition(control, x, y);
    }

    public void SetScrollOverscroll(object control, float dx, float dy)
    {
        // 过界(橡皮筋)平移的是“面板本体”，不是它内部的子级。
        // 原因：原生面板滚动时只移动其内部子级、从不改动面板自身相对父级的位置，
        // 所以在这里平移面板自身不会与原生的子级排布互相抢(不会“打哆嗦”)。
        // 面板固定在裁剪外壳内、基准 (0,0)，这里把它在外壳内平移；移出视口的部分由外壳裁掉，不会飞出容器。
        if (control is Control c && _controlPositions.TryGetValue(control, out var basePos))
        {
            // 界内滚动时 dx=dy=0，面板本就停在基准位置，无需每帧重复写入原生 Position；
            // 仅当过界量相对上次有变化时才写。这是每帧都会命中的热点，收益覆盖所有可滚动列表。
            var last = _overscrollLastApplied.TryGetValue(control, out var l) ? l : default;
            if (MathF.Abs(last.X - dx) < 0.01f && MathF.Abs(last.Y - dy) < 0.01f)
            {
                return;
            }

            c.Position(basePos.X + dx, basePos.Y + dy);
            _overscrollLastApplied[control] = new PointF(dx, dy);
        }
    }

    public void SetSize(object control, float width, float height)
    {
        if (control is Control c)
        {
            var requestedWidth = (float.IsNaN(width) || float.IsInfinity(width)) ? 0f : width;
            var requestedHeight = (float.IsNaN(height) || float.IsInfinity(height)) ? 0f : height;
            _requestedControlSizes[control] = new SizeF(requestedWidth, requestedHeight);

            var safeWidth = ClampNativeDimension(width);
            var safeHeight = ClampNativeDimension(height);
            if (safeWidth != width || safeHeight != height)
            {
                LogSizeGuardDiag($"[FGUI][SIZE-GUARD] normalized control={control.GetType().Name} requested={width}x{height} safe={safeWidth}x{safeHeight}");
            }

            c.Size(safeWidth, safeHeight);
            TrackControlSize(control, safeWidth, safeHeight);
            _baseControlSizes[control] = new SizeF(safeWidth, safeHeight);

            // 有裁剪外壳：外壳与面板同尺寸，才能恰好裁在视口边界上。
            if (_scrollClipHost.TryGetValue(control, out var host))
            {
                host.Size(safeWidth, safeHeight);
                TrackControlSize(host, safeWidth, safeHeight);
            }
        }
    }

    public void SetVisible(object control, bool visible)
    {
        if (control is Control c)
        {
            _controlVisibleStates[control] = visible;
            if (visible) c.Show();
            else c.Hide();
        }
    }

    public void SetOpacity(object control, float opacity)
    {
        if (control is Control c)
        {
            _controlAlphaStates[control] = opacity;
            c.Opacity(opacity);
        }
    }

    public void SetRotation(object control, float rotation)
    {
        if (control is not Control c)
        {
            return;
        }

        var type = c.GetType();
        var applied = false;
        applied |= TrySetFloatProperty(type, c, "Rotation", rotation);
        applied |= TrySetFloatProperty(type, c, "Angle", rotation);
        applied |= TrySetFloatProperty(type, c, "RotationZ", rotation);
        applied |= TrySetFloatProperty(type, c, "EulerZ", rotation);
        applied |= TryInvokeFloatMethod(type, c, "SetRotation", rotation);
        applied |= TryInvokeFloatMethod(type, c, "SetAngle", rotation);
        applied |= TryInvokeFloatMethod(type, c, "Rotate", rotation);

        if (!applied && MathF.Abs(rotation) > 0.001f && _rotationDiagLogCount < RotationDiagLogLimit)
        {
            _rotationDiagLogCount++;
            System.GC.KeepAlive(0);
        }
    }

    public void SetScale(object control, float scaleX, float scaleY)
    {
        if (control is not Control c)
        {
            return;
        }

        if (TryApplyNativeScale(c, scaleX, scaleY))
        {
            LogScaleRoute(c, scaleX, scaleY, "native");
            return;
        }

        if (!_baseControlSizes.TryGetValue(control, out var baseSize) ||
            baseSize.Width < MinNativeSize ||
            baseSize.Height < MinNativeSize)
        {
            if (_controlSizes.TryGetValue(control, out var trackedSize) &&
                trackedSize.Width >= MinNativeSize &&
                trackedSize.Height >= MinNativeSize)
            {
                baseSize = trackedSize;
            }
            else
            {
                var width = ResolveControlDimension(c, "Width", 1f);
                width = ResolveControlDimension(c, "ActualWidth", width);
                var height = ResolveControlDimension(c, "Height", 1f);
                height = ResolveControlDimension(c, "ActualHeight", height);
                baseSize = new SizeF(MathF.Max(1f, width), MathF.Max(1f, height));
            }

            _baseControlSizes[control] = baseSize;
        }

        var flipX = scaleX < 0f;
        var flipY = scaleY < 0f;
        var requestedFlip = flipX || flipY;
        var flipApplied = TryApplyNativeFlip(c, flipX, flipY);
        if (requestedFlip && !flipApplied &&
            _flipUnsupportedLoggedTypes.Add(c.GetType()) &&
            _flipDiagLogCount < FlipDiagLogLimit)
        {
            _flipDiagLogCount++;
            System.GC.KeepAlive(0);
        }

        var safeScaleX = MathF.Max(0.01f, MathF.Abs(scaleX));
        var safeScaleY = MathF.Max(0.01f, MathF.Abs(scaleY));
        var targetWidth = MathF.Max(1f, baseSize.Width * safeScaleX);
        var targetHeight = MathF.Max(1f, baseSize.Height * safeScaleY);

        var basePos = _controlPositions.TryGetValue(control, out var trackedPos)
            ? trackedPos
            : ResolveControlPosition(c);

        if (c is Panel &&
            TryApplyPanelSubtreeScaleFallback(control, basePos, baseSize, scaleX, scaleY))
        {
            LogScaleRoute(c, scaleX, scaleY, "fallback-panel-subtree");
            return;
        }

        // FGUI downEffect scale expects center-based scaling semantics.
        // Use center offset fallback for Panel as well so button press feedback matches editor behavior.
        var targetX = basePos.X + (baseSize.Width - targetWidth) * 0.5f;
        var targetY = basePos.Y + (baseSize.Height - targetHeight) * 0.5f;

        c.Position(targetX, targetY);
        c.Size(targetWidth, targetHeight);
        TrackControlSize(control, targetWidth, targetHeight);
        LogScaleRoute(c, scaleX, scaleY, "fallback-center-offset");
    }

    private bool TryApplyPanelSubtreeScaleFallback(
        object rootControl,
        PointF fallbackRootPos,
        SizeF fallbackRootSize,
        float scaleX,
        float scaleY)
    {
        var safeScaleX = MathF.Max(0.01f, MathF.Abs(scaleX));
        var safeScaleY = MathF.Max(0.01f, MathF.Abs(scaleY));
        var isIdentity = MathF.Abs(safeScaleX - 1f) < 0.0001f && MathF.Abs(safeScaleY - 1f) < 0.0001f;

        // Cold path during initial page build: many controls call SetScale(1,1).
        // If this root is not actively scaled, skip subtree traversal/snapshot work.
        if (isIdentity && !_fallbackScaleActiveRoots.Contains(rootControl))
        {
            return true;
        }

        var nodes = CollectSubtreeNodes(rootControl);
        if (nodes.Count == 0)
        {
            return false;
        }

        if (isIdentity)
        {
            _fallbackScaleActiveRoots.Remove(rootControl);
            RestoreSubtreeBaseRects(nodes);

            return true;
        }

        if (!_fallbackScaleActiveRoots.Contains(rootControl))
        {
            SnapshotSubtreeBaseRects(nodes);
        }
        else
        {
            SnapshotMissingSubtreeBaseRects(nodes);
        }

        if (!_fallbackScaleBaseRects.TryGetValue(rootControl, out var rootRect))
        {
            rootRect = new RectangleF(
                fallbackRootPos.X,
                fallbackRootPos.Y,
                MathF.Max(1f, fallbackRootSize.Width),
                MathF.Max(1f, fallbackRootSize.Height));
            _fallbackScaleBaseRects[rootControl] = rootRect;
        }

        foreach (var node in nodes)
        {
            if (node is not Control nodeControl)
            {
                continue;
            }

            if (!_fallbackScaleBaseRects.TryGetValue(node, out var baseRect))
            {
                continue;
            }

            float scaledX;
            float scaledY;
            var scaledWidth = MathF.Max(1f, baseRect.Width * safeScaleX);
            var scaledHeight = MathF.Max(1f, baseRect.Height * safeScaleY);

            if (ReferenceEquals(node, rootControl))
            {
                // Root is in parent space: keep center fixed in parent coordinates.
                var centerX = baseRect.X + baseRect.Width * 0.5f;
                var centerY = baseRect.Y + baseRect.Height * 0.5f;
                scaledX = centerX - scaledWidth * 0.5f;
                scaledY = centerY - scaledHeight * 0.5f;
            }
            else
            {
                // Descendants are in local(parent) space: scale local rect directly.
                scaledX = baseRect.X * safeScaleX;
                scaledY = baseRect.Y * safeScaleY;
            }

            nodeControl.Position(scaledX, scaledY);
            nodeControl.Size(scaledWidth, scaledHeight);
        }

        _fallbackScaleActiveRoots.Add(rootControl);
        return true;
    }

    private List<object> CollectSubtreeNodes(object rootControl)
    {
        var nodes = new List<object> { rootControl };
        for (var i = 0; i < nodes.Count; i++)
        {
            var parent = nodes[i];
            foreach (var pair in _attachedParentByChild)
            {
                if (!ReferenceEquals(pair.Value, parent))
                {
                    continue;
                }

                if (!nodes.Contains(pair.Key))
                {
                    nodes.Add(pair.Key);
                }
            }
        }

        return nodes;
    }

    private void SnapshotSubtreeBaseRects(List<object> nodes)
    {
        foreach (var node in nodes)
        {
            if (TryResolveControlRect(node, out var rect))
            {
                _fallbackScaleBaseRects[node] = rect;
            }
        }
    }

    private void SnapshotMissingSubtreeBaseRects(List<object> nodes)
    {
        foreach (var node in nodes)
        {
            if (_fallbackScaleBaseRects.ContainsKey(node))
            {
                continue;
            }

            if (TryResolveControlRect(node, out var rect))
            {
                _fallbackScaleBaseRects[node] = rect;
            }
        }
    }

    private void RestoreSubtreeBaseRects(List<object> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is not Control control)
            {
                continue;
            }

            if (!_fallbackScaleBaseRects.TryGetValue(node, out var baseRect))
            {
                continue;
            }

            control.Position(baseRect.X, baseRect.Y);
            control.Size(baseRect.Width, baseRect.Height);
        }
    }

    private bool TryResolveControlRect(object control, out RectangleF rect)
    {
        if (control is not Control nativeControl)
        {
            rect = RectangleF.Empty;
            return false;
        }

        var position = _controlPositions.TryGetValue(control, out var trackedPos)
            ? trackedPos
            : ResolveControlPosition(nativeControl);

        var size = _controlSizes.TryGetValue(control, out var trackedSize)
            ? trackedSize
            : new SizeF(
                MathF.Max(1f, ResolveControlDimension(nativeControl, "ActualWidth", ResolveControlDimension(nativeControl, "Width", 1f))),
                MathF.Max(1f, ResolveControlDimension(nativeControl, "ActualHeight", ResolveControlDimension(nativeControl, "Height", 1f))));

        rect = new RectangleF(
            position.X,
            position.Y,
            MathF.Max(1f, size.Width),
            MathF.Max(1f, size.Height));
        return true;
    }

    public void SetTouchable(object control, bool touchable)
    {
        if (control is Control c)
        {
            if (touchable) c.Enable();
            else c.Disable();
        }
    }

    public void SetBlockPointerEvents(object control, bool block)
    {
        if (control is not Control c)
        {
            return;
        }

        // BlockPointerEvents: 控件消费指针事件；AllowPointerPassthrough: 恢复默认穿透到父级。
        if (block)
        {
            c.BlockPointerEvents();
        }
        else
        {
            c.AllowPointerPassthrough();
        }
    }

    public void SetScrollBarSize(object control, float size)
    {
        if (control is PanelScrollable panel)
        {
            var safe = MathF.Max(0f, size);
            panel.ScrollBarSize = safe;
            // 原生 PanelScrollable 只把宽度设 0 不足以隐藏拖杆(仍会画出轨道/滑块)，
            // 隐藏时把滚动条颜色一并设为全透明，彻底不可见。
            if (safe <= 0f)
            {
                panel.ScrollBarColor = Color.Transparent;
            }
        }
    }

    public void SetGrayed(object control, bool grayed)
    {
        if (control is Control c)
        {
            if (grayed)
            {
                c.Disable();
                if (!TryApplyNativeTint(c, Color.FromArgb(255, 170, 170, 170)))
                {
                    var baseAlpha = _controlAlphaStates.TryGetValue(control, out var alpha)
                        ? alpha
                        : 1f;
                    c.Opacity(MathF.Max(0.15f, baseAlpha * 0.75f));
                }
            }
            else
            {
                c.Enable();
                _ = TryApplyNativeTint(c, Color.White);
                if (_controlAlphaStates.TryGetValue(control, out var alpha))
                {
                    c.Opacity(alpha);
                }
            }
        }
    }

    public void SetBackgroundColor(object control, Color color)
    {
        if (control is Control c)
            c.Background(color);
    }

    public void SetBackgroundImage(object control, string imagePath)
    {
        if (control is Control c && !string.IsNullOrEmpty(imagePath))
        {
            if (!TryEnsureImageTargetSize(c, imagePath, "SetBackgroundImage"))
            {
                return;
            }

            var resolvedImagePath = ResolveControlImagePath(imagePath);
            // SCE Control has Image property for setting image path
            c.Image = resolvedImagePath;
            var type = c.GetType();
            if (type.Name.Contains("Progress", StringComparison.OrdinalIgnoreCase))
            {
                // Some Progress implementations render fill via mask/foreground image.
                var compatApplied = false;
                compatApplied |= TrySetStringProperty(type, c, "ImageMask", resolvedImagePath);
                compatApplied |= TrySetStringProperty(type, c, "FillImage", resolvedImagePath);
                compatApplied |= TrySetStringProperty(type, c, "ProgressImage", resolvedImagePath);
                if (compatApplied && !_fillProgressImageCompatLogged)
                {
                    _fillProgressImageCompatLogged = true;
                    System.GC.KeepAlive(0);
                }
            }
            if (EnableImagePipelineInfoLogs)
            {
                System.GC.KeepAlive(0);
            }
        }
    }

    public bool TrySetImageFill(object control, FillMethod fillMethod, int fillOrigin, bool fillClockwise, float fillAmount)
    {
        if (control is not Control c)
        {
            return false;
        }

        var type = c.GetType();
        if (!_imageFillCapabilityCache.TryGetValue(type, out var capability))
        {
            capability = ResolveImageFillCapability(type);
            _imageFillCapabilityCache[type] = capability;
        }

        if (capability.Unsupported)
        {
            return false;
        }

        var appliedAny = false;
        appliedAny |= TryAssignFillValue(c, capability.FillMethod, fillMethod);
        appliedAny |= TryAssignFillValue(c, capability.FillOrigin, fillOrigin);
        appliedAny |= TryAssignFillValue(c, capability.FillClockwise, fillClockwise);
        appliedAny |= TryAssignFillValue(c, capability.FillAmount, Math.Clamp(fillAmount, 0f, 1f));
        var progressApplied = TryApplyProgressFill(c, capability, fillMethod, fillOrigin, fillClockwise, fillAmount);
        if (progressApplied && !_fillProgressFallbackLogged)
        {
            _fillProgressFallbackLogged = true;
            System.GC.KeepAlive(0);
        }
        else if (!appliedAny && !progressApplied && !_fillProgressUnsupportedLogged && fillMethod != FillMethod.None)
        {
            _fillProgressUnsupportedLogged = true;
            System.GC.KeepAlive(0);
        }

        return appliedAny || progressApplied;
    }

    /// <summary>
    /// Assign image to a Canvas-backed control and draw it in OnRender.
    /// Avoids high-frequency Control.Image path swaps that cause visible flicker on MovieClip playback.
    /// </summary>
    public void SetCanvasImage(object control, string imagePath)
    {
        if (control is not Canvas canvas || string.IsNullOrWhiteSpace(imagePath))
        {
            SetBackgroundImage(control, imagePath);
            return;
        }

        if (!TryEnsureImageTargetSize(canvas, imagePath, "SetCanvasImage"))
        {
            return;
        }

        var resolvedImagePath = ResolveControlImagePath(imagePath);
        var state = EnsureCanvasImageState(canvas);
        if (state.CurrentImage.HasValue &&
            state.ResolvedPath.Equals(resolvedImagePath, StringComparison.OrdinalIgnoreCase) &&
            state.RenderMode == CanvasRenderMode.PlainImage)
        {
            return;
        }

        if (!TryGetCanvasImage(resolvedImagePath, out var image))
        {
            return;
        }

        state.ResolvedPath = resolvedImagePath;
        state.CurrentImage = image;
        state.RenderMode = CanvasRenderMode.PlainImage;
        state.SourceRegion = RectangleF.Empty;
        state.Rotated = false;
        state.AtlasSliced = null;
        state.FillColor = Color.Transparent;
    }

    public void SetCanvasEllipse(object control, Color fillColor)
    {
        if (control is not Canvas canvas)
        {
            SetBackgroundColor(control, fillColor);
            return;
        }

        var state = EnsureCanvasImageState(canvas);
        state.ResolvedPath = string.Empty;
        state.CurrentImage = null;
        state.RenderMode = CanvasRenderMode.FilledEllipse;
        state.SourceRegion = RectangleF.Empty;
        state.Rotated = false;
        state.AtlasSliced = null;
        state.FillColor = fillColor;
    }

    public void ClearCanvasRenderState(object control)
    {
        if (control is not Canvas canvas || !_canvasImageStates.TryGetValue(canvas, out var state))
        {
            return;
        }

        state.ResolvedPath = string.Empty;
        state.CurrentImage = null;
        state.RenderMode = CanvasRenderMode.PlainImage;
        state.SourceRegion = RectangleF.Empty;
        state.Rotated = false;
        state.AtlasSliced = null;
        state.FillColor = Color.Transparent;
    }

    public void SetSlicedImage(object control, string imagePath, int left, int right, int top, int bottom)
    {
        if (control is Control c && !string.IsNullOrEmpty(imagePath))
        {
            var normalizedImagePath = imagePath.Replace('\\', '/');
            if (!TryEnsureImageTargetSize(c, imagePath, "SetSlicedImage"))
            {
                return;
            }

            var renderSize = ResolveRenderSize(c, MinNativeSize, MinNativeSize);
            var targetW = MathF.Max(MinNativeSize, renderSize.Width);
            var targetH = MathF.Max(MinNativeSize, renderSize.Height);

            var maxW = (int)MathF.Floor(targetW);
            var maxH = (int)MathF.Floor(targetH);
            var safeLeft = Math.Max(0, Math.Min(left, maxW));
            var safeTop = Math.Max(0, Math.Min(top, maxH));
            var safeRight = Math.Max(0, Math.Min(right, maxW - safeLeft));
            var safeBottom = Math.Max(0, Math.Min(bottom, maxH - safeTop));

            var resolvedImagePath = ResolveControlImagePath(imagePath);
            c.Image = resolvedImagePath;
            if (safeLeft + safeRight >= maxW || safeTop + safeBottom >= maxH)
            {
                // For tiny targets, forcing 9-slice may collapse; fallback to plain image draw.
                c.SlicedEdges = new Thickness(0, 0, 0, 0);
                return;
            }

            c.SlicedEdges = new Thickness(safeLeft, safeTop, safeRight, safeBottom);

            if (EnableImagePipelineInfoLogs)
            {
                System.GC.KeepAlive(0);
            }
        }
    }

    public void SetTintColor(object control, Color color)
    {
        if (control is Control c)
        {
            if (TryApplyNativeTint(c, color))
            {
                return;
            }

            if (color != Color.White &&
                _tintUnsupportedLoggedTypes.Add(c.GetType()) &&
                _tintDiagLogCount < TintDiagLogLimit)
            {
                _tintDiagLogCount++;
                System.GC.KeepAlive(0);
            }
        }
    }

    public void SetImageRegion(object control, string atlasPath, RectangleF region, bool rotated)
    {
        if (control is Canvas canvas && !string.IsNullOrEmpty(atlasPath))
        {
            if (!IsPositiveFinite(region.Width) || !IsPositiveFinite(region.Height))
            {
                LogInvalidDrawDiag($"[FGUI][DRAW-GUARD] skip SetImageRegion due invalid source region atlas={atlasPath} region={region}");
                return;
            }

            var resolvedImagePath = ResolveControlImagePath(atlasPath);
            if (!TryGetCanvasImage(resolvedImagePath, out var image))
            {
                return;
            }

            var state = EnsureCanvasImageState(canvas);
            state.ResolvedPath = resolvedImagePath;
            state.CurrentImage = image;
            state.RenderMode = CanvasRenderMode.AtlasRegion;
            state.SourceRegion = region;
            state.Rotated = rotated;
            state.AtlasSliced = null;
            state.FillColor = Color.Transparent;

            if (EnableImagePipelineInfoLogs)
            {
                System.GC.KeepAlive(0);
            }
        }
        else if (control is Control c && !string.IsNullOrEmpty(atlasPath))
        {
            if (!TryEnsureImageTargetSize(c, atlasPath, "SetImageRegion-Control"))
            {
                return;
            }

            // Fallback: just set the atlas image (will show whole image)
            c.Image = atlasPath;
            if (EnableImagePipelineInfoLogs)
            {
                System.GC.KeepAlive(0);
            }
        }
    }

    /// <summary>
    /// Set sliced (nine-patch) image from atlas with proper region cropping
    /// </summary>
    public void SetSlicedImageFromAtlas(object control, string atlasPath, RectangleF spriteRect, 
        int left, int right, int top, int bottom, float destWidth, float destHeight)
    {
        if (control is Canvas canvas && !string.IsNullOrEmpty(atlasPath))
        {
            float sx = spriteRect.X;
            float sy = spriteRect.Y;
            float sw = spriteRect.Width;
            float sh = spriteRect.Height;
            if (sw < MinNativeSize || sh < MinNativeSize)
            {
                LogInvalidDrawDiag($"[FGUI][DRAW-GUARD] skip SetSlicedImageFromAtlas due source<=0 atlas={atlasPath} sprite={spriteRect}");
                return;
            }

            // Sanitize scale9 edges so they always fit inside source sprite.
            int safeLeft = Math.Max(0, Math.Min(left, (int)MathF.Floor(sw)));
            int safeTop = Math.Max(0, Math.Min(top, (int)MathF.Floor(sh)));
            int safeRight = Math.Max(0, Math.Min(right, (int)MathF.Floor(sw) - safeLeft));
            int safeBottom = Math.Max(0, Math.Min(bottom, (int)MathF.Floor(sh) - safeTop));

            var resolvedImagePath = ResolveControlImagePath(atlasPath);
            if (!TryGetCanvasImage(resolvedImagePath, out var image))
            {
                return;
            }

            var fallbackWidth = MathF.Max(1f, destWidth);
            var fallbackHeight = MathF.Max(1f, destHeight);
            var state = EnsureCanvasImageState(canvas);
            state.ResolvedPath = resolvedImagePath;
            state.CurrentImage = image;
            state.RenderMode = CanvasRenderMode.AtlasSliced;
            state.SourceRegion = RectangleF.Empty;
            state.Rotated = false;
            state.AtlasSliced = new AtlasSlicedRenderState
            {
                SpriteRect = spriteRect,
                Left = safeLeft,
                Right = safeRight,
                Top = safeTop,
                Bottom = safeBottom,
                FallbackWidth = fallbackWidth,
                FallbackHeight = fallbackHeight
            };
            state.FillColor = Color.Transparent;

            if (EnableImagePipelineInfoLogs)
            {
                System.GC.KeepAlive(0);
            }
        }
        else if (control is Control c && !string.IsNullOrEmpty(atlasPath))
        {
            if (!TryEnsureImageTargetSize(c, atlasPath, "SetSlicedImageFromAtlas-Control"))
            {
                return;
            }

            // Fallback for non-Canvas controls - just set image with SlicedEdges
            c.Image = atlasPath;
            c.SlicedEdges = new Thickness(left, top, right, bottom);
            if (EnableImagePipelineInfoLogs)
            {
                System.GC.KeepAlive(0);
            }
        }
    }

    public void SetText(object control, string text)
    {
        if (control is Input input)
            input.Text = text;
        else if (control is Label label)
            label.Text = text;
    }

    public void SetTextColor(object control, Color color)
    {
        if (control is Input input)
            input.TextColor = color;
        else if (control is Label label)
            label.TextColor = color;
    }

    public void SetFontSize(object control, int size)
    {
        if (control is Input input)
            input.FontSize = size;
        else if (control is Label label)
            label.FontSize = size;
    }

    public void SetFontName(object control, string fontFamily)
    {
        // fontFamily 是字体 family（如 "font/fanyuan"），须已在 ref/fontref.txt 预加载。
        // UTF8String 支持从 string 隐式转换（与 Control.Image 同）。
        if (string.IsNullOrEmpty(fontFamily))
            return;
        if (control is Input input)
        {
            input.Font = fontFamily;
            // Game.Logger.LogInformation("[FGUI][FONT-DIAG] set Input.Font family={Family} readback={Read}", fontFamily, input.Font.ToString());
        }
        else if (control is Label label)
        {
            label.Font = fontFamily;
            // Game.Logger.LogInformation("[FGUI][FONT-DIAG] set Label.Font family={Family} readback={Read}", fontFamily, label.Font.ToString());
        }
        else
        {
            Game.Logger.LogWarning("[FGUI][FONT-DIAG] control is NOT Label/Input, type={Type} family={Family}", control?.GetType().Name, fontFamily);
        }
    }

    public void SetBold(object control, bool bold)
    {
        if (control is Input input)
            input.Bold = bold;
        else if (control is Label label)
            label.Bold = bold;
    }

    public void SetItalic(object control, bool italic)
    {
        if (control is Input input)
            input.Italic = italic;
        else if (control is Label label)
            label.Italic = italic;
    }

    public void SetTextAlign(object control, TextAlign align)
    {
        if (control is Control c)
        {
            var hca = align switch
            {
                TextAlign.Left => HorizontalContentAlignment.Left,
                TextAlign.Center => HorizontalContentAlignment.Center,
                TextAlign.Right => HorizontalContentAlignment.Right,
                _ => HorizontalContentAlignment.Left
            };
            c.HorizontalContentAlignment = hca;
        }
    }

    public void SetTextVerticalAlign(object control, TextVerticalAlign align)
    {
        if (control is Control c)
        {
            var vca = align switch
            {
                TextVerticalAlign.Top => VerticalContentAlignment.Top,
                TextVerticalAlign.Middle => VerticalContentAlignment.Center,
                TextVerticalAlign.Bottom => VerticalContentAlignment.Bottom,
                _ => VerticalContentAlignment.Top
            };
            c.VerticalContentAlignment = vca;
        }
    }
    
    public void SetInputPlaceholder(object control, string placeholder)
    {
        if (control is Input input)
        {
            var type = input.GetType();
            if (TrySetStringProperty(type, input, "Placeholder", placeholder) ||
                TrySetStringProperty(type, input, "PromptText", placeholder) ||
                TrySetStringProperty(type, input, "Hint", placeholder))
            {
                return;
            }
        }

        System.GC.KeepAlive(0);
    }
    
    public void SetInputPassword(object control, bool isPassword)
    {
        if (control is Input input)
        {
            var type = input.GetType();
            if (TrySetBoolProperty(type, input, "Password", isPassword) ||
                TrySetBoolProperty(type, input, "IsPassword", isPassword) ||
                TrySetBoolProperty(type, input, "PasswordMode", isPassword))
            {
                return;
            }
        }

        System.GC.KeepAlive(0);
    }
    
    public void SetInputMaxLength(object control, int maxLength)
    {
        if (control is Input input)
        {
            var type = input.GetType();
            if (TrySetIntProperty(type, input, "MaxLength", maxLength) ||
                TrySetIntProperty(type, input, "TextMaxLength", maxLength))
            {
                return;
            }
        }

        System.GC.KeepAlive(0);
    }
    
    public void SetInputEditable(object control, bool editable)
    {
        if (control is Input input)
        {
            var type = input.GetType();
            if (TrySetBoolProperty(type, input, "Editable", editable) ||
                TrySetBoolProperty(type, input, "IsReadOnly", !editable))
            {
                return;
            }
        }

        if (control is Control c)
        {
            if (editable)
                c.Enable();
            else
                c.Disable();
        }
    }

    public void OnInputTextChanged(object control, Action<string> handler)
    {
        if (control is Input input)
        {
            input.OnInputTextChanged += (_, e) => handler(e.Text ?? string.Empty);
        }
    }

    public void SetCornerRadius(object control, float radius)
    {
        if (control is Control c)
            c.CornerRadius(radius);
    }

    public void SetZIndex(object control, int zIndex)
    {
        if (control is Control c)
            c.ZIndex(zIndex);
    }

    public void SetClipContent(object control, bool clip)
    {
        if (control is not Control c)
        {
            return;
        }

        var type = c.GetType();
        var applied = false;
        applied |= TrySetBoolProperty(type, c, "ClipContent", clip);
        applied |= TrySetBoolProperty(type, c, "ClipToBounds", clip);
        applied |= TrySetBoolProperty(type, c, "IsClipped", clip);
        applied |= TrySetBoolProperty(type, c, "MaskChildren", clip);
        applied |= TrySetBoolProperty(type, c, "UseScissor", clip);
        applied |= TrySetEnumProperty(type, c, "Overflow", clip ? "Hidden" : "Visible");
        applied |= TrySetEnumProperty(type, c, "ContentOverflow", clip ? "Hidden" : "Visible");
        applied |= TryInvokeBoolMethod(type, c, "SetClipContent", clip);
        applied |= TryInvokeBoolMethod(type, c, "SetClipToBounds", clip);
        applied |= TryInvokeBoolMethod(type, c, "SetClip", clip);
        applied |= TryInvokeBoolMethod(type, c, "Clip", clip);

        if (!applied && clip && _clipDiagLogCount < ClipDiagLogLimit)
        {
            _clipDiagLogCount++;
            System.GC.KeepAlive(0);
        }
    }

    public void SetMaskControl(object control, object? maskControl, bool inverted)
    {
        if (control is not Control c)
        {
            return;
        }

        var type = c.GetType();
        var applied = false;
        var hasMask = maskControl != null;
        applied |= TrySetObjectProperty(type, c, "Mask", maskControl);
        applied |= TrySetObjectProperty(type, c, "MaskControl", maskControl);
        applied |= TrySetObjectProperty(type, c, "ClipMask", maskControl);
        applied |= TrySetObjectProperty(type, c, "StencilMask", maskControl);
        applied |= TrySetObjectProperty(type, c, "ContentMask", maskControl);
        applied |= TryInvokeObjectMethod(type, c, "SetMask", maskControl);
        applied |= TryInvokeObjectMethod(type, c, "SetMaskControl", maskControl);
        applied |= TryInvokeObjectMethod(type, c, "SetClipMask", maskControl);
        applied |= TryInvokeObjectMethod(type, c, "SetContentMask", maskControl);
        applied |= TryInvokeObjectBoolMethod(type, c, "SetMask", maskControl, inverted);
        applied |= TryInvokeObjectBoolMethod(type, c, "SetMaskControl", maskControl, inverted);
        applied |= TryInvokeObjectBoolMethod(type, c, "SetClipMask", maskControl, inverted);
        applied |= TryInvokeBoolObjectMethod(type, c, "SetMask", inverted, maskControl);
        applied |= TryInvokeBoolObjectMethod(type, c, "SetMaskControl", inverted, maskControl);
        applied |= TrySetBoolProperty(type, c, "UseMask", hasMask);
        applied |= TrySetBoolProperty(type, c, "MaskEnabled", hasMask);
        applied |= TrySetBoolProperty(type, c, "EnableMask", hasMask);
        applied |= TrySetBoolProperty(type, c, "MaskInverted", inverted);
        applied |= TrySetBoolProperty(type, c, "InvertMask", inverted);
        applied |= TrySetBoolProperty(type, c, "ReverseMask", inverted);

        if (!applied && hasMask && _maskDiagLogCount < MaskDiagLogLimit)
        {
            _maskDiagLogCount++;
            // 星火原生控件没有“用任意控件/图形做遮罩”的 API（只有矩形 ClipContent + CornerRadius），
            // 上面所有反射尝试都会落空。改成真实告警，避免遮罩静默失效。
            Game.Logger.LogWarning(
                "[FGUI][MASK] 原生控件 {Type} 不支持自定义(图形/控件)遮罩，本次遮罩未生效；如需裁剪请改用矩形 ClipContent。inverted={Inverted}",
                c.GetType().Name,
                inverted);
        }
    }

    public void ConfigureScrollable(object control, bool enabled, bool horizontal)
    {
        if (control is PanelScrollable panelScrollable)
        {
            panelScrollable.ScrollEnabled = enabled;
            panelScrollable.ScrollOrientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
            return;
        }

        if (control is not Control c)
        {
            return;
        }

        var type = c.GetType();
        TrySetBoolProperty(type, c, "ScrollEnabled", enabled);
        TrySetEnumProperty(type, c, "ScrollOrientation", horizontal ? "Horizontal" : "Vertical");
    }

    public void SetScrollValue(object control, float value)
    {
        var clamped = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Clamp(value, 0f, 1f);
        if (control is PanelScrollable panelScrollable)
        {
            panelScrollable.ScrollBarValue = clamped;
            return;
        }

        if (control is not Control c)
        {
            return;
        }

        TrySetFloatProperty(c.GetType(), c, "ScrollBarValue", clamped);
    }

    public void OnScrollChanged(object control, Action<float> handler)
    {
        if (control is not PanelScrollable panelScrollable)
        {
            return;
        }

        panelScrollable.OnScrollChanged += (_, args) =>
        {
            var value = float.IsNaN(args.ScrollValue) || float.IsInfinity(args.ScrollValue)
                ? 0f
                : Math.Clamp(args.ScrollValue, 0f, 1f);
            handler(value);
        };
    }

    private static float ResolveControlDimension(Control control, string propertyName, float fallback)
    {
        var prop = control.GetType().GetProperty(propertyName);
        if (prop?.CanRead == true)
        {
            var value = prop.GetValue(control);
            if (TryConvertToFloat(value, out var dim) && dim >= MinNativeSize)
            {
                return dim;
            }
        }

        return fallback;
    }

    private static PointF ResolveControlPosition(Control control)
    {
        var x = ResolveControlDimension(control, "X", 0f);
        x = ResolveControlDimension(control, "Left", x);
        var y = ResolveControlDimension(control, "Y", 0f);
        y = ResolveControlDimension(control, "Top", y);
        return new PointF(x, y);
    }

    private void TrackControlPosition(object control, float x, float y)
    {
        _controlPositions[control] = new PointF(x, y);
    }

    private void TrackControlSize(object control, float width, float height)
    {
        if (width < MinNativeSize || height < MinNativeSize)
        {
            return;
        }

        _controlSizes[control] = new SizeF(width, height);
    }

    private CanvasImageRenderState EnsureCanvasImageState(Canvas canvas)
    {
        if (_canvasImageStates.TryGetValue(canvas, out var existing))
        {
            return existing;
        }

        var state = new CanvasImageRenderState();
        _canvasImageStates[canvas] = state;
        canvas.OnRender += (sender, e) =>
        {
            canvas.ResetState();
            switch (state.RenderMode)
            {
                case CanvasRenderMode.FilledEllipse:
                    DrawFilledEllipse(canvas, state);
                    break;
                case CanvasRenderMode.AtlasRegion:
                    if (!state.CurrentImage.HasValue)
                    {
                        return;
                    }

                    DrawAtlasRegion(canvas, state.CurrentImage.Value, state);
                    break;
                case CanvasRenderMode.AtlasSliced:
                    if (!state.CurrentImage.HasValue)
                    {
                        return;
                    }

                    DrawAtlasSliced(canvas, state.CurrentImage.Value, state);
                    break;
                default:
                    if (!state.CurrentImage.HasValue)
                    {
                        return;
                    }

                    DrawPlainImage(canvas, state.CurrentImage.Value);
                    break;
            }
        };
        return state;
    }

    private void DrawFilledEllipse(Canvas canvas, CanvasImageRenderState state)
    {
        var renderSize = ResolveRenderSize(canvas, MinNativeSize, MinNativeSize);
        var width = renderSize.Width;
        var height = renderSize.Height;
        if (!IsPositiveFinite(width) || !IsPositiveFinite(height))
        {
            return;
        }

        var radiusX = width * 0.5f;
        var radiusY = height * 0.5f;
        if (!IsPositiveFinite(radiusX) || !IsPositiveFinite(radiusY))
        {
            return;
        }

        canvas.FillPaint = state.FillColor;
        canvas.FillEllipse(radiusX, radiusY, radiusX, radiusY);
    }

    private bool TryGetCanvasImage(string resolvedImagePath, out Image image)
    {
        if (_canvasImageCache.TryGetValue(resolvedImagePath, out image))
        {
            return true;
        }

        try
        {
            image = new Image(resolvedImagePath);
            _canvasImageCache[resolvedImagePath] = image;
            return true;
        }
        catch (Exception ex)
        {
            System.GC.KeepAlive(0);
            image = default;
            return false;
        }
    }

    private void DrawPlainImage(Canvas canvas, Image image)
    {
        var renderSize = ResolveRenderSize(canvas, MinNativeSize, MinNativeSize);
        var destWidth = renderSize.Width;
        var destHeight = renderSize.Height;
        if (destWidth < MinNativeSize || destHeight < MinNativeSize)
        {
            return;
        }

        canvas.DrawImage(image, 0f, 0f, destWidth, destHeight);
    }

    private void DrawAtlasRegion(Canvas canvas, Image image, CanvasImageRenderState state)
    {
        var region = state.SourceRegion;
        if (!IsPositiveFinite(region.Width) || !IsPositiveFinite(region.Height))
        {
            return;
        }

        var fallbackWidth = MathF.Max(1f, region.Width);
        var fallbackHeight = MathF.Max(1f, region.Height);
        var renderSize = ResolveRenderSize(canvas, fallbackWidth, fallbackHeight);
        var destWidth = renderSize.Width;
        var destHeight = renderSize.Height;
        if (destWidth < MinNativeSize || destHeight < MinNativeSize)
        {
            LogInvalidDrawDiag($"[FGUI][DRAW-GUARD] skip DrawAtlasRegion due target<=0 image={state.ResolvedPath} dest={destWidth}x{destHeight} region={region}");
            return;
        }

        var destRect = new RectangleF(0, 0, destWidth, destHeight);
        if (!IsPositiveFinite(destRect.Width) || !IsPositiveFinite(destRect.Height))
        {
            LogInvalidDrawDiag($"[FGUI][DRAW-GUARD] skip DrawAtlasRegion due invalid destRect image={state.ResolvedPath} destRect={destRect} region={region}");
            return;
        }

        canvas.DrawImage(image, region, destRect);
    }

    private void DrawAtlasSliced(Canvas canvas, Image image, CanvasImageRenderState state)
    {
        var sliced = state.AtlasSliced;
        if (sliced == null)
        {
            return;
        }

        var spriteRect = sliced.SpriteRect;
        var sw = spriteRect.Width;
        var sh = spriteRect.Height;
        if (!IsPositiveFinite(sw) || !IsPositiveFinite(sh))
        {
            return;
        }

        var renderSize = ResolveRenderSize(canvas, sliced.FallbackWidth, sliced.FallbackHeight);
        // 关键：把渲染尺寸吸附到整数像素。九宫格用 9 次独立 DrawImage 合成，
        // 若边界为浮点，相邻块各自抗锯齿会露底形成接缝线条。取整后各块边界共享同一整数像素、无缝。
        // （官方 FairyGUI 用共享顶点的整块 mesh 天然对齐；此处在 Canvas API 下等效实现。）
        var renderWidth = MathF.Round(renderSize.Width);
        var renderHeight = MathF.Round(renderSize.Height);
        if (renderWidth < MinNativeSize || renderHeight < MinNativeSize)
        {
            LogInvalidDrawDiag($"[FGUI][DRAW-GUARD] skip DrawAtlasSliced due target<=0 image={state.ResolvedPath} render={renderWidth}x{renderHeight} sprite={spriteRect}");
            return;
        }

        if (renderWidth < MinCanvasDrawSize || renderHeight < MinCanvasDrawSize)
        {
            LogInvalidDrawDiag($"[FGUI][DRAW-GUARD] skip DrawAtlasSliced due tiny target image={state.ResolvedPath} render={renderWidth}x{renderHeight} sprite={spriteRect}");
            return;
        }

        var safeLeft = sliced.Left;
        var safeRight = sliced.Right;
        var safeTop = sliced.Top;
        var safeBottom = sliced.Bottom;

        var drawLeft = MathF.Min(safeLeft, renderWidth);
        var drawRight = MathF.Min(safeRight, MathF.Max(0f, renderWidth - drawLeft));
        var drawTop = MathF.Min(safeTop, renderHeight);
        var drawBottom = MathF.Min(safeBottom, MathF.Max(0f, renderHeight - drawTop));
        var dstCenterW = MathF.Max(0f, renderWidth - drawLeft - drawRight);
        var dstCenterH = MathF.Max(0f, renderHeight - drawTop - drawBottom);

        if (drawLeft != safeLeft || drawRight != safeRight || drawTop != safeTop || drawBottom != safeBottom)
        {
            LogInvalidDrawDiag($"[FGUI][DRAW-GUARD] compact DrawAtlasSliced image={state.ResolvedPath} render={renderWidth}x{renderHeight} safe={safeLeft}/{safeRight}/{safeTop}/{safeBottom} draw={drawLeft:0.###}/{drawRight:0.###}/{drawTop:0.###}/{drawBottom:0.###}");
        }

        var sx = spriteRect.X;
        var sy = spriteRect.Y;
        var srcCenterW = sw - safeLeft - safeRight;
        var srcCenterH = sh - safeTop - safeBottom;

        static bool CanDraw(float w, float h) => IsPositiveFinite(w) && IsPositiveFinite(h) && w >= MinCanvasDrawSize && h >= MinCanvasDrawSize;

        var rightX = renderWidth - drawRight;
        var bottomY = renderHeight - drawBottom;

        if (CanDraw(drawLeft, drawTop))
            canvas.DrawImage(image, sx, sy, drawLeft, drawTop, 0, 0, drawLeft, drawTop);
        if (CanDraw(srcCenterW, drawTop) && CanDraw(dstCenterW, drawTop))
            canvas.DrawImage(image, sx + safeLeft, sy, srcCenterW, drawTop, drawLeft, 0, dstCenterW, drawTop);
        if (CanDraw(drawRight, drawTop))
            canvas.DrawImage(image, sx + sw - drawRight, sy, drawRight, drawTop, rightX, 0, drawRight, drawTop);

        if (CanDraw(drawLeft, srcCenterH) && CanDraw(drawLeft, dstCenterH))
            canvas.DrawImage(image, sx, sy + safeTop, drawLeft, srcCenterH, 0, drawTop, drawLeft, dstCenterH);
        if (CanDraw(srcCenterW, srcCenterH) && CanDraw(dstCenterW, dstCenterH))
            canvas.DrawImage(image, sx + safeLeft, sy + safeTop, srcCenterW, srcCenterH, drawLeft, drawTop, dstCenterW, dstCenterH);
        if (CanDraw(drawRight, srcCenterH) && CanDraw(drawRight, dstCenterH))
            canvas.DrawImage(image, sx + sw - drawRight, sy + safeTop, drawRight, srcCenterH, rightX, drawTop, drawRight, dstCenterH);

        if (CanDraw(drawLeft, drawBottom))
            canvas.DrawImage(image, sx, sy + sh - drawBottom, drawLeft, drawBottom, 0, bottomY, drawLeft, drawBottom);
        if (CanDraw(srcCenterW, drawBottom) && CanDraw(dstCenterW, drawBottom))
            canvas.DrawImage(image, sx + safeLeft, sy + sh - drawBottom, srcCenterW, drawBottom, drawLeft, bottomY, dstCenterW, drawBottom);
        if (CanDraw(drawRight, drawBottom))
            canvas.DrawImage(image, sx + sw - drawRight, sy + sh - drawBottom, drawRight, drawBottom, rightX, bottomY, drawRight, drawBottom);
    }

    private SizeF ResolveRenderSize(Control control, float fallbackWidth, float fallbackHeight)
    {
        var width = fallbackWidth;
        var height = fallbackHeight;
        var lockZeroWidth = false;
        var lockZeroHeight = false;

        if (_requestedControlSizes.TryGetValue(control, out var requested))
        {
            if (requested.Width < MinNativeSize)
            {
                width = 0f;
                lockZeroWidth = true;
            }
            else
            {
                width = requested.Width;
            }

            if (requested.Height < MinNativeSize)
            {
                height = 0f;
                lockZeroHeight = true;
            }
            else
            {
                height = requested.Height;
            }
        }

        if (_controlSizes.TryGetValue(control, out var tracked))
        {
            if (!lockZeroWidth && tracked.Width >= MinNativeSize)
            {
                width = tracked.Width;
            }

            if (!lockZeroHeight && tracked.Height >= MinNativeSize)
            {
                height = tracked.Height;
            }
        }

        if (!lockZeroWidth)
        {
            width = ResolveControlDimension(control, "Width", width);
            width = ResolveControlDimension(control, "ActualWidth", width);
        }

        if (!lockZeroHeight)
        {
            height = ResolveControlDimension(control, "Height", height);
            height = ResolveControlDimension(control, "ActualHeight", height);
        }

        return new SizeF(width, height);
    }

    private static bool TryConvertToFloat(object? value, out float result)
    {
        switch (value)
        {
            case float f:
                result = f;
                return true;
            case double d:
                result = (float)d;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            default:
                result = 0f;
                return false;
        }
    }

    private bool TryApplyNativeFlip(Control control, bool flipX, bool flipY)
    {
        var type = control.GetType();
        if (!_flipTypedUnsupportedTypes.Contains(type))
        {
            try
            {
                control.ImageFlipX = flipX;
                control.ImageFlipY = flipY;
                return true;
            }
            catch
            {
                _flipTypedUnsupportedTypes.Add(type);
            }
        }

        var applied = false;
        var sx = flipX ? -1f : 1f;
        var sy = flipY ? -1f : 1f;

        applied |= TrySetBoolProperty(type, control, "FlipX", flipX);
        applied |= TrySetBoolProperty(type, control, "FlipY", flipY);
        applied |= TrySetBoolProperty(type, control, "MirrorX", flipX);
        applied |= TrySetBoolProperty(type, control, "MirrorY", flipY);
        applied |= TrySetFloatProperty(type, control, "ScaleX", sx);
        applied |= TrySetFloatProperty(type, control, "ScaleY", sy);
        applied |= TrySetFloatProperty(type, control, "ImageScaleX", sx);
        applied |= TrySetFloatProperty(type, control, "ImageScaleY", sy);

        if (TrySetEnumProperty(type, control, "Flip", ResolveFlipEnumName(flipX, flipY)))
        {
            applied = true;
        }

        var twoArgMethod = type.GetMethod("SetFlip", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(bool), typeof(bool) }, null)
                           ?? type.GetMethod("Flip", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(bool), typeof(bool) }, null);
        if (twoArgMethod != null)
        {
            twoArgMethod.Invoke(control, new object[] { flipX, flipY });
            applied = true;
        }

        var scaleMethod = type.GetMethod("SetScale", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(float), typeof(float) }, null)
                        ?? type.GetMethod("Scale", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(float), typeof(float) }, null);
        if (scaleMethod != null)
        {
            scaleMethod.Invoke(control, new object[] { sx, sy });
            applied = true;
        }

        return applied || (!flipX && !flipY);
    }

    private static bool TryApplyNativeScale(Control control, float scaleX, float scaleY)
    {
        // Spark Panel exposes several scale-like members via reflection, but they do not
        // produce visible transform for FGUI host panels. Force fallback size/position path.
        if (control is Panel)
        {
            return false;
        }

        var type = control.GetType();
        var applied = false;

        applied |= TrySetFloatProperty(type, control, "ScaleX", scaleX);
        applied |= TrySetFloatProperty(type, control, "ScaleY", scaleY);
        applied |= TrySetFloatProperty(type, control, "ImageScaleX", scaleX);
        applied |= TrySetFloatProperty(type, control, "ImageScaleY", scaleY);

        var scaleMethod = type.GetMethod("SetScale", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(float), typeof(float) }, null)
                        ?? type.GetMethod("Scale", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(float), typeof(float) }, null);
        if (scaleMethod != null)
        {
            scaleMethod.Invoke(control, new object[] { scaleX, scaleY });
            applied = true;
        }

        return applied;
    }

    private void LogScaleRoute(Control control, float scaleX, float scaleY, string route)
    {
        // Skip identity scale logs to keep diagnostics focused on real downEffect/flip transforms.
        if (MathF.Abs(scaleX - 1f) < 0.0001f && MathF.Abs(scaleY - 1f) < 0.0001f)
        {
            return;
        }

        if (_scaleDiagLogCount >= ScaleDiagLogLimit)
        {
            return;
        }

        _scaleDiagLogCount++;
        System.GC.KeepAlive(0);
    }

    private bool TryApplyNativeTint(Control control, Color color)
    {
        var type = control.GetType();
        if (!_tintCapabilityCache.TryGetValue(type, out var capability))
        {
            capability = ResolveTintCapability(type);
            _tintCapabilityCache[type] = capability;
        }

        if (capability.Unsupported)
        {
            return false;
        }

        if (TryApplyTintWithCapability(control, capability, color))
        {
            return true;
        }

        // Capability may drift for derived controls; retry one-time resolve for this type.
        capability = ResolveTintCapability(type);
        _tintCapabilityCache[type] = capability;
        return TryApplyTintWithCapability(control, capability, color);
    }

    private static TintCapability ResolveTintCapability(Type type)
    {
        foreach (var methodName in new[] { "SetTintColor", "SetTint", "Tint", "SetImageColor", "SetColor" })
        {
            var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                continue;
            }

            if (!TryConvertColorValue(parameters[0].ParameterType, Color.White, out _))
            {
                continue;
            }

            return new TintCapability { Method = method };
        }

        foreach (var propertyName in new[] { "Tint", "TintColor", "ImageColor", "Color", "MultiplyColor" })
        {
            var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (prop?.CanWrite != true)
            {
                continue;
            }

            if (!TryConvertColorValue(prop.PropertyType, Color.White, out _))
            {
                continue;
            }

            return new TintCapability { Property = prop };
        }

        return new TintCapability { Unsupported = true };
    }

    private static bool TryApplyTintWithCapability(Control control, TintCapability capability, Color color)
    {
        if (capability.Unsupported)
        {
            return false;
        }

        if (capability.Method != null)
        {
            var parameters = capability.Method.GetParameters();
            if (parameters.Length == 1 &&
                TryConvertColorValue(parameters[0].ParameterType, color, out var arg))
            {
                capability.Method.Invoke(control, new[] { arg });
                return true;
            }
        }

        if (capability.Property?.CanWrite == true &&
            TryConvertColorValue(capability.Property.PropertyType, color, out var value))
        {
            capability.Property.SetValue(control, value);
            return true;
        }

        return false;
    }

    private static ImageFillCapability ResolveImageFillCapability(Type type)
    {
        var fillMethod = type.GetProperty("FillMethod", BindingFlags.Instance | BindingFlags.Public);
        var fillOrigin = type.GetProperty("FillOrigin", BindingFlags.Instance | BindingFlags.Public);
        var fillClockwise = type.GetProperty("FillClockwise", BindingFlags.Instance | BindingFlags.Public);
        var fillAmount = type.GetProperty("FillAmount", BindingFlags.Instance | BindingFlags.Public);
        var progressValue = type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public) ??
                            type.GetProperty("Progress", BindingFlags.Instance | BindingFlags.Public);
        var progressionMode = type.GetProperty("ProgressionMode", BindingFlags.Instance | BindingFlags.Public);
        var progressRotation = type.GetProperty("ProgressRotation", BindingFlags.Instance | BindingFlags.Public);

        var canFillMethod = fillMethod?.CanWrite == true;
        var canFillOrigin = fillOrigin?.CanWrite == true;
        var canFillClockwise = fillClockwise?.CanWrite == true;
        var canFillAmount = fillAmount?.CanWrite == true;
        var canProgressValue = progressValue?.CanWrite == true;
        var canProgressionMode = progressionMode?.CanWrite == true;
        var canProgressRotation = progressRotation?.CanWrite == true;

        if (!canFillMethod && !canFillOrigin && !canFillClockwise && !canFillAmount &&
            !canProgressValue && !canProgressionMode && !canProgressRotation)
        {
            return new ImageFillCapability { Unsupported = true };
        }

        return new ImageFillCapability
        {
            FillMethod = canFillMethod ? fillMethod : null,
            FillOrigin = canFillOrigin ? fillOrigin : null,
            FillClockwise = canFillClockwise ? fillClockwise : null,
            FillAmount = canFillAmount ? fillAmount : null,
            ProgressValue = canProgressValue ? progressValue : null,
            ProgressionMode = canProgressionMode ? progressionMode : null,
            ProgressRotation = canProgressRotation ? progressRotation : null,
            Unsupported = false,
        };
    }

    private static bool TryAssignFillValue(Control control, PropertyInfo? property, object value)
    {
        if (property?.CanWrite != true)
        {
            return false;
        }

        var targetType = property.PropertyType;
        var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        try
        {
            if (actualType.IsEnum)
            {
                object enumValue;
                if (value.GetType().IsEnum)
                {
                    enumValue = Enum.ToObject(actualType, Convert.ToInt32(value));
                }
                else
                {
                    var numeric = Convert.ToInt32(value);
                    enumValue = Enum.ToObject(actualType, numeric);
                }

                property.SetValue(control, enumValue);
                return true;
            }

            var converted = Convert.ChangeType(value, actualType);
            property.SetValue(control, converted);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ConstructorInfo? ResolveProgressControlConstructor()
    {
        if (ProgressControlType == null || !typeof(Control).IsAssignableFrom(ProgressControlType))
        {
            return null;
        }

        return ProgressControlType.GetConstructor(
                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   binder: null,
                   types: Type.EmptyTypes,
                   modifiers: null)
               ?? ProgressControlType.GetConstructor(Type.EmptyTypes);
    }

    private static bool TryApplyProgressFill(
        Control control,
        ImageFillCapability capability,
        FillMethod fillMethod,
        int fillOrigin,
        bool fillClockwise,
        float fillAmount)
    {
        var appliedAny = false;

        var modeName = ResolveProgressionMode(fillMethod, fillOrigin, fillClockwise);
        if (capability.ProgressionMode?.PropertyType.IsEnum == true && !string.IsNullOrWhiteSpace(modeName))
        {
            appliedAny |= TryAssignEnumValueByName(control, capability.ProgressionMode, modeName!);
        }

        if (capability.ProgressRotation != null &&
            TryResolveProgressRotation(fillMethod, fillOrigin, out var rotation))
        {
            appliedAny |= TryAssignFillValue(control, capability.ProgressRotation, rotation);
        }

        if (capability.ProgressValue != null)
        {
            appliedAny |= TryAssignFillValue(control, capability.ProgressValue, Math.Clamp(fillAmount, 0f, 1f));
        }

        return appliedAny;
    }

    private static string? ResolveProgressionMode(FillMethod fillMethod, int fillOrigin, bool fillClockwise)
    {
        return fillMethod switch
        {
            FillMethod.Horizontal => fillOrigin == (int)OriginHorizontal.Right ? "RightToLeft" : "LeftToRight",
            FillMethod.Vertical => fillOrigin == (int)OriginVertical.Bottom ? "BottomToTop" : "TopToBottom",
            // FGUI radial fill and native Progress use opposite winding in current adapter coordinate convention.
            FillMethod.Radial90 or FillMethod.Radial180 or FillMethod.Radial360 => fillClockwise ? "CounterClockwise" : "Clockwise",
            _ => null
        };
    }

    private static bool TryResolveProgressRotation(FillMethod fillMethod, int fillOrigin, out float rotation)
    {
        // Match FGUI origin semantics with native Progress rotation.
        switch (fillMethod)
        {
            case FillMethod.Radial360:
                // Mirror vertical origin to align FGUI texture-space with native Progress space.
                var radial360Origin = fillOrigin switch
                {
                    (int)Origin360.Top => (int)Origin360.Bottom,
                    (int)Origin360.Bottom => (int)Origin360.Top,
                    _ => fillOrigin
                };

                // Native Progress: 0=Top, 90=Right, 180=Bottom, -90=Left.
                rotation = radial360Origin switch
                {
                    (int)Origin360.Top => 0f,
                    (int)Origin360.Right => 90f,
                    (int)Origin360.Bottom => 180f,
                    (int)Origin360.Left => -90f,
                    _ => 0f
                };
                return true;
            case FillMethod.Radial180:
                rotation = fillOrigin switch
                {
                    (int)Origin180.Top => -90f,
                    (int)Origin180.Bottom => 90f,
                    (int)Origin180.Left => 180f,
                    _ => 0f,
                };
                return true;
            case FillMethod.Radial90:
                rotation = fillOrigin switch
                {
                    (int)Origin90.TopLeft => 180f,
                    (int)Origin90.TopRight => -90f,
                    (int)Origin90.BottomLeft => 90f,
                    _ => 0f,
                };
                return true;
            default:
                rotation = 0f;
                return false;
        }
    }

    private static bool TryAssignEnumValueByName(Control control, PropertyInfo? property, string enumName)
    {
        if (property?.CanWrite != true)
        {
            return false;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (!targetType.IsEnum)
        {
            return false;
        }

        try
        {
            var value = Enum.Parse(targetType, enumName, ignoreCase: true);
            property.SetValue(control, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetBoolProperty(Type type, object instance, string propertyName, bool value)
    {
        var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (prop?.CanWrite != true || prop.PropertyType != typeof(bool))
        {
            return false;
        }

        prop.SetValue(instance, value);
        return true;
    }

    private static bool TrySetObjectProperty(Type type, object instance, string propertyName, object? value)
    {
        var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (prop?.CanWrite != true)
        {
            return false;
        }

        var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        if (value == null)
        {
            if (targetType.IsValueType)
            {
                return false;
            }

            prop.SetValue(instance, null);
            return true;
        }

        if (!targetType.IsInstanceOfType(value))
        {
            return false;
        }

        prop.SetValue(instance, value);
        return true;
    }

    private static bool TrySetFloatProperty(Type type, object instance, string propertyName, float value)
    {
        var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (prop?.CanWrite != true)
        {
            return false;
        }

        if (prop.PropertyType == typeof(float))
        {
            prop.SetValue(instance, value);
            return true;
        }

        if (prop.PropertyType == typeof(double))
        {
            prop.SetValue(instance, (double)value);
            return true;
        }

        return false;
    }

    private static bool TrySetIntProperty(Type type, object instance, string propertyName, int value)
    {
        var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (prop?.CanWrite != true || prop.PropertyType != typeof(int))
        {
            return false;
        }

        prop.SetValue(instance, value);
        return true;
    }

    private static bool TrySetStringProperty(Type type, object instance, string propertyName, string value)
    {
        var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (prop?.CanWrite != true || prop.PropertyType != typeof(string))
        {
            return false;
        }

        prop.SetValue(instance, value);
        return true;
    }

    private static bool TrySetEnumProperty(Type type, object instance, string propertyName, string enumName)
    {
        var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (prop?.CanWrite != true || !prop.PropertyType.IsEnum)
        {
            return false;
        }

        try
        {
            var value = Enum.Parse(prop.PropertyType, enumName, ignoreCase: true);
            prop.SetValue(instance, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryInvokeFloatMethod(Type type, object instance, string methodName, float value)
    {
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(float) }, null);
        if (method != null)
        {
            method.Invoke(instance, new object[] { value });
            return true;
        }

        method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(double) }, null);
        if (method != null)
        {
            method.Invoke(instance, new object[] { (double)value });
            return true;
        }

        method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int) }, null);
        if (method != null)
        {
            method.Invoke(instance, new object[] { (int)MathF.Round(value) });
            return true;
        }

        return false;
    }

    private static bool TryInvokeBoolMethod(Type type, object instance, string methodName, bool value)
    {
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(bool) }, null);
        if (method == null)
        {
            return false;
        }

        method.Invoke(instance, new object[] { value });
        return true;
    }

    private static bool TryInvokeObjectMethod(Type type, object instance, string methodName, object? value)
    {
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public);
        foreach (var method in methods)
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) || method.ReturnType != typeof(void))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                continue;
            }

            var parameterType = Nullable.GetUnderlyingType(parameters[0].ParameterType) ?? parameters[0].ParameterType;
            if (value == null)
            {
                if (parameterType.IsValueType)
                {
                    continue;
                }
            }
            else if (!parameterType.IsInstanceOfType(value))
            {
                continue;
            }

            method.Invoke(instance, new[] { value });
            return true;
        }

        return false;
    }

    private static bool TryInvokeObjectBoolMethod(Type type, object instance, string methodName, object? first, bool second)
    {
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public);
        foreach (var method in methods)
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) || method.ReturnType != typeof(void))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 2 || parameters[1].ParameterType != typeof(bool))
            {
                continue;
            }

            var parameterType = Nullable.GetUnderlyingType(parameters[0].ParameterType) ?? parameters[0].ParameterType;
            if (first == null)
            {
                if (parameterType.IsValueType)
                {
                    continue;
                }
            }
            else if (!parameterType.IsInstanceOfType(first))
            {
                continue;
            }

            method.Invoke(instance, new[] { first, (object)second });
            return true;
        }

        return false;
    }

    private static bool TryInvokeBoolObjectMethod(Type type, object instance, string methodName, bool first, object? second)
    {
        var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public);
        foreach (var method in methods)
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) || method.ReturnType != typeof(void))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != typeof(bool))
            {
                continue;
            }

            var parameterType = Nullable.GetUnderlyingType(parameters[1].ParameterType) ?? parameters[1].ParameterType;
            if (second == null)
            {
                if (parameterType.IsValueType)
                {
                    continue;
                }
            }
            else if (!parameterType.IsInstanceOfType(second))
            {
                continue;
            }

            method.Invoke(instance, new[] { (object)first, second });
            return true;
        }

        return false;
    }

    private static string ResolveFlipEnumName(bool flipX, bool flipY)
    {
        if (flipX && flipY)
        {
            return "Both";
        }

        if (flipX)
        {
            return "Horizontal";
        }

        if (flipY)
        {
            return "Vertical";
        }

        return "None";
    }

    private static bool TryConvertColorValue(Type targetType, Color color, out object value)
    {
        if (targetType == typeof(Color))
        {
            value = color;
            return true;
        }

        var fromArgb = targetType.GetMethod("FromArgb", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(int), typeof(int), typeof(int), typeof(int) }, null);
        if (fromArgb != null)
        {
            value = fromArgb.Invoke(null, new object[] { color.A, color.R, color.G, color.B })!;
            return true;
        }

        var ctorInt = targetType.GetConstructor(new[] { typeof(int), typeof(int), typeof(int), typeof(int) });
        if (ctorInt != null)
        {
            value = ctorInt.Invoke(new object[] { color.A, color.R, color.G, color.B });
            return true;
        }

        var ctorByte = targetType.GetConstructor(new[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte) });
        if (ctorByte != null)
        {
            value = ctorByte.Invoke(new object[] { color.A, color.R, color.G, color.B });
            return true;
        }

        value = null!;
        return false;
    }

    private static bool IsPositiveFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= MinNativeSize;

    private static float ClampNativeDimension(float value) =>
        IsPositiveFinite(value) ? value : MinNativeSize;

    private void LogInvalidDrawDiag(string message)
    {
        if (_invalidDrawDiagLogCount >= InvalidDrawDiagLogLimit)
        {
            return;
        }

        _invalidDrawDiagLogCount++;
        System.GC.KeepAlive(0);
    }

    private void LogSizeGuardDiag(string message)
    {
        if (_sizeGuardDiagLogCount >= SizeGuardDiagLogLimit)
        {
            return;
        }

        _sizeGuardDiagLogCount++;
        System.GC.KeepAlive(0);
    }

    private bool TryEnsureImageTargetSize(Control control, string imagePath, string tag)
    {
        var renderSize = ResolveRenderSize(control, MinNativeSize, MinNativeSize);
        if (renderSize.Width < MinNativeSize || renderSize.Height < MinNativeSize)
        {
            LogTextureGuardDiag(
                "[FGUI][TEX-GUARD] skip image assign tag={Tag} image={Image} target={Width}x{Height} control={ControlType}",
                tag, imagePath, renderSize.Width, renderSize.Height, control.GetType().Name);
            return false;
        }

        var safeWidth = MathF.Max(MinNativeSize, renderSize.Width);
        var safeHeight = MathF.Max(MinNativeSize, renderSize.Height);
        control.Size(safeWidth, safeHeight);
        TrackControlSize(control, safeWidth, safeHeight);
        return true;
    }

    private void LogTextureGuardDiag(string template, params object[] args)
    {
        if (_textureGuardDiagLogCount >= TextureGuardDiagLogLimit)
        {
            return;
        }

        _textureGuardDiagLogCount++;
        System.GC.KeepAlive(0);
    }

    private static string ResolveControlImagePath(string imagePath)
    {
        if (Path.IsPathRooted(imagePath))
        {
            return imagePath;
        }

        var normalized = imagePath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return imagePath;
        }

        // Spark runtime convention: logical image paths are "image/*".
        if (normalized.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("ui/image/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized["ui/".Length..];
        }

        // Normalize accidental AppBundle-prefixed logical paths back to runtime logical form.
        if (normalized.StartsWith("AppBundle/ui/", StringComparison.OrdinalIgnoreCase))
        {
            var trimmed = normalized["AppBundle/".Length..];
            if (trimmed.StartsWith("ui/image/", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed["ui/".Length..];
            }

            return trimmed;
        }

        if (normalized.StartsWith("AppBundle/image/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized["AppBundle/".Length..];
        }

        return normalized;
    }

    public void AddChild(object parent, object child)
    {
        // 若 child 是被裁剪外壳包着的滚动面板：真正要挂进父级的是外壳(面板已固定在外壳内)。
        // 用外壳作为实际控件与跟踪键，位置/尺寸也取外壳自己的跟踪值。
        var hosted = _scrollClipHost.TryGetValue(child, out var host);
        Control? actual = hosted ? host : child as Control;
        object trackKey = hosted ? host! : child;

        if (parent is Control p && actual is Control c)
        {
            if (_attachedParentByChild.TryGetValue(trackKey, out var existingParent))
            {
                if (ReferenceEquals(existingParent, parent))
                {
                    LogHierarchyDiag("[FGUI][DIAG] adapter skip duplicate AddChild parent={ParentHash} child={ChildHash}",
                        parent.GetHashCode(), trackKey.GetHashCode());
                    return;
                }

                c.RemoveFromParent();
                LogHierarchyDiag("[FGUI][DIAG] adapter reparent child={ChildHash} from={FromHash} to={ToHash}",
                    trackKey.GetHashCode(), existingParent.GetHashCode(), parent.GetHashCode());
            }

            c.AlignTop().AlignLeft();
            p.Add(c);
            if (_controlPositions.TryGetValue(trackKey, out var trackedPos))
            {
                c.Position(trackedPos.X, trackedPos.Y);
            }

            if (_controlSizes.TryGetValue(trackKey, out var trackedSize))
            {
                c.Size(trackedSize.Width, trackedSize.Height);
            }

            _attachedParentByChild[trackKey] = parent;
        }
    }

    public void RemoveChild(object parent, object child)
    {
        // 若 child 是被裁剪外壳包着的滚动面板：从父级摘掉的是外壳。
        var hosted = _scrollClipHost.TryGetValue(child, out var host);
        Control? c = hosted ? host : child as Control;
        object trackKey = hosted ? host! : child;

        if (c != null)
        {
            if (_attachedParentByChild.TryGetValue(trackKey, out var existingParent) &&
                !ReferenceEquals(existingParent, parent))
            {
                LogHierarchyDiag("[FGUI][DIAG] adapter RemoveChild mismatch child={ChildHash} trackedParent={TrackedHash} requestParent={RequestHash}",
                    trackKey.GetHashCode(), existingParent.GetHashCode(), parent.GetHashCode());
            }

            c.RemoveFromParent();
            _attachedParentByChild.Remove(trackKey);
        }
    }

    public void AddToRoot(object control)
    {
        if (control is Control c)
        {
            // 方法1：直接设置为屏幕尺寸
            var size = GameUI.Device.ScreenViewport.Primary.Size;
            System.GC.KeepAlive(0);
            
            // 设置位置和尺寸
            c.Position(0, 0);
            c.Size(size.Width, size.Height);
            TrackControlSize(control, size.Width, size.Height);
            
            // 设置拉伸以适应屏幕变化
            c.Stretch();
            c.GrowRatio(1, 1);
            
            c.Show();
            c.AddToRoot();
            
            System.GC.KeepAlive(0);
        }
    }
    
    /// <summary>
    /// 添加到根节点，但保持原始尺寸不自动拉伸
    /// </summary>
    public void AddToRootWithFixedSize(object control, float width, float height)
    {
        if (control is Control c)
        {
            var safeWidth = ClampNativeDimension(width);
            var safeHeight = ClampNativeDimension(height);
            if (safeWidth != width || safeHeight != height)
            {
                LogSizeGuardDiag($"[FGUI][SIZE-GUARD] normalized root control={control.GetType().Name} requested={width}x{height} safe={safeWidth}x{safeHeight}");
            }

            c.Size(safeWidth, safeHeight);
            TrackControlSize(control, safeWidth, safeHeight);
            c.AlignTop().AlignLeft();
            c.Show();
            c.AddToRoot();
        }
    }

    public void RemoveFromParent(object control)
    {
        if (control is Control c)
        {
            c.RemoveFromParent();
            ClearHierarchyTracking(control, clearChildren: false);
        }
    }

    public void OnClick(object control, Action handler)
    {
        if (control is Control c)
        {
            c.OnPointerClicked += (sender, e) =>
            {
                if (IsPrimaryPointerButton(e))
                {
                    handler();
                }
            };
        }
    }

    public void OnPointerEnter(object control, Action handler)
    {
        if (control is Control c)
            c.MouseEnter(handler);
    }

    public void OnPointerLeave(object control, Action handler)
    {
        if (control is Control c)
            c.MouseLeave(handler);
    }

    public void OnPointerPress(object control, Action handler)
    {
        if (control is Control c)
        {
            c.OnPointerPressed += (sender, e) =>
            {
                if (!IsPrimaryPointerButton(e))
                {
                    LogPointerFilterDiag(control, e, "pressed");
                    return;
                }
                handler();
            };
        }
    }
    
    public void OnPointerPressWithPosition(object control, Action<float, float> handler)
    {
        if (control is Control c)
        {
            c.OnPointerPressed += (sender, e) =>
            {
                if (!IsPrimaryPointerButton(e))
                {
                    LogPointerFilterDiag(control, e, "pressed-pos");
                    return;
                }

                var pos = e.PointerPosition;
                if (pos.HasValue)
                {
                    handler(pos.Value.X, pos.Value.Y);
                }
                else
                {
                    handler(0, 0);
                }
            };
        }
    }

    public void OnPointerRelease(object control, Action handler)
    {
        if (control is Control c)
        {
            c.OnPointerReleased += (sender, e) =>
            {
                if (!IsPrimaryPointerButton(e))
                {
                    LogPointerFilterDiag(control, e, "released");
                    return;
                }
                handler();
            };
        }
    }

    public void OnMouseWheel(object control, Action<float> handler)
    {
        if (control is not Control c)
        {
            return;
        }

        if (TryBindWheelEvent(c, "OnMouseWheel", handler))
        {
            return;
        }

        if (TryBindWheelEvent(c, "OnMouseWheelChanged", handler))
        {
            return;
        }

        _ = TryBindWheelEvent(c, "OnPointerWheelChanged", handler);
    }

    private static bool TryBindWheelEvent(Control control, string eventName, Action<float> handler)
    {
        var eventInfo = control.GetType().GetEvent(eventName);
        if (eventInfo?.EventHandlerType == null)
        {
            return false;
        }

        var delegateInstance = CreateWheelDelegate(eventInfo.EventHandlerType, handler);
        if (delegateInstance == null)
        {
            return false;
        }

        eventInfo.AddEventHandler(control, delegateInstance);
        return true;
    }

    private static Delegate? CreateWheelDelegate(Type eventHandlerType, Action<float> handler)
    {
        var invoke = eventHandlerType.GetMethod("Invoke");
        if (invoke == null)
        {
            return null;
        }

        var parameters = invoke.GetParameters();
        if (parameters.Length == 2)
        {
            var senderParam = Expression.Parameter(parameters[0].ParameterType, "sender");
            var argsParam = Expression.Parameter(parameters[1].ParameterType, "args");
            var body = Expression.Call(
                typeof(SCEAdapter).GetMethod(nameof(DispatchWheelFromArgs), BindingFlags.NonPublic | BindingFlags.Static)!,
                Expression.Convert(argsParam, typeof(object)),
                Expression.Constant(handler));
            return Expression.Lambda(eventHandlerType, body, senderParam, argsParam).Compile();
        }

        if (parameters.Length == 1)
        {
            var argsParam = Expression.Parameter(parameters[0].ParameterType, "args");
            var body = Expression.Call(
                typeof(SCEAdapter).GetMethod(nameof(DispatchWheelFromArgs), BindingFlags.NonPublic | BindingFlags.Static)!,
                Expression.Convert(argsParam, typeof(object)),
                Expression.Constant(handler));
            return Expression.Lambda(eventHandlerType, body, argsParam).Compile();
        }

        return null;
    }

    private static void DispatchWheelFromArgs(object? args, Action<float> handler)
    {
        if (TryExtractWheelDelta(args, out var delta))
        {
            handler(delta);
        }
    }

    private static bool TryExtractWheelDelta(object? args, out float delta)
    {
        delta = 0f;
        if (args == null)
        {
            return false;
        }

        var type = args.GetType();
        var directProps = new[] { "WheelDelta", "Delta", "MouseWheelDelta", "ScrollDelta" };
        foreach (var propName in directProps)
        {
            var prop = type.GetProperty(propName);
            if (prop == null)
            {
                continue;
            }

            var raw = prop.GetValue(args);
            if (TryConvertToFloat(raw, out delta))
            {
                return true;
            }
        }

        // Some frameworks expose vector deltas through Delta with X/Y fields.
        var deltaProp = type.GetProperty("Delta");
        var deltaValue = deltaProp?.GetValue(args);
        if (deltaValue != null)
        {
            var deltaType = deltaValue.GetType();
            var yProp = deltaType.GetProperty("Y");
            if (yProp != null && TryConvertToFloat(yProp.GetValue(deltaValue), out delta))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPrimaryPointerButton(object? eventArgs)
    {
        if (eventArgs == null)
        {
            return true;
        }

        if (IsTouchLikePointer(eventArgs))
        {
            return true;
        }

        var eventType = eventArgs.GetType();
        var prop = eventType.GetProperty("Button")
                   ?? eventType.GetProperty("Buttons")
                   ?? eventType.GetProperty("PointerButton");
        if (prop == null)
        {
            return true;
        }

        var value = prop.GetValue(eventArgs);
        if (value == null)
        {
            return true;
        }

        if (value is PointerButtons buttons)
        {
            // PointerButtons is a flags enum; pressed/released phases may carry combined values.
            // Treat any payload containing Button1 as primary instead of strict equality.
            return buttons == PointerButtons.None || buttons.HasFlag(PointerButtons.Button1);
        }

        if (value is int intValue)
        {
            return intValue == 0 || intValue == 1 || (intValue & 1) == 1;
        }

        if (value is uint uintValue)
        {
            return uintValue == 0 || uintValue == 1 || (uintValue & 1u) == 1u;
        }

        if (value is long longValue)
        {
            return longValue == 0 || longValue == 1 || (longValue & 1L) == 1L;
        }

        if (value is ulong ulongValue)
        {
            return ulongValue == 0UL || ulongValue == 1UL || (ulongValue & 1UL) == 1UL;
        }

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var isPrimary =
               text.IndexOf("Button1", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("Button0", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("LeftButton", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("Primary", StringComparison.OrdinalIgnoreCase) >= 0
               || text.Equals("1", StringComparison.OrdinalIgnoreCase)
               || text.Equals("0", StringComparison.OrdinalIgnoreCase)
               || text.Equals("None", StringComparison.OrdinalIgnoreCase);

        return isPrimary;
    }

    private void LogPointerFilterDiag(object control, object? eventArgs, string phase)
    {
        if (_pointerFilterDiagLogCount >= PointerFilterDiagLogLimit || eventArgs == null)
        {
            return;
        }

        _pointerFilterDiagLogCount++;
        var eventType = eventArgs.GetType();
        var prop = eventType.GetProperty("Button")
                   ?? eventType.GetProperty("Buttons")
                   ?? eventType.GetProperty("PointerButton");
        var value = prop?.GetValue(eventArgs);
        var raw = value?.ToString() ?? "<null>";
        System.GC.KeepAlive(0);
    }

    private static bool IsTouchLikePointer(object eventArgs)
    {
        var eventType = eventArgs.GetType();

        var isTouchProp = eventType.GetProperty("IsTouch");
        if (isTouchProp?.GetValue(eventArgs) is bool isTouch && isTouch)
        {
            return true;
        }

        var pointerTypeProp = eventType.GetProperty("PointerType")
                              ?? eventType.GetProperty("PointerDeviceType")
                              ?? eventType.GetProperty("DeviceType");
        var pointerTypeValue = pointerTypeProp?.GetValue(eventArgs)?.ToString();
        if (string.IsNullOrWhiteSpace(pointerTypeValue))
        {
            return false;
        }

        return pointerTypeValue.IndexOf("Touch", StringComparison.OrdinalIgnoreCase) >= 0
               || pointerTypeValue.IndexOf("Pen", StringComparison.OrdinalIgnoreCase) >= 0
               || pointerTypeValue.IndexOf("Stylus", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ===== Touch Behavior (Gestures) =====
    
    public void EnableTouchBehavior(object control, TouchBehaviorConfig config)
    {
        if (control is Control c)
        {
            // Remove existing if any
            DisableTouchBehavior(control);
            
            // Add new TouchBehavior with configuration
            var tb = c.AddTouchBehaviorWithDuration(
                scaleFactor: config.ScaleFactor,
                pressAnimationDurationMs: config.AnimationDurationMs,
                longPressDurationMs: config.LongPressDurationMs
            );
            
            _touchBehaviors[control] = tb;
            
            // Wire up long press event if handler exists
            if (_longPressHandlers.TryGetValue(control, out var handler))
            {
                tb.LongPressTriggered += (s, e) => handler();
            }
        }
    }
    
    public void DisableTouchBehavior(object control)
    {
        if (_touchBehaviors.TryGetValue(control, out var tb))
        {
            // TouchBehavior is managed by the control's behavior collection
            // Just remove from our tracking dictionary
            _touchBehaviors.Remove(control);
        }
    }
    
    public void OnLongPress(object control, Action handler)
    {
        _longPressHandlers[control] = handler;
        
        // If TouchBehavior already exists, wire up the event
        if (_touchBehaviors.TryGetValue(control, out var tb))
        {
            tb.LongPressTriggered += (s, e) => handler();
        }
    }
    
    public void OnDoubleClick(object control, Action handler)
    {
        // SCE doesn't have built-in double click, implement with timing
        if (control is Control c)
        {
            DateTime lastClick = DateTime.MinValue;
            c.OnPointerClicked += (sender, e) =>
            {
                var now = DateTime.Now;
                if ((now - lastClick).TotalMilliseconds < 300)
                {
                    handler();
                    lastClick = DateTime.MinValue; // Reset to prevent triple-click
                }
                else
                {
                    lastClick = now;
                }
            };
        }
    }
    
    // ===== Pointer Capture (for Drag/Swipe) =====
    
    public void CapturePointer(object control)
    {
        if (control is Control c)
        {
            var captureButtons = PrimaryCaptureButtons == PointerButtons.None
                ? PointerButtons.Button1
                : PrimaryCaptureButtons;
            c.CapturePointer(captureButtons);
            _capturedPointers[control] = captureButtons;
        }
    }
    
    public void ReleasePointer(object control)
    {
        if (control is Control c && _capturedPointers.TryGetValue(control, out var buttons))
        {
            c.ReleasePointer(buttons);
            _capturedPointers.Remove(control);
        }
    }
    
    public void OnPointerCapturedMove(object control, Action<float, float> handler)
    {
        if (control is Control c)
        {
            _pointerMoveHandlers[control] = handler;
            c.OnPointerCapturedMove += (sender, e) =>
            {
                var pos = e.PointerPosition;
                if (pos.HasValue)
                {
                    handler(pos.Value.X, pos.Value.Y);
                }
            };
        }
    }
    
    // ===== Virtualizing Panel =====
    
    public void SetVirtualizingPanelConfig(object panel, VirtualPanelConfig config)
    {
        if (panel is VirtualizingPanel vp)
        {
            vp.ItemSize = new SizeF(config.ItemWidth, config.ItemHeight);
            vp.ScrollOrientation = config.IsHorizontal ? GameUI.Enum.Orientation.Horizontal : GameUI.Enum.Orientation.Vertical;
            // CachePages handled via CacheLength
            if (config.CachePages > 0)
            {
                vp.CacheLength = new GameUI.Control.Struct.VirtualizationCacheLength(config.CachePages, config.CachePages);
            }
        }
    }
    
    public void SetVirtualizingPanelItems(object panel, int itemCount, Action<int, object> itemRenderer)
    {
        if (panel is VirtualizingPanel vp)
        {
            if (!_virtualizingPanelStates.TryGetValue(vp, out var state))
            {
                state = new VirtualizingPanelRenderState();
                _virtualizingPanelStates[vp] = state;
            }

            state.ItemCount = itemCount;
            state.ItemRenderer = itemRenderer;
            if (!state.Hooked)
            {
                state.Hooked = true;
                // Set up one callback only; renderer/itemCount are read from mutable state.
                vp.OnChildVirtualizationPhase += (sender, e) =>
                {
                    if (!_virtualizingPanelStates.TryGetValue(vp, out var current) || current.ItemRenderer == null)
                    {
                        return;
                    }

                    var control = e.Control;
                    var index = control.ItemIndex;
                    if (index >= 0 && index < current.ItemCount)
                    {
                        current.ItemRenderer(index, control);
                    }
                };
            }

            // Create items list to trigger virtualization
            var items = new List<int>();
            for (int i = 0; i < itemCount; i++)
                items.Add(i);
            vp.ItemsSource = items.Cast<object>();
        }
    }
    
    public void RefreshVirtualizingPanel(object panel)
    {
        if (panel is VirtualizingPanel vp)
        {
            vp.GenerateChildren();
        }
    }

    // ===== Utilities =====
    
    public void Dispose(object control)
    {
        // Clean up TouchBehavior if exists
        DisableTouchBehavior(control);
        
        // Clean up handlers
        _longPressHandlers.Remove(control);
        _pointerMoveHandlers.Remove(control);
        _capturedPointers.Remove(control);
        _controlSizes.Remove(control);
        _baseControlSizes.Remove(control);
        _controlPositions.Remove(control);
        _overscrollLastApplied.Remove(control);
        // 一并回收裁剪外壳：把外壳从父级摘掉并释放，避免残留空容器。
        if (_scrollClipHost.Remove(control, out var clipHost))
        {
            _controlPositions.Remove(clipHost);
            _controlSizes.Remove(clipHost);
            _attachedParentByChild.Remove(clipHost);
            clipHost.RemoveFromParent();
        }
        _fallbackScaleBaseRects.Remove(control);
        _fallbackScaleActiveRoots.Remove(control);
        if (control is Canvas canvas)
        {
            _canvasImageStates.Remove(canvas);
        }
        if (control is VirtualizingPanel vp)
        {
            _virtualizingPanelStates.Remove(vp);
        }
        ClearHierarchyTracking(control, clearChildren: true);
        
        if (control is Control c)
            c.RemoveFromParent();
    }

    private static PointerButtons BuildPrimaryCaptureButtons()
    {
        var mask = PointerButtons.None;
        foreach (var value in Enum.GetValues<PointerButtons>())
        {
            var name = value.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (name.IndexOf("Primary", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Button0", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Button1", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mask |= value;
            }
        }

        return mask;
    }

    public byte[]? LoadTexture(string path) => null;
    
    public SizeF GetScreenSize()
    {
        // 获取实际屏幕尺寸（设备无关像素）
        var size = GameUI.Device.ScreenViewport.Primary.Size;
        return new SizeF(size.Width, size.Height);
    }

    private void ClearHierarchyTracking(object control, bool clearChildren)
    {
        _attachedParentByChild.Remove(control);
        _controlSizes.Remove(control);
        _baseControlSizes.Remove(control);
        _controlPositions.Remove(control);
        if (_scrollClipHost.Remove(control, out var clipHost))
        {
            _controlPositions.Remove(clipHost);
            _controlSizes.Remove(clipHost);
            _attachedParentByChild.Remove(clipHost);
            clipHost.RemoveFromParent();
        }
        _fallbackScaleBaseRects.Remove(control);
        _fallbackScaleActiveRoots.Remove(control);
        if (!clearChildren || _attachedParentByChild.Count == 0)
        {
            return;
        }

        List<object>? danglingChildren = null;
        foreach (var pair in _attachedParentByChild)
        {
            if (!ReferenceEquals(pair.Value, control))
            {
                continue;
            }

            danglingChildren ??= [];
            danglingChildren.Add(pair.Key);
        }

        if (danglingChildren == null)
        {
            return;
        }

        foreach (var child in danglingChildren)
        {
            _attachedParentByChild.Remove(child);
            _controlSizes.Remove(child);
            _baseControlSizes.Remove(child);
            _controlPositions.Remove(child);
            _fallbackScaleBaseRects.Remove(child);
            _fallbackScaleActiveRoots.Remove(child);
        }
    }

    private void LogHierarchyDiag(string template, params object[] args)
    {
        if (_hierarchyDiagLogCount >= HierarchyDiagLogLimit)
        {
            return;
        }

        _hierarchyDiagLogCount++;
        System.GC.KeepAlive(0);
    }
}
#endif

