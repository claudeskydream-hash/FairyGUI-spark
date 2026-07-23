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
    private readonly HashSet<object> _boundButtonRelayNative = [];
    private readonly HashSet<object> _boundGenericClickNative = [];
    private readonly HashSet<object> _boundTouchNative = [];
    private readonly HashSet<object> _boundScrollableNative = [];
    private readonly HashSet<object> _boundScrollablePointerNative = [];
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

        // Non-ellipse graph keeps control path.
        var panel = _adapter.CreatePanel();
        if (panel != null && graph.Type != GraphType.Empty)
            _adapter.SetBackgroundColor(panel, graph.FillColor);
        return panel;
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
        var block = obj is GButton
            || obj is GTextInput
            || (obj is GComponent component && component.ScrollPane != null);
        _adapter.SetBlockPointerEvents(native, block);
    }

    public void EnsureTouchBinding(GObject obj)
    {
        if (_adapter == null || obj.NativeObject == null)
        {
            return;
        }

        BindTouchEvents(obj, obj.NativeObject);
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

    private void ApplyGraphProperties(GGraph graph, object native)
    {
        if (_adapter == null)
        {
            return;
        }

        if (graph.Type == GraphType.Ellipse)
        {
            _adapter.SetCanvasEllipse(native, graph.FillColor);
            return;
        }

        _adapter.ClearCanvasRenderState(native);
        _adapter.SetBackgroundColor(native, graph.FillColor);
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

        var relayTarget = ResolveTouchRelayTarget(obj);
        var relayButton = relayTarget as GButton;
        var comboInputPath = EnableComboInputDiagLogs && (obj is GComboBox || relayTarget is GComboBox);
        var shouldBind = ShouldBindTouchEvents(obj, relayTarget);
        var alreadyBound = _boundTouchNative.Contains(native);
        if (comboInputPath)
        {
            System.GC.KeepAlive(0);
        }

        if (!shouldBind || !_boundTouchNative.Add(native))
        {
            return;
        }

        if (comboInputPath)
        {
            System.GC.KeepAlive(0);
        }

        _adapter.OnPointerPressWithPosition(native, (x, y) =>
        {
            if (ShouldLogButtonDiag(obj))
            {
                System.GC.KeepAlive(0);
            }

            if (comboInputPath)
            {
                System.GC.KeepAlive(0);
            }

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
                if (relayButton != null && ShouldLogButtonDiag(relayButton))
                {
                    System.GC.KeepAlive(0);
                }

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

            var moveRecipient = relayTarget ?? obj;
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
            if (ShouldLogButtonDiag(obj))
            {
                System.GC.KeepAlive(0);
            }

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
                if (relayButton != null && ShouldLogButtonDiag(relayButton))
                {
                    System.GC.KeepAlive(0);
                }

                var relayContext = new EventContext
                {
                    Sender = relayTarget,
                    Type = "onTouchEnd"
                };
                relayTarget.DispatchEventWithContext("onTouchEnd", relayContext, null);
            }
        });

        if (relayTarget != null && _boundButtonRelayNative.Add(native))
        {
            _adapter.OnClick(native, () =>
            {
                if (relayButton != null && ShouldLogButtonDiag(relayButton))
                {
                    System.GC.KeepAlive(0);
                }
                else if (EnableComboInputDiagLogs && relayTarget is GComboBox)
                {
                    System.GC.KeepAlive(0);
                }

                relayTarget.DispatchEvent("onClick", null);
            });
            _adapter.OnPointerEnter(native, () => relayTarget.DispatchEvent("onRollOver", null));
            _adapter.OnPointerLeave(native, () => relayTarget.DispatchEvent("onRollOut", null));
        }
        else if (relayTarget == null && obj is not GButton && _boundGenericClickNative.Add(native))
        {
            _adapter.OnClick(native, () =>
            {
                if (EnableComboInputDiagLogs && obj is GComboBox)
                {
                    System.GC.KeepAlive(0);
                }

                obj.DispatchEvent("onClick", null);
            });
            _adapter.OnPointerEnter(native, () => obj.DispatchEvent("onRollOver", null));
            _adapter.OnPointerLeave(native, () => obj.DispatchEvent("onRollOut", null));
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

        return HasAnyTouchListener(obj);
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
            var useManualPointerBridge = isManualVirtualFallback || needsNativeScroll;
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
                    var pane = component.ScrollPane;
                    if (pane == null || !_scrollDragStates.TryGetValue(native, out var drag))
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
                        if (dragHorizontal == horizontal)
                        {
                            // 拖拽方向与本列表滚动轴一致：本列表处理
                            drag.ActivePane = pane;
                            drag.ActiveNative = native;
                            drag.ActiveHorizontal = horizontal;
                        }
                        else
                        {
                            // 垂直于本列表滚动轴：转发给方向匹配的滚动祖先（外层列表）
                            var ancestor = FindAncestorScrollable(component, dragHorizontal);
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
                        drag.ActivePane.OnTouchBegin(drag.StartX, drag.StartY);
                    }

                    if (drag.ActivePane == null || drag.ActiveNative == null)
                    {
                        return;
                    }

                    // OnTouchMove 内部会经 UpdateScrollPosition → SyncScrollPaneToNative 同步到原生，
                    // 这里不再重复推送，避免每帧双推放大抖动。
                    drag.ActivePane.OnTouchMove(x, y);
                });

                _adapter.OnPointerRelease(native, () =>
                {
                    _adapter.ReleasePointer(native);
                    if (_scrollDragStates.Remove(native, out var drag)
                        && drag.Decided
                        && drag.ActivePane != null
                        && drag.ActiveNative != null)
                    {
                        drag.ActivePane.OnTouchEnd();
                        SyncPaneToNative(drag.ActiveNative, drag.ActivePane, drag.ActiveHorizontal);
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
        _boundButtonRelayNative.Remove(native);
        _boundGenericClickNative.Remove(native);
        _boundTouchNative.Remove(native);
        _boundScrollableNative.Remove(native);
        _boundScrollablePointerNative.Remove(native);
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
    }
}
#endif


