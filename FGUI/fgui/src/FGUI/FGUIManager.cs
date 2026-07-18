#if CLIENT
using FairyGUI;
using FairyGUI.Render;

namespace FairyGUI;

/// <summary>
/// 屏幕适配模式
/// </summary>
public enum ScreenMatchMode
{
    /// <summary>
    /// 取宽度和高度缩放比的较小值，保证UI完全显示在屏幕内
    /// </summary>
    MatchWidthOrHeight,
    /// <summary>
    /// 按宽度缩放
    /// </summary>
    MatchWidth,
    /// <summary>
    /// 按高度缩放
    /// </summary>
    MatchHeight,
    /// <summary>
    /// 铺满屏幕（Cover）：取宽高缩放比的较大值，可能裁切一侧
    /// </summary>
    Fill
}

public enum ImageAssetMode
{
    Atlas,
    ScatterPreferred,
    ScatterOnly
}

public enum SemanticRootMode
{
    SceStageDirect
}

public static class UIRuntime
{
    private static bool _initialized;
    private static bool _controlOnlyImageMode;
    private static ImageAssetMode _imageAssetMode = ImageAssetMode.ScatterOnly;
    private static SemanticRootMode _semanticRootMode = SemanticRootMode.SceStageDirect;
    private static readonly HashSet<GObject> _sceStageObjects = [];
    private static readonly HashSet<FGUIRoot> _fullScreenRoots = [];
    private static readonly Dictionary<GObject, FGUIRoot> _fullScreenRootByContent = [];
    private static bool _screenSizeListenerBound;

    /// <summary>
    /// 根视口尺寸变化时触发。全屏遮罩等非 FGUIRoot 对象可订阅此事件刷新自身尺寸。
    /// </summary>
    public static event Action<float, float>? RootSizeChanged;
    
    /// <summary>
    /// 设计分辨率宽度
    /// </summary>
    public static float DesignResolutionX { get; private set; } = 1136;
    
    /// <summary>
    /// 设计分辨率高度
    /// </summary>
    public static float DesignResolutionY { get; private set; } = 640;
    
    /// <summary>
    /// 屏幕适配模式
    /// </summary>
    public static ScreenMatchMode MatchMode { get; private set; } = ScreenMatchMode.MatchWidthOrHeight;
    
    /// <summary>
    /// 内容缩放因子（实际屏幕尺寸 / 设计分辨率）
    /// </summary>
    public static float ContentScaleFactor { get; private set; } = 1f;

    /// <summary>
    /// FGUI 图片渲染是否强制走 Control 链路（不走 Canvas）。
    /// </summary>
    public static bool ControlOnlyImageMode =>
        _controlOnlyImageMode || _imageAssetMode != ImageAssetMode.Atlas;

    public static ImageAssetMode CurrentImageAssetMode => _imageAssetMode;
    public static SemanticRootMode CurrentSemanticRootMode => _semanticRootMode;
    public static bool IsSceStageDirectMode => true;

    public static void Initialize(float designWidth = 1136, float designHeight = 640)
    {
        if (_initialized) return;
        DesignResolutionX = designWidth;
        DesignResolutionY = designHeight;
        EnsureScreenSizeListener();
        UpdateContentScaleFactor();
        _sceStageObjects.Clear();
        _fullScreenRoots.Clear();
        _fullScreenRootByContent.Clear();
        _initialized = true;
    }

    public static void Initialize(ISCEAdapter adapter, float designWidth = 1136, float designHeight = 640)
    {
        SCERenderContext.Instance.Initialize(adapter);
        Initialize(designWidth, designHeight);
    }

    public static void SetControlOnlyImageMode(bool enabled)
    {
        _controlOnlyImageMode = enabled;
        Game.Logger.LogInformation("[FGUI] ControlOnlyImageMode set to {Enabled}", enabled);
    }

    public static void SetImageAssetMode(ImageAssetMode mode)
    {
        _imageAssetMode = mode;
        Game.Logger.LogInformation("[FGUI] ImageAssetMode set to {Mode}", mode);
    }

    public static void SetSemanticRootMode(SemanticRootMode mode)
    {
        _semanticRootMode = SemanticRootMode.SceStageDirect;
        _sceStageObjects.Clear();
        Game.Logger.LogWarning("[FGUI][ROOT-MODE] mode fixed to SceStageDirect; ignored request={Mode}", mode);
    }
    
    /// <summary>
    /// 设置内容缩放因子（设计分辨率适配）
    /// </summary>
    /// <param name="designResolutionX">设计分辨率宽度</param>
    /// <param name="designResolutionY">设计分辨率高度</param>
    /// <param name="matchMode">屏幕适配模式</param>
    public static void SetContentScaleFactor(float designResolutionX, float designResolutionY, 
        ScreenMatchMode matchMode = ScreenMatchMode.MatchWidthOrHeight)
    {
        DesignResolutionX = designResolutionX;
        DesignResolutionY = designResolutionY;
        MatchMode = matchMode;
        UpdateContentScaleFactor();
    }
    
    /// <summary>
    /// 更新内容缩放因子
    /// </summary>
    internal static void UpdateContentScaleFactor()
    {
        var screenSize = SCERenderContext.Instance.Adapter?.GetScreenSize();
        if (!screenSize.HasValue || screenSize.Value.Width <= 0 || screenSize.Value.Height <= 0)
        {
            ContentScaleFactor = 1f;
            return;
        }
        
        float screenWidth = screenSize.Value.Width;
        float screenHeight = screenSize.Value.Height;
        
        if (DesignResolutionX <= 0 || DesignResolutionY <= 0)
        {
            ContentScaleFactor = 1f;
            return;
        }
        
        float s1 = screenWidth / DesignResolutionX;
        float s2 = screenHeight / DesignResolutionY;
        
        var computedScale = MatchMode switch
        {
            ScreenMatchMode.MatchWidth => s1,
            ScreenMatchMode.MatchHeight => s2,
            ScreenMatchMode.Fill => Math.Max(s1, s2),
            _ => Math.Min(s1, s2) // MatchWidthOrHeight
        };
        
        // 防止缩放因子过大
        if (computedScale > 10)
            computedScale = 10;

        // Stage-direct semantic: disable global per-control scaling.
        // Screen adaptation should come from layout relations (for example Width/Height),
        // not from multiplying every control's size/position by a global factor.
        ContentScaleFactor = 1f;

        Game.Logger.LogInformation(
            "[FGUI] ContentScaleFactor fixed to 1.000 (computed={ComputedScale:F3}, match={MatchMode}, screen={ScreenWidth}x{ScreenHeight}, design={DesignWidth}x{DesignHeight})",
            computedScale,
            MatchMode,
            screenWidth,
            screenHeight,
            DesignResolutionX,
            DesignResolutionY);
    }

    public static ISCEAdapter? Adapter
    {
        get => SCERenderContext.Instance.Adapter;
        set => SCERenderContext.Instance.Adapter = value;
    }

    public static UIPackage? AddPackage(string filePath, Func<string, string, byte[]?> loadFunc) =>
        UIPackage.AddPackage(filePath, (name, ext) => loadFunc(name, ext));

    public static UIPackage? AddPackage(byte[] data, string assetPrefix, Func<string, string, byte[]?> loadFunc) =>
        UIPackage.AddPackage(data, assetPrefix, (name, ext) => loadFunc(name, ext));

    public static UIPackage? GetPackage(string name) => UIPackage.GetByName(name);

    public static GObject? CreateObject(string packageName, string componentName) =>
        UIPackage.GetByName(packageName)?.CreateObject(componentName);

    public static GObject? CreateObject(string url)
    {
        // If it's a URL format, parse it
        if (url.StartsWith(UIPackage.URL_PREFIX))
        {
            var item = UIPackage.GetItemByURL(url);
            return item?.Owner?.CreateObject(item);
        }
        // Otherwise try to find package/component by name
        return null;
    }

    public static GObject? CreateObjectFromURL(string url) =>
        UIPackage.GetItemByURL(url)?.Owner?.CreateObject(UIPackage.GetItemByURL(url)!);

    public static void RemovePackage(string packageIdOrName) => UIPackage.RemovePackage(packageIdOrName);
    public static void RemoveAllPackages() => UIPackage.RemoveAllPackages();
    public static float RootWidth
    {
        get
        {
            var rootSize = GetLogicalRootSize();
            return rootSize.Width;
        }
    }

    public static float RootHeight
    {
        get
        {
            var rootSize = GetLogicalRootSize();
            return rootSize.Height;
        }
    }

    public static void AddToRoot(GObject obj)
    {
        if (obj == null)
        {
            return;
        }

        if (obj.Parent != null)
        {
            obj.RemoveFromParent();
        }

        PrepareRootForStage(obj);
        obj.AddToStage();
        _sceStageObjects.Add(obj);
    }

    /// <summary>
    /// 将页面组件保留为原具体类型，并由全屏根节点承载。不要用于窗口、Popup 或拖拽代理。
    /// </summary>
    public static FGUIRoot AddToFullScreenRoot(GComponent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (_fullScreenRootByContent.TryGetValue(content, out var existingRoot) && !existingRoot.Disposed)
        {
            return existingRoot;
        }

        if (content.Parent != null)
        {
            content.RemoveFromParent();
        }

        var root = new FGUIRoot();
        root.SetContent(content);
        _fullScreenRootByContent[content] = root;
        AddToRoot(root);
        return root;
    }

    public static bool IsFullScreenContent(GObject obj) => _fullScreenRootByContent.ContainsKey(obj);

    public static void RemoveFromRoot(GObject obj, bool dispose = false)
    {
        if (obj == null)
        {
            return;
        }

        if (_fullScreenRootByContent.TryGetValue(obj, out var contentRoot))
        {
            _fullScreenRootByContent.Remove(obj);
            if (obj.Parent == contentRoot)
            {
                contentRoot.RemoveChild(obj, dispose: false);
            }

            RemoveFromRoot(contentRoot, dispose: true);
            if (dispose && !obj.Disposed)
            {
                obj.Dispose();
            }
            return;
        }

        _sceStageObjects.Remove(obj);
        if (obj is FGUIRoot fullScreenRoot)
        {
            UnregisterFullScreenRoot(fullScreenRoot);
        }
        if (obj.NativeObject != null)
        {
            SCERenderContext.Instance.RemoveFromParent(obj);
        }

        if (dispose && !obj.Disposed)
        {
            obj.Dispose();
        }
    }

    public static List<GObject> GetTopLevelObjectsSnapshot()
    {
        var snapshot = new List<GObject>(_sceStageObjects.Count);
        foreach (var obj in _sceStageObjects)
        {
            if (obj.Disposed)
            {
                continue;
            }

            snapshot.Add(obj);
        }

        return snapshot;
    }
    
    /// <summary>
    /// Get item URL from package
    /// </summary>
    public static string? GetItemURL(string packageName, string itemName)
    {
        var pkg = UIPackage.GetByName(packageName);
        if (pkg == null) return null;
        var item = pkg.GetItemByName(itemName);
        if (item == null) return null;
        return UIPackage.URL_PREFIX + pkg.Id + item.Id;
    }

    private static (float Width, float Height) GetLogicalRootSize()
    {
        var screenSize = SCERenderContext.Instance.Adapter?.GetScreenSize();
        var scale = MathF.Max(0.0001f, ContentScaleFactor);
        if (screenSize.HasValue && screenSize.Value.Width > 0f && screenSize.Value.Height > 0f)
        {
            return (screenSize.Value.Width / scale, screenSize.Value.Height / scale);
        }

        return (MathF.Max(1f, DesignResolutionX), MathF.Max(1f, DesignResolutionY));
    }

    internal static void PrepareRootForStage(GObject obj)
    {
        if (obj is not FGUIRoot fullScreenRoot)
        {
            return;
        }

        var rootSize = GetLogicalRootSize();
        fullScreenRoot.ApplyViewportSize(rootSize.Width, rootSize.Height);
        _fullScreenRoots.Add(fullScreenRoot);
    }

    internal static void UnregisterFullScreenRoot(FGUIRoot root)
    {
        _fullScreenRoots.Remove(root);

        List<GObject>? hostedContent = null;
        foreach (var pair in _fullScreenRootByContent)
        {
            if (ReferenceEquals(pair.Value, root))
            {
                hostedContent ??= [];
                hostedContent.Add(pair.Key);
            }
        }

        if (hostedContent != null)
        {
            foreach (var content in hostedContent)
            {
                _fullScreenRootByContent.Remove(content);
            }
        }
    }

    private static void EnsureScreenSizeListener()
    {
        if (_screenSizeListenerBound)
        {
            return;
        }

        GameUI.Device.ScreenViewport.Primary.OnSizeChanged += OnScreenSizeChanged;
        _screenSizeListenerBound = true;
    }

    private static void OnScreenSizeChanged(int _, int __)
    {
        UpdateContentScaleFactor();
        var rootSize = GetLogicalRootSize();

        List<FGUIRoot>? staleRoots = null;
        foreach (var root in _fullScreenRoots)
        {
            if (root.Disposed)
            {
                staleRoots ??= [];
                staleRoots.Add(root);
                continue;
            }

            root.ApplyViewportSize(rootSize.Width, rootSize.Height);
        }

        if (staleRoots != null)
        {
            foreach (var root in staleRoots)
            {
                _fullScreenRoots.Remove(root);
            }
        }

        RootSizeChanged?.Invoke(rootSize.Width, rootSize.Height);
    }
}
#endif

