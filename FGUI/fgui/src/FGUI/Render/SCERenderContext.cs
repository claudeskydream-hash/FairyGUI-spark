#if CLIENT
using System.Drawing;
using System.IO;
using SCEFGUI.Core;
using SCEFGUI.UI;
using GameUI.Control.Primitive;

namespace SCEFGUI.Render;

public class SCERenderContext
{
    private static SCERenderContext? _instance;
    private ISCEAdapter? _adapter;
    private readonly Dictionary<object, object> _nativeParentOfChild = new();
    private readonly HashSet<object> _boundButtonNative = [];

    public static SCERenderContext Instance => _instance ??= new SCERenderContext();
    public ISCEAdapter? Adapter { get => _adapter; set => _adapter = value; }

    public void Initialize(ISCEAdapter adapter)
    {
        _adapter = adapter;
    }

    public object? CreateNativeControl(FGUIObject obj)
    {
        if (obj.NativeObject != null)
        {
            return obj.NativeObject;
        }

        if (_adapter == null) return null;
        object? native = obj switch
        {
            // Use Panel for FGUIButton to support child controls (title, icon)
            // SCE Button doesn't support child controls properly
            FGUIButton => _adapter.CreatePanel(),
            FGUITextInput => _adapter.CreateInput(),
            FGUITextField => _adapter.CreateLabel(),
            FGUIList => _adapter.CreateScrollablePanel(),
            FGUIGraph graph => CreateGraphControl(graph),
            FGUIImage image => CreateImageControl(image),
            FGUIComponent => _adapter.CreatePanel(),
            _ => _adapter.CreatePanel()
        };
        if (native != null)
        {
            obj.NativeObject = native;
            ApplyProperties(obj);
        }
        return native;
    }
    
    private object? CreateGraphControl(FGUIGraph graph)
    {
        if (_adapter == null) return null;
        // Graph uses Panel with background color
        var panel = _adapter.CreatePanel();
        if (panel != null && graph.Type != GraphType.Empty)
        {
            _adapter.SetBackgroundColor(panel, graph.FillColor);
            if (graph.Type == GraphType.Ellipse)
                _adapter.SetCornerRadius(panel, Math.Min(graph.Width, graph.Height) / 2);
        }
        return panel;
    }
    
    private object? CreateImageControl(FGUIImage image)
    {
        if (_adapter == null) return null;
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
            float scaleFactor = FGUIManager.ContentScaleFactor;
            width *= scaleFactor;
            height *= scaleFactor;
            
            // Create Canvas for atlas region rendering (both for normal sprites and nine-slice)
            return ((SCEAdapter)_adapter).CreateCanvas(width, height);
        }
        // Otherwise use Panel with background color
        return _adapter.CreatePanel();
    }

    public void ApplyProperties(FGUIObject obj)
    {
        if (_adapter == null || obj.NativeObject == null) return;
        var native = obj.NativeObject;
        
        // 获取内容缩放因子
        float scaleFactor = FGUIManager.ContentScaleFactor;
        
        // 如果是FGUIRoot，位置和尺寸由AddToRoot处理，这里只处理其他属性
        if (obj is FGUIRoot)
        {
            _adapter.SetVisible(native, obj.Visible);
            _adapter.SetOpacity(native, obj.Alpha);
            _adapter.SetTouchable(native, obj.Touchable);
            _adapter.SetGrayed(native, obj.Grayed);
        }
        else
        {
            // 对于子组件，应用缩放因子
            _adapter.SetPosition(native, obj.X * scaleFactor, obj.Y * scaleFactor);
            _adapter.SetSize(native, obj.Width * scaleFactor, obj.Height * scaleFactor);
            _adapter.SetVisible(native, obj.Visible);
            _adapter.SetOpacity(native, obj.Alpha);
            _adapter.SetRotation(native, obj.Rotation);
            _adapter.SetScale(native, obj.ScaleX, obj.ScaleY);
            _adapter.SetTouchable(native, obj.Touchable);
            _adapter.SetGrayed(native, obj.Grayed);
        }
        ApplyTypeSpecificProperties(obj);
    }

    private void ApplyTypeSpecificProperties(FGUIObject obj)
    {
        if (_adapter == null || obj.NativeObject == null) return;
        var native = obj.NativeObject;
        switch (obj)
        {
            case FGUIImage image: ApplyImageProperties(image, native); break;
            case FGUILoader loader: ApplyLoaderProperties(loader, native); break;
            case FGUITextField text: ApplyTextProperties(text, native); break;
            case FGUIButton button: ApplyButtonProperties(button, native); break;
            case FGUIComponent component: ApplyComponentProperties(component, native); break;
        }
    }

    private static void ApplyLoaderProperties(FGUILoader loader, object native)
    {
        loader.SyncNativeContent();
    }

    private void ApplyImageProperties(FGUIImage image, object native)
    {
        if (_adapter == null) return;
        if (image.Color != Color.White) _adapter.SetBackgroundColor(native, image.Color);
        if (image.PackageItem?.Sprite != null)
        {
            var sprite = image.PackageItem.Sprite;
            var atlas = sprite.Atlas;
            if (atlas?.File != null)
            {
                // Resolve atlas path while preserving sub-directory mapping when package path contains one.
                string sceImagePath = ResolveSceImagePath(atlas);
                
                // 获取缩放因子
                float scaleFactor = FGUIManager.ContentScaleFactor;
                
                if (image.PackageItem.Scale9Grid.HasValue)
                {
                    // Scale9Grid is (X, Y, Width, Height) of the center region
                    // Need to calculate Left, Right, Top, Bottom edges
                    var grid = image.PackageItem.Scale9Grid.Value;
                    // 九宫格边距也需要缩放
                    int left = (int)(grid.X * scaleFactor);
                    int top = (int)(grid.Y * scaleFactor);
                    int right = (int)((image.PackageItem.Width - grid.X - grid.Width) * scaleFactor);
                    int bottom = (int)((image.PackageItem.Height - grid.Y - grid.Height) * scaleFactor);
                    
                    // Use actual dimensions, fallback to PackageItem/Sprite if 0
                    float destWidth = image.Width > 0 ? image.Width : 
                                      (image.InitWidth > 0 ? image.InitWidth : 
                                      (image.PackageItem.Width > 0 ? image.PackageItem.Width : sprite.Rect.Width));
                    float destHeight = image.Height > 0 ? image.Height : 
                                       (image.InitHeight > 0 ? image.InitHeight : 
                                       (image.PackageItem.Height > 0 ? image.PackageItem.Height : sprite.Rect.Height));
                    
                    // 应用缩放因子到目标尺寸
                    destWidth *= scaleFactor;
                    destHeight *= scaleFactor;
                    
                    // Use sliced image with atlas region cropping
                    ((SCEAdapter)_adapter).SetSlicedImageFromAtlas(native, sceImagePath, sprite.Rect, 
                        left, right, top, bottom, destWidth, destHeight);
                }
                else 
                {
                    _adapter.SetImageRegion(native, sceImagePath, sprite.Rect, sprite.Rotated);
                }
            }
        }
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

    private void ApplyTextProperties(FGUITextField text, object native)
    {
        if (_adapter == null) return;
        _adapter.SetText(native, text.Text ?? "");
        _adapter.SetTextColor(native, text.Color);
        
        // 字体大小也需要缩放
        float scaleFactor = FGUIManager.ContentScaleFactor;
        int scaledFontSize = (int)(text.FontSize * scaleFactor);
        _adapter.SetFontSize(native, scaledFontSize);
        
        _adapter.SetBold(native, text.Bold);
        _adapter.SetItalic(native, text.Italic);
        var hAlign = text.Align switch { AlignType.Left => TextAlign.Left, AlignType.Center => TextAlign.Center, AlignType.Right => TextAlign.Right, _ => TextAlign.Left };
        _adapter.SetTextAlign(native, hAlign);
        var vAlign = text.VerticalAlign switch { VertAlignType.Top => TextVerticalAlign.Top, VertAlignType.Middle => TextVerticalAlign.Middle, VertAlignType.Bottom => TextVerticalAlign.Bottom, _ => TextVerticalAlign.Top };
        _adapter.SetTextVerticalAlign(native, vAlign);
    }

    private void ApplyButtonProperties(FGUIButton button, object native)
    {
        if (_adapter == null) return;
        ApplyComponentProperties(button, native);
        if (_boundButtonNative.Add(native))
        {
            _adapter.OnClick(native, () => button.DispatchEvent("onClick", null));
            _adapter.OnPointerEnter(native, () => button.DispatchEvent("onRollOver", null));
            _adapter.OnPointerLeave(native, () => button.DispatchEvent("onRollOut", null));
        }
    }

    private void ApplyComponentProperties(FGUIComponent component, object native)
    {
        if (_adapter == null) return;
        if (component.Overflow == OverflowType.Hidden) _adapter.SetClipContent(native, true);
        foreach (var child in component.Children) RenderChild(component, child);
    }

    public void RenderChild(FGUIComponent parent, FGUIObject child)
    {
        if (_adapter == null) return;

        if (!child.FinalVisible)
        {
            if (child.NativeObject != null)
            {
                RemoveFromParent(child);
            }

            return;
        }

        var childNative = CreateNativeControl(child);
        if (childNative == null) 
        {
            Game.Logger.LogWarning($"[FGUI] RenderChild: Failed to create native control for '{child.Name}' (type: {child.GetType().Name})");
            return;
        }
        if (parent.NativeObject != null) 
        {
            var parentNative = parent.NativeObject;
            if (_nativeParentOfChild.TryGetValue(childNative, out var attachedParent))
            {
                if (ReferenceEquals(attachedParent, parentNative))
                {
                    return;
                }
                _adapter.RemoveChild(attachedParent, childNative);
            }

            _adapter.AddChild(parentNative, childNative);
            _nativeParentOfChild[childNative] = parentNative;
            float scaleFactor = FGUIManager.ContentScaleFactor;
            Game.Logger.LogInformation($"[FGUI] RenderChild: Added '{child.Name}' ({child.GetType().Name}) to '{parent.Name}', text='{child.Text}', pos=({child.X * scaleFactor:F0},{child.Y * scaleFactor:F0}), size=({child.Width * scaleFactor:F0}x{child.Height * scaleFactor:F0}), scale={scaleFactor:F2}");
        }
        _adapter.SetZIndex(childNative, child.SortingOrder);
    }

    public void UpdatePosition(FGUIObject obj) 
    { 
        if (_adapter == null || obj.NativeObject == null) return;
        float scaleFactor = obj is FGUIRoot ? 1f : FGUIManager.ContentScaleFactor;
        _adapter.SetPosition(obj.NativeObject, obj.X * scaleFactor, obj.Y * scaleFactor); 
    }
    
    public void UpdateSize(FGUIObject obj) 
    { 
        if (_adapter == null || obj.NativeObject == null) return;
        float scaleFactor = obj is FGUIRoot ? 1f : FGUIManager.ContentScaleFactor;
        _adapter.SetSize(obj.NativeObject, obj.Width * scaleFactor, obj.Height * scaleFactor); 
    }
    
    public void UpdateVisibility(FGUIObject obj) { if (_adapter != null && obj.NativeObject != null) _adapter.SetVisible(obj.NativeObject, obj.Visible); }
    public void UpdateAlpha(FGUIObject obj) { if (_adapter != null && obj.NativeObject != null) _adapter.SetOpacity(obj.NativeObject, obj.Alpha); }

    public void AddToRoot(FGUIObject obj)
    {
        if (_adapter == null) return;
        
        // 创建原生控件（如果还没有）
        if (obj.NativeObject == null)
        {
            CreateNativeControl(obj);
        }
        
        if (obj.NativeObject != null)
        {
            // 判断是否是 FGUIRoot - 只有 FGUIRoot 才需要全屏拉伸
            bool isRoot = obj is FGUIRoot;
            
            if (isRoot)
            {
                // FGUIRoot 使用全屏模式
                _adapter.AddToRoot(obj.NativeObject);
                Game.Logger.LogInformation($"[FGUI] AddToRoot: Added FGUIRoot to stage with fullscreen mode");
            }
            else
            {
                // 其他组件使用固定尺寸添加到根节点
                ((SCEAdapter)_adapter).AddToRootWithFixedSize(obj.NativeObject, obj.Width, obj.Height);
                Game.Logger.LogInformation($"[FGUI] AddToRoot: Added '{obj.Name}' ({obj.GetType().Name}) to stage with size {obj.Width}x{obj.Height}");
            }
        }
    }

    public void RemoveFromParent(FGUIObject obj)
    {
        if (_adapter == null || obj.NativeObject == null) return;
        _adapter.RemoveFromParent(obj.NativeObject);
        ClearNativeAttachmentTracking(obj.NativeObject, clearChildren: false);
    }

    public void DisposeNative(FGUIObject obj)
    {
        if (_adapter == null || obj.NativeObject == null) return;
        var native = obj.NativeObject;
        _boundButtonNative.Remove(native);
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

}
#endif
