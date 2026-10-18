#if CLIENT
using System.Drawing;
using System.Diagnostics;
using System.IO;
using FairyGUI;
using GameUI.Control.Primitive;

namespace FairyGUI.Render;

public class SCERenderContext
{
    private static SCERenderContext? _instance;
    private ISCEAdapter? _adapter;
    /// <summary>滚动条显示时的默认拖杆粗细（编辑器未设 Hidden 时使用）。</summary>
    private const float DefaultScrollBarSize = 8f;
    /// <summary>判定拖拽方向所需的最小位移（原生像素）。</summary>
    private const float ScrollDirectionThreshold = 6f;
    /// <summary>每个可滚动 native 当前的拖拽状态（方向分流用）。</summary>
    private readonly Dictionary<object, ScrollDragState> _scrollDragStates = new();
    private readonly Dictionary<object, object> _nativeParentOfChild = new();
    private readonly HashSet<object> _boundButtonNative = [];
    private readonly HashSet<object> _boundGenericClickNative = [];

    /// <summary>
    /// 本次指针交互里已经派发过 onClick 的对象。<b>一次交互，一个对象只派发一次。</b>
    ///
    /// 同一次物理点击会从多条路一起打到同一个对象上：
    /// 一是 GComponent 里往往铺着多个重叠子控件(底图、图标、文字)，它们自己没有 touch 监听，
    /// <b>每一个都会把点击转发给同一个祖先</b>(见 <c>ResolveTouchRelayTarget</c>)；
    /// 二是祖先若自带命中面(GButton)，它自己那份 click 也会触发。
    /// 不挡的话业务回调要跑好几遍 —— <c>FGUIButton.HandleClick</c> 里那个 60ms 去重窗口
    /// 挡的就是这个，但它只保护按钮自身的 Selected 翻转，业务回调照样重复。
    ///
    /// <b>不能改成"祖先自己绑了 click 就不转发"</b>：普通 GComponent 是透明容器，
    /// SCE 里收不到点击（只有 <c>ApplyButtonProperties</c> 会给 GButton 铺一层命中面），
    /// 它那份 click 绑了也永不触发，挡掉转发就等于整个点不动了。
    ///
    /// 交互边界取 press：一次新的按下必然意味着新的交互，而 press 一定早于 click，
    /// 所以连点、双击都不会被误挡。
    /// </summary>
    private readonly HashSet<GObject> _clickDispatchedInGesture = [];

    /// <summary>
    /// 本次指针交互已经被判定为"滑动"，松手时不该再算点击。
    ///
    /// SCE 对点击的定义是<b>在同一个控件上按下再释放</b>，中间移动多远都不管 ——
    /// 所以在列表上滑动一下选内容，松手时指针仍停在某一项上，那一项照样收到 click，
    /// 表现为"滑一下就误选"。FGUI 原版靠按下点与松手点的距离区分两者，这里补上。
    ///
    /// 判定直接复用滚动桥自己的 <c>drag.Decided</c>(见 <c>ScrollDirectionThreshold</c>)，
    /// 不另立阈值：那本来就是"这次是滑动不是点击"的分界，两处各判一次迟早会不一致。
    ///
    /// 保持到<b>下一次按下</b>才清零：click 是松手之后才派发的，松手即清会来不及。
    /// </summary>
    private bool _gestureDragged;
    private readonly HashSet<object> _boundTouchNative = [];
    private readonly HashSet<object> _boundScrollableNative = [];
    private readonly HashSet<object> _boundScrollablePointerNative = [];
    /// <summary>挂过"列表里子控件的拖动桥"的原生控件（见 BindScrollChildDragBridge）。</summary>
    private readonly HashSet<object> _boundScrollChildPointerNative = [];
    private readonly HashSet<object> _boundScrollableWheelNative = [];
    private readonly HashSet<object> _boundScrollableItemPointerNative = [];
    private readonly HashSet<object> _scrollableItemCapturedNative = [];
    private readonly HashSet<object> _touchCapturedNative = [];
    private readonly Dictionary<object, bool> _scrollableHorizontalByNative = new();
    private readonly HashSet<object> _bothAxisFallbackLogged = [];
    private readonly HashSet<object> _syncingScrollableToNative = [];
    private readonly HashSet<object> _syncingScrollableFromNative = [];
    private readonly HashSet<GMovieClip> _activeMovieClips = [];
    private readonly Dictionary<GMovieClip, string> _movieClipFramePathCache = new();
    private readonly HashSet<string> _movieClipMissingFrameLogged = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _scatterNoSpriteLogged = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RenderPerfStat> _renderPerfStats = new(StringComparer.Ordinal);
    private readonly HashSet<string> _perfProbeLogged = new(StringComparer.Ordinal);
    private const bool EnablePerfDiag = false;
    private const bool EnablePerfProbeLogs = false;
    private const bool EnableComboInputDiagLogs = false;
    private const int PerfDiagMaxLogs = 120;
    private int _remainingPerfLogs = PerfDiagMaxLogs;

    public static SCERenderContext Instance => _instance ??= new SCERenderContext();
    public ISCEAdapter? Adapter { get => _adapter; set => _adapter = value; }

    public void Initialize(ISCEAdapter adapter)
    {
        _adapter = adapter;
    }

    public object? CreateNativeControl(GObject obj)
    {
        if (obj.NativeObject != null)
        {
            return obj.NativeObject;
        }

        if (_adapter == null) return null;
        object? native = obj switch
        {
            // Use Panel for GButton to support child controls (title, icon)
            // SCE Button doesn't support child controls properly
            GButton => _adapter.CreatePanel(),
            // Unity/FairyGUI: GGroup is a logical grouping node, not a render/hit-test node.
            GGroup => null,
            GTextInput => _adapter.CreateInput(),
            GTextField => _adapter.CreateLabel(),
            GList => _adapter.CreateScrollablePanel(),
            GGraph graph => CreateGraphControl(graph),
            GImage image => CreateImageControl(image),
            FGUIRoot => _adapter.CreatePanel(),
            GComponent => _adapter.CreatePanel(),
            _ => _adapter.CreatePanel()
        };
        if (native != null)
        {
            obj.NativeObject = native;
            if (obj is GMovieClip movieClip)
            {
                _activeMovieClips.Add(movieClip);
            }
            ApplyProperties(obj);
        }
        return native;
    }
    
    private object? CreateGraphControl(GGraph graph)
    {
        if (_adapter == null) return null;
        if (graph.Type == GraphType.Ellipse)
        {
            var canvas = _adapter.CreateCanvas();
            _adapter.SetCanvasEllipse(canvas, graph.FillColor);
            return canvas;
        }

        // 带描边、或四角半径不一样的矩形，原生控件画不出来，改用 Canvas 自绘。
        // 其余（纯色、统一圆角——弹窗遮罩基本都是这种）仍用 Panel 的背景色 + 圆角，不白占一张画布。
        // 样式由 ApplyGraphProperties 统一设置。
        if (graph.Type == GraphType.Rect && NeedsCanvasRect(graph))
        {
            return _adapter.CreateCanvas();
        }

        return _adapter.CreatePanel();
    }

    private static bool NeedsCanvasRect(GGraph graph)
    {
        // FGUI 矩形默认就带 1 像素黑色描边（xml 里省略即默认值），黑色遮罩的描边与填充同色、画了也看不出，
        // 不为它换 Canvas。
        if (graph.LineSize > 0 && graph.LineColor.A > 0 && graph.LineColor.ToArgb() != graph.FillColor.ToArgb())
        {
            return true;
        }

        var corner = graph.CornerRadius;
        return corner[1] != corner[0] || corner[2] != corner[0] || corner[3] != corner[0];
    }
    
    private object? CreateImageControl(GImage image)
    {
        if (_adapter == null) return null;
        if (image is GMovieClip)
        {
            // MovieClip uses Canvas frame drawing to avoid Control.Image path swap flicker.
            return _adapter.CreateCanvas();
        }

        if (image.FillMethod != FillMethod.None)
        {
            // Fill image should use a fill-capable native control to match ability cooldown visuals.
            return _adapter.CreateFillImageControl();
        }

        if (UIRuntime.ControlOnlyImageMode)
        {
            return _adapter.CreatePanel();
        }

        // 引擎原生图集登记过的图：普通控件 + Image 填逻辑名，不占 Canvas 帧缓冲（见 FGUINativeAtlas）。
        if (FGUINativeAtlas.CanUse(image))
        {
            return _adapter.CreatePanel();
        }

        // 独立图集中的九宫格图片无需 Canvas 裁切：直接交给星火 Control.Image +
        // SlicedEdges 渲染，避免 9 次 Canvas.DrawImage 在缩放时产生接缝。
        if (CanUseNativeSlicedImage(image))
        {
            return _adapter.CreatePanel();
        }

        // Check if image has sprite (atlas region) - use Canvas for proper region rendering
        if (image.PackageItem?.Sprite != null)
        {
            // Priority: image.Width > image.InitWidth > PackageItem.Width > Sprite.Rect
            float width = image.Width > 0 ? image.Width : 
                          (image.InitWidth > 0 ? image.InitWidth : 
                          (image.PackageItem.Width > 0 ? image.PackageItem.Width : 
                          image.PackageItem.Sprite.Rect.Width));
            float height = image.Height > 0 ? image.Height : 
                           (image.InitHeight > 0 ? image.InitHeight : 
                           (image.PackageItem.Height > 0 ? image.PackageItem.Height : 
                           image.PackageItem.Sprite.Rect.Height));
            
            // 应用缩放因子到图片尺寸
            float scaleFactor = UIRuntime.ContentScaleFactor;
            width *= scaleFactor;
            height *= scaleFactor;
            
            // Create Canvas for atlas region rendering (both for normal sprites and nine-slice)
            return _adapter.CreateCanvas(width, height);
        }
        // Otherwise use Panel with background color
        return _adapter.CreatePanel();
    }

    /// <summary>
    /// 星火原生九宫格只能处理完整图片，不能从共享图集裁切区域。FairyGUI 的
    /// <c>atlas="alone_npot"</c> 导出会生成独立图集，其 sprite 覆盖 atlas 全部范围；
    /// 这类图片可安全使用 Control.SlicedEdges。
    /// </summary>
    private static bool CanUseNativeSlicedImage(GImage image)
    {
        var item = image.PackageItem;
        var sprite = item?.Sprite;
        var atlas = sprite?.Atlas;
        if (item?.Scale9Grid.HasValue != true || sprite == null || atlas == null || sprite.Rotated)
        {
            return false;
        }

        const float Epsilon = 0.01f;
        var rect = sprite.Rect;
        return MathF.Abs(rect.X) < Epsilon
            && MathF.Abs(rect.Y) < Epsilon
            && MathF.Abs(rect.Width - atlas.Width) < Epsilon
            && MathF.Abs(rect.Height - atlas.Height) < Epsilon;
    }

    public void ApplyProperties(GObject obj)
    {
        if (_adapter == null || obj.NativeObject == null) return;
        var native = obj.NativeObject;
        
        // 获取内容缩放因子
        float scaleFactor = UIRuntime.ContentScaleFactor;
        
        if (obj is not FGUIRoot)
        {
            var renderPos = ResolveRenderPosition(obj);
            _adapter.SetPosition(native, renderPos.X * scaleFactor, renderPos.Y * scaleFactor);
            _adapter.SetSize(native, obj.Width * scaleFactor, obj.Height * scaleFactor);
        }
        _adapter.SetVisible(native, obj.Visible);
        _adapter.SetOpacity(native, obj.Alpha);
        _adapter.SetRotation(native, obj.Rotation);
        _adapter.SetScale(native, obj.ScaleX, obj.ScaleY);
        _adapter.SetTouchable(native, obj.Touchable);
        _adapter.SetGrayed(native, obj.Grayed);
        if (IsDemoGraphProbe(obj))
        {
            LogDemoGraphProbe("ApplyProperties", obj);
        }
        BindTouchEvents(obj, native);
        ApplyPointerBlocking(obj, native);
        ApplyTypeSpecificProperties(obj);
    }

    /// <summary>
    /// 决定控件是否拦截指针事件。Control 框架默认穿透到父级，会导致点击漏到场景。
    /// 策略「仅控件挡住」：只拦截交互控件——按钮、以及带 ScrollPane 的组件（列表）。
    /// 列表拦截后既能独占指针以支持拖拽滚动，其区域内的点击也不再漏到场景；
    /// 透明容器/根节点保持穿透，空白处仍可点到场景。
    /// </summary>
    private void ApplyPointerBlocking(GObject obj, object native)
    {
        if (_adapter == null)
        {
            return;
        }

        // 输入框也必须拦截指针:否则点击穿透到父级,原生 Input 收不到按下事件、无法聚焦输入。
        // 有形状的图形也挡（见 IsHitGraph）；自定义数据里标了拦截的图片也挡（见 IsBlockTouchImage）。
        var block = obj is GButton
            || obj is GTextInput
            || (obj is GComponent component && component.ScrollPane != null)
            || IsHitGraph(obj)
            || IsBlockTouchImage(obj);
        _adapter.SetBlockPointerEvents(native, block);

        // 拦截了指针的子控件（按钮等）按下事件到不了外面的列表，要自己接住转给列表拖动。
        // 列表本身与输入框不挂：前者有自己的桥，后者按住要能选字。
        if (block && obj is not GTextInput && !(obj is GComponent { ScrollPane: not null }))
        {
            BindScrollChildDragBridge(obj, native);
        }
    }

    public void EnsureTouchBinding(GObject obj)
    {
        if (_adapter == null || obj.NativeObject == null)
        {
            return;
        }

        BindTouchEvents(obj, obj.NativeObject);
    }

    /// <summary>
    /// 自定义数据读进来之后，按它重新判一次"挂不挂指针回调、拦不拦点击"。
    /// 图片的原生控件可能在读自定义数据之前就建好了（构建资源时），那时判定看不到 <see cref="BlockTouchMarker"/>。
    /// </summary>
    internal void RefreshPointerBlocking(GObject obj)
    {
        if (_adapter == null || obj.NativeObject == null)
        {
            return;
        }

        BindTouchEvents(obj, obj.NativeObject);
        ApplyPointerBlocking(obj, obj.NativeObject);
    }

    private void ApplyTypeSpecificProperties(GObject obj)
    {
        if (_adapter == null || obj.NativeObject == null) return;
        var native = obj.NativeObject;
        switch (obj)
        {
            case GImage image: ApplyImageProperties(image, native); break;
            case GLoader loader: ApplyLoaderProperties(loader, native); break;
            case GTextField text: ApplyTextProperties(text, native); break;
            case GButton button: ApplyButtonProperties(button, native); break;
            case GGraph graph: ApplyGraphProperties(graph, native); break;
            case GComponent component: ApplyComponentProperties(component, native); break;
        }
    }

    private static void ApplyLoaderProperties(GLoader loader, object native)
    {
        loader.SyncNativeContent();
    }

    /// <summary>
    /// 把图形的形状与颜色同步到原生控件。创建时与 <see cref="GGraph"/> 改颜色 / 重画时都走这里，只有一份。
    /// 目前支持：空白、矩形（填充、描边、圆角）、椭圆（只有填充）；多边形、正多边形尚未实现，暂按矩形填充画。
    /// </summary>
    internal void ApplyGraphProperties(GGraph graph, object native)
    {
        if (_adapter == null)
        {
            return;
        }

        switch (graph.Type)
        {
            case GraphType.Ellipse:
                _adapter.SetCanvasEllipse(native, graph.FillColor);
                return;

            case GraphType.Empty:
                _adapter.ClearCanvasRenderState(native);
                _adapter.SetBackgroundColor(native, Color.Transparent);
                _adapter.SetCornerRadius(native, 0f);
                return;

            case GraphType.Rect:
            {
                // 描边宽度与圆角是设计像素，和尺寸一样要乘内容缩放（见 ApplyProperties）。
                float scaleFactor = UIRuntime.ContentScaleFactor;
                var corner = graph.CornerRadius;
                var scaledCorner = new float[corner.Count];
                for (var i = 0; i < corner.Count; i++)
                {
                    scaledCorner[i] = corner[i] * scaleFactor;
                }

                _adapter.SetGraphRect(native, graph.FillColor, graph.LineSize * scaleFactor, graph.LineColor, scaledCorner);
                return;
            }

            default:
                _adapter.ClearCanvasRenderState(native);
                _adapter.SetBackgroundColor(native, graph.FillColor);
                _adapter.SetCornerRadius(native, 0f);
                return;
        }
    }

    private void ApplyImageProperties(GImage image, object native)
    {
        if (_adapter == null) return;

        if (image is GMovieClip movieClip)
        {
            ApplyMovieClipProperties(movieClip, native);
            image.ApplyNativeVisualState();
            return;
        }

        var packageItem = image.PackageItem;
        if (packageItem == null)
        {
            image.ApplyNativeVisualState();
            return;
        }

        // 引擎原生图集：Image 填逻辑名，九宫格再设边距（见 FGUINativeAtlas）。
        // 原生控件已经按 Canvas 建好的（开关打开之前建的）照旧走下面的 Canvas 画法。
        if (native is not Canvas && FGUINativeAtlas.CanUse(image))
        {
            var logicalPath = FGUINativeAtlas.LogicalPath(packageItem);
            if (packageItem.Scale9Grid.HasValue && !FGUINativeAtlas.SliceByConfigBorder)
            {
                var nativeGrid = packageItem.Scale9Grid.Value;
                _adapter.SetSlicedImage(native, logicalPath,
                    (int)nativeGrid.X,
                    (int)(packageItem.Width - nativeGrid.X - nativeGrid.Width),
                    (int)nativeGrid.Y,
                    (int)(packageItem.Height - nativeGrid.Y - nativeGrid.Height));
            }
            else
            {
                _adapter.SetBackgroundImage(native, logicalPath);
            }

            image.ApplyNativeVisualState();
            return;
        }

        // Atlas 模式：直接从图集区域绘制（不依赖散图文件/清单）
        if (!UIRuntime.ControlOnlyImageMode && packageItem.Sprite != null
            && TryGetAtlasRegion(packageItem.Sprite, out var atlasImgPath, out var atlasRegion, out var atlasRotated))
        {
            if (packageItem.Scale9Grid.HasValue)
            {
                var grid9 = packageItem.Scale9Grid.Value;
                int l = (int)grid9.X;
                int t = (int)grid9.Y;
                int r = (int)(packageItem.Width - grid9.X - grid9.Width);
                int b = (int)(packageItem.Height - grid9.Y - grid9.Height);
                ((SCEAdapter)_adapter).SetSlicedImageFromAtlas(native, atlasImgPath, atlasRegion, l, r, t, b,
                    image.Width > 0 ? image.Width : packageItem.Width,
                    image.Height > 0 ? image.Height : packageItem.Height);
            }
            else
            {
                _adapter.SetImageRegion(native, atlasImgPath, atlasRegion, atlasRotated);

                // 发布时裁掉了透明边（trimImage）的图：图集里只存实际内容，要按偏移放回原始尺寸里的位置，
                // 否则会被拉伸铺满整个控件（图标变大、变形）—— 这是以前只能用单图的原因。
                var sprite = packageItem.Sprite;
                ((SCEAdapter)_adapter).SetImageRegionTrim(native, sprite.Offset, sprite.OriginalSize);
            }
            image.ApplyNativeVisualState();
            return;
        }

        if (!TryResolveScatterImagePath(packageItem, out var imagePath) ||
            string.IsNullOrWhiteSpace(imagePath))
        {
            System.GC.KeepAlive(0);
            image.ApplyNativeVisualState();
            return;
        }

        if (packageItem.Sprite == null)
        {
            var packageId = packageItem.Owner?.Id ?? string.Empty;
            var mappingKey = $"{packageId}:{packageItem.Id}";
            if (_scatterNoSpriteLogged.Add(mappingKey))
            {
                System.GC.KeepAlive(0);
            }
        }

        if (packageItem.Scale9Grid.HasValue)
        {
            var grid = packageItem.Scale9Grid.Value;
            var left = (int)grid.X;
            var top = (int)grid.Y;
            var right = (int)(packageItem.Width - grid.X - grid.Width);
            var bottom = (int)(packageItem.Height - grid.Y - grid.Height);
            _adapter.SetSlicedImage(native, imagePath, left, right, top, bottom);
            image.ApplyNativeVisualState();
            return;
        }

        _adapter.SetBackgroundImage(native, imagePath);
        image.ApplyNativeVisualState();
    }

    private void ApplyMovieClipProperties(GMovieClip movieClip, object native)
    {
        if (_adapter == null)
        {
            return;
        }

        var packageItem = movieClip.PackageItem;
        if (packageItem == null)
        {
            return;
        }

        packageItem.Owner?.GetItemAsset(packageItem);

        // Atlas 模式：当前帧从图集区域绘制（帧的 SpriteId -> 图集 sprite -> region）
        if (!UIRuntime.ControlOnlyImageMode)
        {
            var frames = packageItem.MovieClipFrames;
            var idx = movieClip.Frame;
            if (frames != null && idx >= 0 && idx < frames.Count)
            {
                var spriteId = frames[idx].SpriteId;
                var sprite = string.IsNullOrWhiteSpace(spriteId) ? null : packageItem.Owner?.GetSprite(spriteId!);
                if (TryGetAtlasRegion(sprite, out var clipAtlas, out var clipRegion, out var clipRotated))
                {
                    _adapter.SetImageRegion(native, clipAtlas, clipRegion, clipRotated);
                }
            }
            return;
        }

        if (!TryResolveMovieClipFrameImagePath(movieClip, out var imagePath) || string.IsNullOrWhiteSpace(imagePath))
        {
            var packageId = packageItem.Owner?.Id ?? "<null>";
            var clipItemId = packageItem.Id ?? "<null>";
            var frameIndex = movieClip.Frame;
            var missingKey = $"{packageId}::{clipItemId}::{frameIndex}";
            if (_movieClipMissingFrameLogged.Add(missingKey))
            {
                System.GC.KeepAlive(0);
            }

            // Keep the last valid frame to avoid hard-stop when one frame mapping is missing.
            if (_movieClipFramePathCache.TryGetValue(movieClip, out var cachedFallback) &&
                !string.IsNullOrWhiteSpace(cachedFallback))
            {
                _adapter.SetCanvasImage(native, cachedFallback);
            }
            return;
        }

        if (_movieClipFramePathCache.TryGetValue(movieClip, out var cachedPath) &&
            cachedPath.Equals(imagePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _movieClipFramePathCache[movieClip] = imagePath;
        _adapter.SetCanvasImage(native, imagePath);
    }

    private static bool TryResolveMovieClipFrameImagePath(GMovieClip movieClip, out string imagePath)
    {
        imagePath = string.Empty;
        var movieClipItem = movieClip.PackageItem;
        var owner = movieClipItem?.Owner;
        if (movieClipItem == null || owner == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(owner.Id) || string.IsNullOrWhiteSpace(movieClipItem.Id))
        {
            return false;
        }

        // Atlas 模式下不走散图 manifest（参考框架 MovieClip 仅实现了散图路径）。
        // 优雅跳过，避免 manifest 缺失刷错误日志；MovieClip 帧暂不渲染（保留上一帧）。
        if (!UIRuntime.ControlOnlyImageMode)
        {
            return false;
        }

        return FGUIScatterManifest.TryResolveMovieClipFrame(owner.Id, movieClipItem.Id, movieClip.Frame, out imagePath);
    }

    private static bool TryResolveScatterImagePath(PackageItem item, out string imagePath)
    {
        imagePath = string.Empty;
        var packageId = item.Owner?.Id;
        var itemId = item.Id;
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        if (FGUIScatterManifest.TryResolve(packageId, itemId, out imagePath))
        {
            return true;
        }

        if (TryBuildScatterFallbackPath(item, out imagePath))
        {
            System.GC.KeepAlive(0);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 从图集 sprite 解析出引擎图片路径 + 区域。图集实际部署在 ui/image/ui/&lt;atlas&gt;.png
    /// （由 SyncAsset 分发），对应引擎路径 image/ui/&lt;atlas&gt;.png。
    /// </summary>
    private static bool TryGetAtlasRegion(AtlasSprite? sprite, out string atlasPath, out RectangleF region, out bool rotated)
    {
        atlasPath = string.Empty;
        region = default;
        rotated = false;
        var file = sprite?.Atlas?.File;
        if (sprite == null || string.IsNullOrWhiteSpace(file))
        {
            return false;
        }
        var fileName = Path.GetFileName(file.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }
        atlasPath = "image/ui/" + fileName;
        if (_atlasPathLogged.Add(file!))
        {
//             Game.Logger.LogInformation("[FGUI][ATLAS] 图集解析: rawFile='{Raw}' -> atlasPath='{Path}' region={Region}",
//                 file, atlasPath, sprite.Rect);
        }
        region = sprite.Rect;
        rotated = sprite.Rotated;
        return true;
    }

    private static readonly HashSet<string> _atlasPathLogged = new(StringComparer.OrdinalIgnoreCase);

    private static bool TryBuildScatterFallbackPath(PackageItem item, out string imagePath)
    {
        imagePath = string.Empty;
        var packageName = item.Owner?.Name;
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return false;
        }

        var itemId = item.Id;
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        var rawName = item.Name;
        if (string.IsNullOrWhiteSpace(rawName))
        {
            rawName = itemId;
        }

        var sanitizedName = NormalizeScatterToken(Path.GetFileNameWithoutExtension(rawName));
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            sanitizedName = itemId;
        }

        imagePath = $"image/fgui/scatter/{packageName}/{itemId}__{sanitizedName}.png";
        return true;
    }

    private static string NormalizeScatterToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var chars = token.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
            {
                continue;
            }

            if (Path.GetInvalidFileNameChars().Contains(ch) || char.IsWhiteSpace(ch) || ch == '/')
            {
                chars[i] = '_';
                continue;
            }

            chars[i] = '_';
        }

        return new string(chars).Trim('_', ' ');
    }

    private static string ResolveSceImagePath(PackageItem atlasItem)
    {
        var atlasFile = NormalizePath(atlasItem.File);
        if (string.IsNullOrEmpty(atlasFile))
        {
            return "image/ui/unknown.png";
        }

        if (atlasFile.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return atlasFile;
        }

        if (atlasFile.StartsWith("ui/image/", StringComparison.OrdinalIgnoreCase))
        {
            return atlasFile.Substring(3);
        }

        var atlasFileName = Path.GetFileName(atlasFile);

        var packageAssetPath = NormalizePath(atlasItem.Owner?.AssetPath);
        if (!string.IsNullOrEmpty(packageAssetPath))
        {
            var packageDir = NormalizePath(Path.GetDirectoryName(packageAssetPath)).Trim('/');
            if (packageDir.Equals("ui", StringComparison.OrdinalIgnoreCase))
            {
                return $"image/ui/{atlasFileName}";
            }

            if (packageDir.StartsWith("ui/image/", StringComparison.OrdinalIgnoreCase))
            {
                return $"{packageDir.Substring(3)}/{atlasFileName}";
            }

            if (packageDir.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return $"{packageDir}/{atlasFileName}";
            }

            if (!string.IsNullOrEmpty(packageDir))
            {
                return $"image/{packageDir}/{atlasFileName}";
            }
        }

        return $"image/ui/{atlasFileName}";
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Replace('\\', '/').TrimStart('/');
    }

    private void ApplyTextProperties(GTextField text, object native)
    {
        if (_adapter == null) return;
        _adapter.SetText(native, text.Text ?? "");
        _adapter.SetTextColor(native, text.Color);
        
        // 字体大小也需要缩放
        float scaleFactor = UIRuntime.ContentScaleFactor;
        int scaledFontSize = (int)(text.FontSize * scaleFactor);
        _adapter.SetFontSize(native, scaledFontSize);

        // 设置字体 family（把 FGUI 字体名/资源引用映射成引擎 family 后套用）
        var fontFamily = FairyGUI.Utils.FGUIFontMap.Resolve(text.Font);
        if (fontFamily != null)
        {
            _adapter.SetFontName(native, fontFamily);
        }

        _adapter.SetBold(native, text.Bold);
        _adapter.SetItalic(native, text.Italic);
        var hAlign = text.Align switch { AlignType.Left => TextAlign.Left, AlignType.Center => TextAlign.Center, AlignType.Right => TextAlign.Right, _ => TextAlign.Left };
        _adapter.SetTextAlign(native, hAlign);
        var vAlign = text.VerticalAlign switch { VertAlignType.Top => TextVerticalAlign.Top, VertAlignType.Middle => TextVerticalAlign.Middle, VertAlignType.Bottom => TextVerticalAlign.Bottom, _ => TextVerticalAlign.Top };
        _adapter.SetTextVerticalAlign(native, vAlign);
    }

    private void ApplyButtonProperties(GButton button, object native)
    {
        if (_adapter == null) return;
        // Ensure native hit-test surface exists for transparent panel-backed buttons.
        _adapter.SetBackgroundColor(native, Color.FromArgb(1, 255, 255, 255));
        ApplyComponentProperties(button, native);
        if (ShouldLogButtonDiag(button))
        {
            System.GC.KeepAlive(0);
        }

        if (_boundButtonNative.Add(native))
        {
            _adapter.OnClick(native, () =>
            {
                if (ShouldLogButtonDiag(button))
                {
                    System.GC.KeepAlive(0);
                }

                if (_gestureDragged || !_clickDispatchedInGesture.Add(button))
                {
                    return;
                }

                button.DispatchEvent("onClick", null);
            });
            _adapter.OnPointerEnter(native, () => button.DispatchEvent("onRollOver", null));
            _adapter.OnPointerLeave(native, () => button.DispatchEvent("onRollOut", null));
        }
    }

    private void BindTouchEvents(GObject obj, object native)
    {
        if (_adapter == null)
        {
            return;
        }

        // 绑定时的转发目标只用来判"要不要挂回调"；回调里一律现找（ResolveTouchRelayTarget）。
        // 原因：控件建好、绑上回调的时候，祖先往往还没挂监听（列表项是渲染回调里才包 Cell、挂 OnClick 的），
        // 那时定死的转发目标是 null，之后祖先挂了监听也收不到 —— 表现为"列表里只有最后建的那一项点得动"。
        var relayTarget = ResolveTouchRelayTarget(obj);
        var shouldBind = ShouldBindTouchEvents(obj, relayTarget);
        if (!shouldBind || !_boundTouchNative.Add(native))
        {
            return;
        }

        _adapter.OnPointerPressWithPosition(native, (x, y) =>
        {
            var relayTarget = ResolveTouchRelayTarget(obj);

            // 新的一次按下 = 新的一次交互，把上一次的派发记录和滑动标记都清掉。
            // 同一次按下会被多个重叠子控件各收一遍，重复清无害。
            _clickDispatchedInGesture.Clear();
            _gestureDragged = false;

            var beginPoint = new PointF(x, y);
            var beginContext = new EventContext
            {
                Sender = obj,
                Type = "onTouchBegin",
                Data = beginPoint
            };
            obj.DispatchEventWithContext("onTouchBegin", beginContext, beginPoint);

            if (relayTarget != null)
            {
                var relayContext = new EventContext
                {
                    Sender = relayTarget,
                    Type = "onTouchBegin",
                    Data = beginPoint
                };
                relayTarget.DispatchEventWithContext("onTouchBegin", relayContext, beginPoint);
            }

            if (!ShouldCaptureForTouchMove(obj, relayTarget))
            {
                return;
            }

            _adapter.CapturePointer(native);
            _touchCapturedNative.Add(native);
        });

        _adapter.OnPointerCapturedMove(native, (x, y) =>
        {
            if (!_touchCapturedNative.Contains(native))
            {
                return;
            }

            var moveRecipient = ResolveTouchRelayTarget(obj) ?? obj;
            var movePoint = new PointF(x, y);
            var moveContext = new EventContext
            {
                Sender = moveRecipient,
                Type = "onTouchMove",
                Data = movePoint
            };
            moveRecipient.DispatchEventWithContext("onTouchMove", moveContext, movePoint);
        });

        _adapter.OnPointerRelease(native, () =>
        {
            var relayTarget = ResolveTouchRelayTarget(obj);
            if (_touchCapturedNative.Remove(native))
            {
                _adapter.ReleasePointer(native);
            }

            var endContext = new EventContext
            {
                Sender = obj,
                Type = "onTouchEnd"
            };
            obj.DispatchEventWithContext("onTouchEnd", endContext, null);

            if (relayTarget != null)
            {
                var relayContext = new EventContext
                {
                    Sender = relayTarget,
                    Type = "onTouchEnd"
                };
                relayTarget.DispatchEventWithContext("onTouchEnd", relayContext, null);
            }
        });

        // 按钮自己的点击在 ApplyButtonProperties 里挂；其余控件点击时现找目标：
        // 有挂了监听的祖先就转给它（FGUI 的冒泡），没有就派发给自己。
        if (obj is not GButton && _boundGenericClickNative.Add(native))
        {
            _adapter.OnClick(native, () =>
            {
                if (_gestureDragged)
                {
                    return;
                }

                var target = ResolveTouchRelayTarget(obj) ?? obj;

                // 同一次交互里这个目标已经收到过就不再派发。
                // 一个 cell 里底图、图标、文字都会各转发一次，不挡的话业务回调要跑好几遍：
                // 幂等的回调（开个页面）看着正常，开关类的回调则"开了又关"，
                // 而真正危险的是像建造摆放那样第二遍会把刚建好的原生对象销毁重建 —— 直接崩进程。
                if (!_clickDispatchedInGesture.Add(target))
                {
                    return;
                }

                target.DispatchEvent("onClick", null);
            });
            _adapter.OnPointerEnter(native, () => (ResolveTouchRelayTarget(obj) ?? obj).DispatchEvent("onRollOver", null));
            _adapter.OnPointerLeave(native, () => (ResolveTouchRelayTarget(obj) ?? obj).DispatchEvent("onRollOut", null));
        }
    }


    private static bool ShouldBindTouchEvents(GObject obj, GObject? relayTarget)
    {
        if (obj is GButton || relayTarget != null)
        {
            return true;
        }

        if (obj.Draggable || obj is GTextInput)
        {
            return true;
        }

        if (obj is GComponent component && component.ScrollPane != null)
        {
            return true;
        }

        // 光设 BlockPointerEvents 不够：没挂指针回调的原生控件不参与命中，点击照样漏到场景（实测）。
        if (IsHitGraph(obj) || IsBlockTouchImage(obj))
        {
            return true;
        }

        return HasAnyTouchListener(obj);
    }

    /// <summary>
    /// 有形状的图形（矩形 / 椭圆…）同 FairyGUI 一样可点：弹窗底下的半透明遮罩靠它挡住点击、也能挂 onClick。
    /// 空白图形（Empty）只是占位，保持穿透；不可触摸（touchable=false）的也不挡。
    /// </summary>
    private static bool IsHitGraph(GObject obj)
    {
        return obj is GGraph graph && graph.Type != GraphType.Empty && graph.Touchable;
    }

    /// <summary>
    /// 图片自定义数据（customData）里写了这一行，就拦截点击。
    /// </summary>
    public const string BlockTouchMarker = "Spark.BlockTouch=1";

    /// <summary>
    /// 标了拦截的图片（自定义数据里有 <see cref="BlockTouchMarker"/> 一行）：挡住点击，不漏到下面。
    ///
    /// <para>
    /// 官方 FairyGUI 的图片一律不接受点击，编辑器里图片的「不可触摸」固定勾着、置灰改不了；
    /// 而发布出来每张图片的可触摸位其实都是 1，运行时区分不出哪张是特意要挡的。
    /// 所以改用自定义数据做显式开关：没标的图片照旧穿透，现有界面不受影响。
    /// 编辑器里由 AzureSailBoardGenerator 插件的「点击」下拉写入。
    /// </para>
    /// </summary>
    private static bool IsBlockTouchImage(GObject obj)
    {
        if (obj is not GImage || !obj.Touchable || obj.Data is not string data || data.Length == 0)
        {
            return false;
        }

        foreach (var line in data.Split('\n'))
        {
            if (string.Equals(line.Trim(), BlockTouchMarker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAnyTouchListener(GObject obj)
    {
        return obj.HasEventListener("onTouchBegin") ||
               obj.HasEventListener("onTouchMove") ||
               obj.HasEventListener("onTouchEnd") ||
               obj.HasEventListener("onClick") ||
               obj.HasEventListener("onRollOver") ||
               obj.HasEventListener("onRollOut");
    }

    private static bool ShouldCaptureForTouchMove(GObject obj, GObject? relayTarget)
    {
        var captureTarget = relayTarget ?? obj;
        var hasMoveListener = obj.HasEventListener("onTouchMove") || (relayTarget?.HasEventListener("onTouchMove") ?? false);
        if (!hasMoveListener)
        {
            return false;
        }

        if (captureTarget is GSlider || captureTarget is GScrollBar)
        {
            return true;
        }

        if (HasSliderAncestor(captureTarget))
        {
            return true;
        }

        if (obj is GButton || obj.Draggable)
        {
            return false;
        }

        if (obj is GComponent component && component.ScrollPane != null)
        {
            return false;
        }

        return true;
    }

    private static bool HasSliderAncestor(GObject obj)
    {
        var current = obj.Parent;
        while (current != null)
        {
            if (current is GSlider || current is GScrollBar)
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static GObject? ResolveTouchRelayTarget(GObject obj)
    {
        if (obj is GButton || HasAnyTouchListener(obj))
        {
            return null;
        }

        var parent = obj.Parent;
        while (parent != null)
        {
            if (HasAnyTouchListener(parent))
            {
                return parent;
            }

            parent = parent.Parent;
        }

        return null;
    }

    private static bool ShouldLogButtonDiag(GObject obj)
    {
        return obj is GButton && !string.IsNullOrEmpty(obj.Name) && obj.Name.StartsWith("btn_", StringComparison.Ordinal);
    }

    private void ApplyComponentProperties(GComponent component, object native)
    {
        if (_adapter == null) return;
        var perfStart = EnablePerfDiag ? Stopwatch.GetTimestamp() : 0L;
        ApplyComponentScrollAndClip(component, native);
        var renderStart = EnablePerfDiag ? Stopwatch.GetTimestamp() : 0L;
        string childPerfName = string.Empty;
        string childPerfPackage = string.Empty;
        var trackChildPerf = EnablePerfDiag && ShouldTrackPerf(component, out childPerfName, out childPerfPackage);
        double maxChildMs = 0;
        string maxChildName = string.Empty;
        string maxChildType = string.Empty;
        int slowChildren = 0;

        foreach (var child in component.Children)
        {
            var childStart = trackChildPerf ? Stopwatch.GetTimestamp() : 0L;
            RenderChild(component, child, syncOrder: false);
            if (trackChildPerf && childStart > 0)
            {
                var childMs = (Stopwatch.GetTimestamp() - childStart) * 1000.0 / Stopwatch.Frequency;
                if (childMs > maxChildMs)
                {
                    maxChildMs = childMs;
                    maxChildName = string.IsNullOrWhiteSpace(child.Name) ? "<unnamed>" : child.Name;
                    maxChildType = child.GetType().Name;
                }

                if (childMs >= 6.0)
                {
                    slowChildren++;
                }
            }
        }

        ApplyComponentMask(component, native);
        var renderEnd = EnablePerfDiag ? Stopwatch.GetTimestamp() : 0L;
        SyncNativeChildOrder(component, native);
        var syncEnd = EnablePerfDiag ? Stopwatch.GetTimestamp() : 0L;
        if (EnablePerfDiag)
        {
            LogRenderPerf(component, "apply", component.Children.Count, perfStart);
            LogRenderPerfBreakdown(component, component.Children.Count, renderStart, renderEnd, syncEnd);
            if (trackChildPerf)
            {
                LogRenderPerfSlowestChild(childPerfPackage, childPerfName, maxChildName, maxChildType, component.Children.Count, maxChildMs, slowChildren);
            }
        }
    }

    private void ApplyComponentScrollAndClip(GComponent component, object native)
    {
        if (_adapter == null)
        {
            return;
        }

        var shouldClip =
            component.Overflow == OverflowType.Hidden ||
            component.Overflow == OverflowType.Scroll ||
            component.ScrollPane != null;
        if (shouldClip && component.ScrollPane == null)
        {
            component.EnsureBoundsCorrect();
        }
        _adapter.SetClipContent(native, shouldClip);

        if (component.ScrollPane != null)
        {
            var scrollPane = component.ScrollPane;
            scrollPane.SetViewSize(component.Width, component.Height);
            component.EnsureBoundsCorrect();

            var horizontal = ResolveNativeHorizontal(scrollPane);
            var needsNativeScroll = ResolveNativeScrollEnabled(scrollPane, horizontal);
            var isManualVirtualFallback = component is GList list && list.IsVirtual && !list.IsUsingNativeVirtualization;
            _scrollableHorizontalByNative[native] = horizontal;
            _adapter.ConfigureScrollable(native, enabled: needsNativeScroll, horizontal: horizontal);

            // 滚动条拖杆尺寸按 FGUI 编辑器里的显示设置映射：Hidden→隐藏(0)，其余→默认粗细。
            // 这样是否显示拖杆可直接在编辑器里逐列表配置，而不用改代码。
            var scrollBarSize = scrollPane.ScrollBarDisplay == ScrollBarDisplayType.Hidden ? 0f : DefaultScrollBarSize;
            _adapter.SetScrollBarSize(native, scrollBarSize);

            if (needsNativeScroll && scrollPane.ScrollType == ScrollType.Both && _bothAxisFallbackLogged.Add(native))
            {
                System.GC.KeepAlive(0);
            }

            if (needsNativeScroll && _boundScrollableNative.Add(native))
            {
                _adapter.OnScrollChanged(native, value =>
                {
                    if (_syncingScrollableToNative.Contains(native))
                    {
                        return;
                    }

                    var pane = component.ScrollPane;
                    if (pane == null)
                    {
                        return;
                    }

                    // 拖动中、以及松手后的惯性/回弹 tween 期间，都以 FGUI 的 _xPos/_yPos 为唯一来源，
                    // 忽略原生回读。否则：
                    // 1) 过界时原生夹紧值(0/1)会把 _yPos 拉回边缘，按住也"自己恢复"；
                    // 2) tween 期间原生延迟一帧的量化回声若被写回，会触发 SetPos→KillTween，
                    //    把回弹动画反复打断重启，在边界处表现为"打哆嗦"且收不了尾、"划不动"。
                    if (pane.IsDragging || pane.IsTweening)
                    {
                        return;
                    }

                    var isHorizontalAxis = _scrollableHorizontalByNative.TryGetValue(native, out var axisHorizontal)
                        ? axisHorizontal
                        : pane.ScrollType == ScrollType.Horizontal;

                    // 死区：忽略与当前值几乎相等的原生回读。这类回读是我们推送后原生量化产生的回声，
                    // 若照单回写会与 FGUI 的 _xPos/_yPos 每帧互相纠正，表现为滚动"打哆嗦"。
                    var currentPercent = isHorizontalAxis ? pane.PercentX : pane.PercentY;
                    if (MathF.Abs(value - currentPercent) < 0.003f)
                    {
                        return;
                    }

                    _syncingScrollableFromNative.Add(native);
                    try
                    {
                        var isHorizontal = isHorizontalAxis;
                        if (isHorizontal)
                        {
                            pane.PercentX = value;
                        }
                        else
                        {
                            pane.PercentY = value;
                        }
                    }
                    finally
                    {
                        _syncingScrollableFromNative.Remove(native);
                    }
                });
            }

            // Manual drag bridge: ensures list drag works even when child controls consume pointer events.
            // 内容没超出视口（needsNativeScroll=false）时原生滚动不开，但开着拖动效果 + 回弹的列表照样挂桥：
            // 拖动走 FGUI 的橡皮筋（ScrollPane.OnTouchMove 的 bounceback 分支），过界量由 SetScrollOverscroll
            // 平移面板本体画出来（与原生滚动开没开无关），松手回弹归位 —— 内容少时也能拖出弹性，不再"拖不动"。
            // 桥按 native 只挂一次；内容后来超出了也照用同一套，不冲突。
            var useManualPointerBridge = isManualVirtualFallback || needsNativeScroll || CanBounceWithoutOverflow(scrollPane);
            if (useManualPointerBridge && _boundScrollablePointerNative.Add(native))
            {
                _adapter.OnPointerPressWithPosition(native, (x, y) =>
                {
                    var pane = component.ScrollPane;
                    if (pane == null)
                    {
                        return;
                    }

                    _adapter.CapturePointer(native);
                    // 延迟到首次明显移动再决定滚动归属：沿自身轴则自己滚，垂直于自身轴则转发给外层。
                    // 这样既不会把点击误当成滚动，也支持嵌套列表的方向分流。
                    _scrollDragStates[native] = new ScrollDragState(x, y);
                });

                _adapter.OnPointerCapturedMove(native, (x, y) =>
                {
                    if (_scrollDragStates.TryGetValue(native, out var drag))
                    {
                        MoveScrollDrag(component, native, horizontal, drag, x, y);
                    }
                });

                _adapter.OnPointerRelease(native, () =>
                {
                    _adapter.ReleasePointer(native);
                    if (_scrollDragStates.Remove(native, out var drag))
                    {
                        EndScrollDrag(drag);
                    }
                });
            }

            if (!needsNativeScroll && _boundScrollableWheelNative.Add(native))
            {
                _adapter.OnMouseWheel(native, delta =>
                {
                    var pane = component.ScrollPane;
                    pane?.OnMouseWheel(delta);
                });
            }

            if (!_syncingScrollableFromNative.Contains(native))
            {
                if (needsNativeScroll)
                {
                    var isHorizontal = _scrollableHorizontalByNative.TryGetValue(native, out var mappedHorizontal)
                        ? mappedHorizontal
                        : scrollPane.ScrollType == ScrollType.Horizontal;
                    var currentPercent = isHorizontal
                        ? scrollPane.PercentX
                        : scrollPane.PercentY;
                    var clamped = float.IsNaN(currentPercent) || float.IsInfinity(currentPercent)
                        ? 0f
                        : Math.Clamp(currentPercent, 0f, 1f);

                    _syncingScrollableToNative.Add(native);
                    try
                    {
                        _adapter.SetScrollValue(native, clamped);
                    }
                    finally
                    {
                        _syncingScrollableToNative.Remove(native);
                    }
                }
                else
                {
                    _adapter.SetScrollValue(native, 0f);
                }
            }

            return;
        }

        _adapter.ConfigureScrollable(native, enabled: false, horizontal: false);
        _scrollableHorizontalByNative.Remove(native);
        _bothAxisFallbackLogged.Remove(native);
    }

    public void RenderChild(GComponent parent, GObject child, bool syncOrder = true)
    {
        if (_adapter == null) return;

        if (!child.FinalVisible)
        {
            if (child.NativeObject != null)
            {
                RemoveFromParent(child);
            }

            if (syncOrder && parent.NativeObject != null)
            {
                SyncNativeChildOrder(parent, parent.NativeObject);
            }

            return;
        }

        var childNative = CreateNativeControl(child);
        if (childNative == null) 
        {
            System.GC.KeepAlive(0);
            return;
        }
        if (parent.NativeObject != null) 
        {
            var parentNative = parent.NativeObject;
            var isAlreadyAttachedToParent =
                _nativeParentOfChild.TryGetValue(childNative, out var attachedParent) &&
                ReferenceEquals(attachedParent, parentNative);

            if (attachedParent != null && !ReferenceEquals(attachedParent, parentNative))
            {
                _adapter.RemoveChild(attachedParent, childNative);
            }

            // Keep native hierarchy order aligned with FairyGUI child index semantics.
            // SCE adapter only exposes append AddChild, so we replay children in order.
            if (syncOrder)
            {
                SyncNativeChildOrder(parent, parentNative);
            }
            if (!isAlreadyAttachedToParent)
            {
                if (IsDemoGraphProbe(child))
                {
                    LogDemoGraphProbe("RenderChildAdd", child);
                }

                // 刚挂（或重新挂）到父节点上：把位置、尺寸再推一遍。
                // 显隐控制器切隐藏时子节点会被摘下（ChildStateChanged → RemoveFromParent），切回来在这里挂回；
                // 隐藏期间建出来的原生节点（构造时设文字就会建）位置尺寸是设在没挂进界面树的控件上的，
                // 挂上后实测可能停在父节点原点、0×0（2026-10-14 英雄卡「已出战」文字飘到卡片外）。
                if (_nativeParentOfChild.TryGetValue(childNative, out var nowParent) && ReferenceEquals(nowParent, parentNative))
                {
                    ReapplyGeometry(child, childNative);
                }
            }
        }

        BindManualVirtualItemDragBridge(parent, childNative);
    }

    private void BindManualVirtualItemDragBridge(GComponent parent, object childNative)
    {
        if (_adapter == null || parent is not GList list || parent.NativeObject == null || parent.ScrollPane == null)
        {
            return;
        }

        if (!list.IsVirtual || list.IsUsingNativeVirtualization)
        {
            return;
        }

        if (!_boundScrollableItemPointerNative.Add(childNative))
        {
            return;
        }

        _adapter.OnPointerPressWithPosition(childNative, (x, y) =>
        {
            var pane = parent.ScrollPane;
            if (pane == null)
            {
                return;
            }

            _adapter.CapturePointer(childNative);
            _scrollableItemCapturedNative.Add(childNative);
            pane.OnTouchBegin(x, y);
        });

        _adapter.OnPointerCapturedMove(childNative, (x, y) =>
        {
            if (!_scrollableItemCapturedNative.Contains(childNative))
            {
                return;
            }

            var pane = parent.ScrollPane;
            var parentNative = parent.NativeObject;
            if (pane == null || parentNative == null)
            {
                return;
            }

            pane.OnTouchMove(x, y);
            SyncNativeScrollFromPane(parentNative, pane);
        });

        _adapter.OnPointerRelease(childNative, () =>
        {
            if (_scrollableItemCapturedNative.Remove(childNative))
            {
                _adapter.ReleasePointer(childNative);
            }

            var pane = parent.ScrollPane;
            var parentNative = parent.NativeObject;
            if (pane == null || parentNative == null)
            {
                return;
            }

            pane.OnTouchEnd();
            SyncNativeScrollFromPane(parentNative, pane);
        });
    }

    private void SyncNativeScrollFromPane(object native, ScrollPane pane)
    {
        if (_syncingScrollableFromNative.Contains(native))
        {
            return;
        }

        var isHorizontal = _scrollableHorizontalByNative.TryGetValue(native, out var mappedHorizontal)
            ? mappedHorizontal
            : pane.ScrollType == ScrollType.Horizontal;
        var percent = isHorizontal ? pane.PercentX : pane.PercentY;
        var clampedPercent = float.IsNaN(percent) || float.IsInfinity(percent)
            ? 0f
            : Math.Clamp(percent, 0f, 1f);

        _syncingScrollableToNative.Add(native);
        try
        {
            _adapter!.SetScrollValue(native, clampedPercent);
        }
        finally
        {
            _syncingScrollableToNative.Remove(native);
        }
    }

    private void ApplyComponentMask(GComponent component, object native)
    {
        if (_adapter == null)
        {
            return;
        }

        var maskNative = component.MaskObject?.NativeObject;
        _adapter.SetMaskControl(native, maskNative, component.MaskInverted);

        // FairyGUI 语义：遮罩对象只用于定义遮罩区域，本身不作为可见内容渲染。
        // 星火原生控件用不了它做遮罩，若不隐藏，这张遮罩图会被当普通子控件直接显示出来。
        if (maskNative != null)
        {
            _adapter.SetVisible(maskNative, false);
        }
    }

    private void SyncNativeChildOrder(GComponent parent, object parentNative)
    {
        if (_adapter == null)
        {
            return;
        }

        var perfStart = EnablePerfDiag ? Stopwatch.GetTimestamp() : 0L;
        var visualOrder = 0;
        foreach (var sibling in parent.Children)
        {
            if (!sibling.FinalVisible)
            {
                continue;
            }

            var siblingNative = sibling.NativeObject;
            if (siblingNative == null)
            {
                continue;
            }

            AttachChildInVisualOrder(parentNative, siblingNative);
            _adapter.SetZIndex(siblingNative, visualOrder++);
        }

        if (EnablePerfDiag)
        {
            LogRenderPerf(parent, "sync", visualOrder, perfStart);
        }
    }

    private void LogRenderPerf(GComponent component, string phase, int nodeCount, long startTimestamp)
    {
        if (_remainingPerfLogs <= 0 || startTimestamp <= 0)
        {
            return;
        }

        if (!ShouldTrackPerf(component, out var perfName, out var packageName))
        {
            return;
        }

        var elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        var key = $"{packageName}/{perfName}:{phase}";
        if (!_renderPerfStats.TryGetValue(key, out var stat))
        {
            stat = new RenderPerfStat();
            _renderPerfStats[key] = stat;
        }

        stat.Count++;
        stat.TotalMs += elapsedMs;
        if (elapsedMs > stat.MaxMs)
        {
            stat.MaxMs = elapsedMs;
        }

        var shouldLog = stat.Count <= 5 || elapsedMs >= 12.0 || stat.Count % 20 == 0;
        if (!shouldLog)
        {
            return;
        }

        _remainingPerfLogs--;
        System.GC.KeepAlive(0);
    }

    private void LogRenderPerfBreakdown(GComponent component, int nodeCount, long renderStart, long renderEnd, long syncEnd)
    {
        if (_remainingPerfLogs <= 0 || renderStart <= 0 || renderEnd < renderStart || syncEnd < renderEnd)
        {
            return;
        }

        if (!ShouldTrackPerf(component, out var perfName, out var packageName))
        {
            return;
        }

        var renderMs = (renderEnd - renderStart) * 1000.0 / Stopwatch.Frequency;
        var syncMs = (syncEnd - renderEnd) * 1000.0 / Stopwatch.Frequency;
        if (renderMs < 8.0 && syncMs < 8.0)
        {
            return;
        }

        _remainingPerfLogs--;
        System.GC.KeepAlive(0);
    }

    private void LogRenderPerfSlowestChild(string packageName, string componentName, string childName, string childType, int nodeCount, double maxChildMs, int slowChildren)
    {
        if (_remainingPerfLogs <= 0 || maxChildMs < 8.0)
        {
            return;
        }

        _remainingPerfLogs--;
        System.GC.KeepAlive(0);
    }

    private bool ShouldTrackPerf(GComponent component, out string perfName, out string packageName)
    {
        packageName = component.PackageItem?.Owner?.Name ?? string.Empty;
        var name = component.Name ?? string.Empty;
        var packageItemName = component.PackageItem?.Name ?? string.Empty;
        perfName = !string.IsNullOrWhiteSpace(packageItemName) ? packageItemName : name;

        if (string.IsNullOrWhiteSpace(perfName))
        {
            perfName = "<unnamed>";
        }

        var tracked =
            perfName.Equals("Demo_Button", StringComparison.OrdinalIgnoreCase) ||
            perfName.Equals("Demo_List", StringComparison.OrdinalIgnoreCase) ||
            perfName.Equals("Demo_Grid", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Demo_Button", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Demo_List", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Demo_Grid", StringComparison.OrdinalIgnoreCase);

        if (EnablePerfProbeLogs && tracked && packageName.Equals("Basics", StringComparison.OrdinalIgnoreCase))
        {
            var probeKey = $"{packageName}/{perfName}";
            if (_perfProbeLogged.Add(probeKey))
            {
                System.GC.KeepAlive(0);
            }
        }

        return tracked;
    }

    private sealed class RenderPerfStat
    {
        public int Count;
        public double TotalMs;
        public double MaxMs;
    }

    private void AttachChildInVisualOrder(object parentNative, object childNative)
    {
        if (_adapter == null)
        {
            return;
        }

        if (_nativeParentOfChild.TryGetValue(childNative, out var attachedParent))
        {
            // Avoid expensive remove/add churn when child is already attached to this parent.
            // Sibling order is maintained through SetZIndex in SyncNativeChildOrder.
            if (ReferenceEquals(attachedParent, parentNative))
            {
                return;
            }

            _adapter.RemoveChild(attachedParent, childNative);
        }

        _adapter.AddChild(parentNative, childNative);
        _nativeParentOfChild[childNative] = parentNative;
    }

    public void Tick(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            return;
        }

        // Drive FairyGUI tweens every tick; display-lock release depends on tween completion.
        GTween.Update(deltaSeconds);

        if (_adapter == null || _activeMovieClips.Count == 0)
        {
            return;
        }

        List<GMovieClip>? stale = null;
        foreach (var movieClip in _activeMovieClips)
        {
            if (movieClip.Disposed || movieClip.NativeObject == null || movieClip.PackageItem == null)
            {
                stale ??= [];
                stale.Add(movieClip);
                continue;
            }

            if (!movieClip.Advance(deltaSeconds))
            {
                continue;
            }

            ApplyMovieClipProperties(movieClip, movieClip.NativeObject);
            movieClip.ApplyNativeVisualState();
        }

        if (stale == null)
        {
            return;
        }

        foreach (var clip in stale)
        {
            _activeMovieClips.Remove(clip);
            _movieClipFramePathCache.Remove(clip);
        }
    }

    public void UpdatePosition(GObject obj) 
    { 
        if (_adapter == null || obj.NativeObject == null) return;
        if (obj is FGUIRoot) return;
        float scaleFactor = UIRuntime.ContentScaleFactor;
        var renderPos = ResolveRenderPosition(obj);
        _adapter.SetPosition(obj.NativeObject, renderPos.X * scaleFactor, renderPos.Y * scaleFactor); 
        if (obj is GImage image)
        {
            image.ApplyNativeVisualState();
        }
        if (IsDemoGraphProbe(obj))
        {
            LogDemoGraphProbe("UpdatePosition", obj);
        }
    }
    
    /// <summary>
    /// 只把位置和尺寸重新推给原生节点（不碰滚动、裁剪等其它状态），给"重新挂到父节点"时补一次用，见 <see cref="RenderChild"/>。
    /// </summary>
    private void ReapplyGeometry(GObject obj, object native)
    {
        if (_adapter == null || obj is FGUIRoot)
        {
            return;
        }

        float scaleFactor = UIRuntime.ContentScaleFactor;
        var renderPos = ResolveRenderPosition(obj);
        _adapter.SetPosition(native, renderPos.X * scaleFactor, renderPos.Y * scaleFactor);
        _adapter.SetSize(native, obj.Width * scaleFactor, obj.Height * scaleFactor);
    }

    public void UpdateSize(GObject obj)
    { 
        if (_adapter == null || obj.NativeObject == null) return;
        if (obj is FGUIRoot) return;
        float scaleFactor = UIRuntime.ContentScaleFactor;
        _adapter.SetSize(obj.NativeObject, obj.Width * scaleFactor, obj.Height * scaleFactor);
        if (obj is GImage image)
        {
            image.ApplyNativeVisualState();
        }
        if (IsDemoGraphProbe(obj))
        {
            LogDemoGraphProbe("UpdateSize", obj);
        }
        if (obj is GComponent component)
        {
            ApplyComponentScrollAndClip(component, obj.NativeObject);
        }
    }

    private static bool IsDemoGraphProbe(GObject obj)
    {
        if (obj == null)
        {
            return false;
        }

        var packageOk = string.Equals(obj.PackageItem?.Owner?.Name, "Basics", StringComparison.OrdinalIgnoreCase);
        if (!packageOk)
        {
            return false;
        }

        var parent = obj.Parent;
        if (parent == null)
        {
            return false;
        }

        var parentPkgName = parent.PackageItem?.Name;
        var parentOk = string.Equals(parentPkgName, "Demo_Graph", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parent.Name, "Demo_Graph", StringComparison.OrdinalIgnoreCase);
        if (!parentOk)
        {
            return false;
        }

        if (string.Equals(obj.Name, "n14", StringComparison.Ordinal)
            || string.Equals(obj.Name, "radial", StringComparison.Ordinal))
        {
            return true;
        }

        var childIndex = parent.GetChildIndex(obj);
        return childIndex == 13 || childIndex == 14;
    }

    private static PointF CalcAbsolutePosition(GObject obj)
    {
        var absX = obj.X;
        var absY = obj.Y;
        var parent = obj.Parent;
        while (parent != null)
        {
            absX += parent.X;
            absY += parent.Y;
            parent = parent.Parent;
        }

        return new PointF(absX, absY);
    }

    private static PointF ResolveRenderPosition(GObject obj)
    {
        var x = obj.X;
        var y = obj.Y;

        // FairyGUI pivot-as-anchor: X/Y represent pivot point, while SCE control position is top-left.
        // Convert anchor coordinates into top-left before handing off to SCE.
        if (obj.PivotAsAnchor && (obj.PivotX != 0f || obj.PivotY != 0f))
        {
            x -= obj.Width * obj.PivotX;
            y -= obj.Height * obj.PivotY;
        }

        return new PointF(x, y);
    }

    private static void LogDemoGraphProbe(string stage, GObject obj)
    {
        var scaleFactor = UIRuntime.ContentScaleFactor;
        var abs = CalcAbsolutePosition(obj);
        var renderPos = ResolveRenderPosition(obj);
        var childIndex = obj.Parent?.GetChildIndex(obj) ?? -1;
        System.GC.KeepAlive(0);
    }

    public void RefreshComponentScrollState(GComponent component)
    {
        if (_adapter == null)
        {
            return;
        }

        var native = component.NativeObject;
        if (native == null)
        {
            return;
        }

        ApplyComponentScrollAndClip(component, native);
    }
    
    public void UpdateVisibility(GObject obj) { if (_adapter != null && obj.NativeObject != null) _adapter.SetVisible(obj.NativeObject, obj.Visible); }
    public void UpdateAlpha(GObject obj) { if (_adapter != null && obj.NativeObject != null) _adapter.SetOpacity(obj.NativeObject, obj.Alpha); }

    public void AddToRoot(GObject obj)
    {
        if (_adapter == null) return;
        
        // 创建原生控件（如果还没有）
        if (obj.NativeObject == null)
        {
            CreateNativeControl(obj);
        }
        
        if (obj.NativeObject != null)
        {
            if (obj is FGUIRoot)
            {
                _adapter.AddToRoot(obj.NativeObject);
            }
            else
            {
                // Stage-direct mode mounts ordinary root objects with fixed bounds.
                var scale = MathF.Max(0.0001f, UIRuntime.ContentScaleFactor);
                var nativeWidth = obj.Width * scale;
                var nativeHeight = obj.Height * scale;
                _adapter.AddToRootWithFixedSize(obj.NativeObject, nativeWidth, nativeHeight);
            }
            System.GC.KeepAlive(0);
        }
    }

    public void RemoveFromParent(GObject obj)
    {
        if (_adapter == null || obj.NativeObject == null) return;
        _touchCapturedNative.Remove(obj.NativeObject);
        _scrollableItemCapturedNative.Remove(obj.NativeObject);
        _adapter.ReleasePointer(obj.NativeObject);
        _adapter.RemoveFromParent(obj.NativeObject);
        ClearNativeAttachmentTracking(obj.NativeObject, clearChildren: false);
    }

    public void DisposeNative(GObject obj)
    {
        if (_adapter == null || obj.NativeObject == null) return;
        var native = obj.NativeObject;
        _touchCapturedNative.Remove(native);
        _adapter.ReleasePointer(native);
        if (obj is GMovieClip movieClip)
        {
            _activeMovieClips.Remove(movieClip);
            _movieClipFramePathCache.Remove(movieClip);
        }
        _boundButtonNative.Remove(native);
        _boundGenericClickNative.Remove(native);
        _boundTouchNative.Remove(native);
        _boundScrollableNative.Remove(native);
        _boundScrollablePointerNative.Remove(native);
        _boundScrollChildPointerNative.Remove(native);
        _scrollDragStates.Remove(native);
        _boundScrollableWheelNative.Remove(native);
        _boundScrollableItemPointerNative.Remove(native);
        _scrollableHorizontalByNative.Remove(native);
        _bothAxisFallbackLogged.Remove(native);
        _syncingScrollableToNative.Remove(native);
        _syncingScrollableFromNative.Remove(native);
        _scrollableItemCapturedNative.Remove(native);
        _adapter.Dispose(native);
        ClearNativeAttachmentTracking(native, clearChildren: true);
        obj.NativeObject = null;
    }

    private void ClearNativeAttachmentTracking(object native, bool clearChildren)
    {
        _nativeParentOfChild.Remove(native);
        if (!clearChildren || _nativeParentOfChild.Count == 0)
        {
            return;
        }

        List<object>? danglingChildren = null;
        foreach (var pair in _nativeParentOfChild)
        {
            if (!ReferenceEquals(pair.Value, native))
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

        foreach (var childNative in danglingChildren)
        {
            _nativeParentOfChild.Remove(childNative);
        }
    }

    private static bool ResolveNativeHorizontal(ScrollPane pane)
    {
        if (pane.ScrollType == ScrollType.Horizontal)
        {
            return true;
        }

        if (pane.ScrollType != ScrollType.Both)
        {
            return false;
        }

        var overflowX = Math.Max(0f, pane.ContentWidth - pane.ViewWidth);
        var overflowY = Math.Max(0f, pane.ContentHeight - pane.ViewHeight);
        if (overflowX > overflowY)
        {
            return true;
        }

        if (overflowY > overflowX)
        {
            return false;
        }

        return pane.PercentX > pane.PercentY;
    }

    /// <summary>
    /// 内容没超出视口时要不要也能拖：开着拖动效果（touchEffect）和回弹（bounceback）就要 ——
    /// 拖出橡皮筋、松手弹回，手感上告诉玩家"这是个列表，只是到头了"。两者任一关掉就不拖（与 FGUI 编辑器里的设置一致）。
    /// </summary>
    private static bool CanBounceWithoutOverflow(ScrollPane pane) => pane.TouchEffect && pane.BouncebackEffect;

    /// <summary>这个滚动面板沿给定轴的内容是否超出视口（能真正滚动）。</summary>
    private static bool HasOverflow(ScrollPane pane, bool horizontalAxis)
    {
        const float epsilon = 0.01f;
        return horizontalAxis
            ? pane.ContentWidth - pane.ViewWidth > epsilon
            : pane.ContentHeight - pane.ViewHeight > epsilon;
    }

    private static bool ResolveNativeScrollEnabled(ScrollPane pane, bool horizontalAxis)
    {
        var overflowX = Math.Max(0f, pane.ContentWidth - pane.ViewWidth);
        var overflowY = Math.Max(0f, pane.ContentHeight - pane.ViewHeight);
        const float epsilon = 0.01f;

        if (pane.ScrollType == ScrollType.Both)
        {
            return overflowX > epsilon || overflowY > epsilon;
        }

        return horizontalAxis ? overflowX > epsilon : overflowY > epsilon;
    }

    /// <summary>
    /// 拖动桥的一次移动：还没判定归属时，等位移过 <see cref="ScrollDirectionThreshold"/> 再按方向决定由谁滚
    /// （沿 <paramref name="owner"/> 自身轴则自己滚，垂直则转给外层同向列表；同轴但自己没超出、外层能滚也让给外层），
    /// 判定后每次移动交给那个 ScrollPane。列表自己的桥与列表里子控件的桥（<see cref="BindScrollChildDragBridge"/>）共用。
    /// </summary>
    private void MoveScrollDrag(GComponent owner, object native, bool horizontal, ScrollDragState drag, float x, float y)
    {
        var pane = owner.ScrollPane;
        if (pane == null)
        {
            return;
        }

        if (!drag.Decided)
        {
            var dxTotal = x - drag.StartX;
            var dyTotal = y - drag.StartY;
            if (MathF.Abs(dxTotal) < ScrollDirectionThreshold && MathF.Abs(dyTotal) < ScrollDirectionThreshold)
            {
                return;
            }

            var dragHorizontal = MathF.Abs(dxTotal) > MathF.Abs(dyTotal);
            // 同轴、但自己内容没超出、外层同轴列表能滚：让给外层滚，不在内层原地弹。
            // （内容少的内层列表挂了回弹桥之后，不这么分流就会把外层列表的滚动截下来。）
            var sameAxisAncestor = dragHorizontal == horizontal && !HasOverflow(pane, horizontal)
                ? FindAncestorScrollable(owner, dragHorizontal)
                : null;
            if (sameAxisAncestor.HasValue && HasOverflow(sameAxisAncestor.Value.Pane, sameAxisAncestor.Value.Horizontal))
            {
                drag.ActivePane = sameAxisAncestor.Value.Pane;
                drag.ActiveNative = sameAxisAncestor.Value.Native;
                drag.ActiveHorizontal = sameAxisAncestor.Value.Horizontal;
            }
            else if (dragHorizontal == horizontal)
            {
                // 拖拽方向与本列表滚动轴一致：本列表处理
                drag.ActivePane = pane;
                drag.ActiveNative = native;
                drag.ActiveHorizontal = horizontal;
            }
            else
            {
                // 垂直于本列表滚动轴：转发给方向匹配的滚动祖先（外层列表）
                var ancestor = FindAncestorScrollable(owner, dragHorizontal);
                if (ancestor.HasValue)
                {
                    drag.ActivePane = ancestor.Value.Pane;
                    drag.ActiveNative = ancestor.Value.Native;
                    drag.ActiveHorizontal = ancestor.Value.Horizontal;
                }
                else
                {
                    drag.ActivePane = pane;
                    drag.ActiveNative = native;
                    drag.ActiveHorizontal = horizontal;
                }
            }

            drag.Decided = true;

            // 这一刻起本次交互算"滑动"了，松手时那一项不该再收到点击。
            _gestureDragged = true;

            drag.ActivePane.OnTouchBegin(drag.StartX, drag.StartY);
        }

        if (drag.ActivePane == null || drag.ActiveNative == null)
        {
            return;
        }

        // OnTouchMove 内部会经 UpdateScrollPosition → SyncScrollPaneToNative 同步到原生，
        // 这里不再重复推送，避免每帧双推放大抖动。
        drag.ActivePane.OnTouchMove(x, y);
    }

    /// <summary>拖动桥松手：判定过归属的，结束那个 ScrollPane 的拖动（惯性 / 回弹）并同步到原生。</summary>
    private void EndScrollDrag(ScrollDragState drag)
    {
        if (drag.Decided && drag.ActivePane != null && drag.ActiveNative != null)
        {
            drag.ActivePane.OnTouchEnd();
            SyncPaneToNative(drag.ActiveNative, drag.ActivePane, drag.ActiveHorizontal);
        }
    }

    /// <summary>
    /// 列表里会拦截指针的子控件（按钮、有命中面的图形、标了拦截的图片）也挂一座拖动桥。
    ///
    /// 这些控件开了 BlockPointerEvents，按下事件停在它们身上、到不了列表，列表自己的桥收不到 ——
    /// 表现为"按在卡片上拖不动，按在卡片之间的空白处才拖得动"。这里在子控件上接住按下，往上找最近一个
    /// 挂了拖动桥的滚动祖先，之后的方向判定、滚动、松手全走列表那一套（<see cref="MoveScrollDrag"/>）。
    /// 不动就是普通点击；拖过了 <see cref="ScrollDirectionThreshold"/> 由 _gestureDragged 吞掉松手时的点击。
    /// 祖先在按下时现找，子控件被池复用、换了列表也对。
    /// </summary>
    private void BindScrollChildDragBridge(GObject obj, object native)
    {
        if (_adapter == null || !_boundScrollChildPointerNative.Add(native))
        {
            return;
        }

        _adapter.OnPointerPressWithPosition(native, (x, y) =>
        {
            // 手动虚拟列表的列表项另有一座桥（BindManualVirtualItemDragBridge），不重复接。
            if (_boundScrollableItemPointerNative.Contains(native))
            {
                return;
            }

            var owner = FindDragBridgedAncestor(obj);
            if (owner == null || owner.NativeObject == null)
            {
                return;
            }

            _adapter.CapturePointer(native);
            _scrollDragStates[native] = new ScrollDragState(x, y) { Owner = owner };
        });

        _adapter.OnPointerCapturedMove(native, (x, y) =>
        {
            if (!_scrollDragStates.TryGetValue(native, out var drag)
                || drag.Owner == null
                || drag.Owner.NativeObject == null)
            {
                return;
            }

            var owner = drag.Owner;
            var ownerNative = owner.NativeObject;
            var horizontal = _scrollableHorizontalByNative.TryGetValue(ownerNative, out var mapped)
                ? mapped
                : owner.ScrollPane?.ScrollType == ScrollType.Horizontal;
            MoveScrollDrag(owner, ownerNative, horizontal, drag, x, y);
        });

        _adapter.OnPointerRelease(native, () =>
        {
            if (_scrollDragStates.Remove(native, out var drag))
            {
                _adapter.ReleasePointer(native);
                EndScrollDrag(drag);
            }
        });
    }

    /// <summary>从 <paramref name="obj"/> 往上找最近一个挂了拖动桥（_boundScrollablePointerNative）的滚动组件；没有返回 null。</summary>
    private GComponent? FindDragBridgedAncestor(GObject obj)
    {
        var parent = obj.Parent;
        while (parent != null)
        {
            if (parent.ScrollPane != null
                && parent.NativeObject != null
                && _boundScrollablePointerNative.Contains(parent.NativeObject))
            {
                return parent;
            }

            parent = parent.Parent;
        }

        return null;
    }

    /// <summary>
    /// 从指定组件向上找一个滚动方向为 wantHorizontal 的可滚动祖先，用于嵌套滚动方向分流。
    /// </summary>
    private (ScrollPane Pane, object Native, bool Horizontal)? FindAncestorScrollable(GComponent from, bool wantHorizontal)
    {
        var parent = from.Parent;
        while (parent != null)
        {
            var pane = parent.ScrollPane;
            var parentNative = parent.NativeObject;
            if (pane != null && parentNative != null)
            {
                var horizontal = _scrollableHorizontalByNative.TryGetValue(parentNative, out var mapped)
                    ? mapped
                    : pane.ScrollType == ScrollType.Horizontal;
                if (horizontal == wantHorizontal)
                {
                    return (pane, parentNative, horizontal);
                }
            }

            parent = parent.Parent;
        }

        return null;
    }

    /// <summary>
    /// 把某个组件 ScrollPane 的当前逻辑位置同步到它的 native 面板。
    /// 由 ScrollPane.UpdateScrollPosition 每次滚动（含松手后的惯性/回弹 tween）调用，
    /// 使 FGUI 的滚动物理成为唯一来源、内外列表手感一致。
    /// </summary>
    internal void SyncScrollPaneToNative(GComponent owner)
    {
        if (_adapter == null)
        {
            return;
        }

        var pane = owner.ScrollPane;
        var native = owner.NativeObject;
        if (pane == null || native == null)
        {
            return;
        }

        var horizontal = _scrollableHorizontalByNative.TryGetValue(native, out var mapped)
            ? mapped
            : pane.ScrollType == ScrollType.Horizontal;
        SyncPaneToNative(native, pane, horizontal);

        // 橡皮筋过界：FGUI 的 _xPos/_yPos 在 bounceback 分支会超过 [0,max]，
        // 原生滚动值被夹在 [0,1] 画不出过界，这里把超出量作为整体平移叠加到列表上，
        // 松手后回弹 tween 会把 _xPos/_yPos 收回边缘，过界量归 0、平移归位。
        var maxX = MathF.Max(0f, pane.ContentWidth - pane.ViewWidth);
        var maxY = MathF.Max(0f, pane.ContentHeight - pane.ViewHeight);
        var overX = pane.ScrollingPosX - Math.Clamp(pane.ScrollingPosX, 0f, maxX);
        var overY = pane.ScrollingPosY - Math.Clamp(pane.ScrollingPosY, 0f, maxY);
        // 内容随滚动位置反向移动，过界平移方向取负；乘缩放因子对齐原生坐标。
        var scale = UIRuntime.ContentScaleFactor;
        _adapter.SetScrollOverscroll(native, -overX * scale, -overY * scale);
    }

    /// <summary>把 ScrollPane 当前百分比推到对应 native 面板，驱动其可视滚动。</summary>
    private void SyncPaneToNative(object native, ScrollPane pane, bool horizontal)
    {
        if (_adapter == null || _syncingScrollableFromNative.Contains(native))
        {
            return;
        }

        var percent = horizontal ? pane.PercentX : pane.PercentY;
        var clamped = float.IsNaN(percent) || float.IsInfinity(percent)
            ? 0f
            : Math.Clamp(percent, 0f, 1f);

        _syncingScrollableToNative.Add(native);
        try
        {
            _adapter.SetScrollValue(native, clamped);
        }
        finally
        {
            _syncingScrollableToNative.Remove(native);
        }
    }

    /// <summary>可滚动 native 的一次拖拽状态：延迟决定归属（自身轴 or 转发外层）。</summary>
    private sealed class ScrollDragState
    {
        public ScrollDragState(float startX, float startY)
        {
            StartX = startX;
            StartY = startY;
        }

        public float StartX { get; }
        public float StartY { get; }
        public bool Decided { get; set; }
        public ScrollPane? ActivePane { get; set; }
        public object? ActiveNative { get; set; }
        public bool ActiveHorizontal { get; set; }

        /// <summary>子控件的桥：按下时找到的滚动祖先（列表自己的桥不用，为 null）。</summary>
        public GComponent? Owner { get; set; }
    }
}
#endif


