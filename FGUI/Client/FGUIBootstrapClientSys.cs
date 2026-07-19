#if CLIENT
using FairyGUI;
using FairyGUI.Render;

namespace GameEntry;

public sealed class FGUIBootstrapClientSys : IGameClass
{
    private static bool _initialized;
    private const float LandscapeDesignWidth = 1136f;
    private const float LandscapeDesignHeight = 640f;
    private const float PortraitDesignWidth = 640f;
    private const float PortraitDesignHeight = 1136f;

    public static void OnRegisterGameClass()
    {
        Game.OnGameTriggerInitialization += OnGameTriggerInitialization;
    }

    private static void OnGameTriggerInitialization()
    {
        if (Game.GameModeLink != ScopeData.GameDataGameMode.MapGameMode)
        {
            return;
        }

        new Trigger<EventGameStart>(OnGameStartAsync, keepReference: true).Register(Game.Instance);
        new Trigger<EventGameTick>(OnGameTickAsync, keepReference: true).Register(Game.Instance);
    }

    private static Task<bool> OnGameStartAsync(object sender, EventGameStart eventArgs)
    {
        EnsureInitialized();
        // 演示：加载并显示 Basics 包的 Main 组件（换基座后的连通性验证，可后续移除）
        var main = LoadAndShow("ui/MainAssetPackage", "MainAssetPackage", "MainLayer");
        // 填充 RwdList(外层)→ 每行的 itemList(内层)→ ItemCell，并接上每行的加按钮
        SetupRwdList(main);
        // btnAdd 点击 → 循环切换控制器 btnControl 的页
        SetupBtnControl(main);
        // 独立的 TTF 字体测试：try/catch 包裹，字体有问题也不影响主界面
        // TryShowFontTest(main);
        return Task.FromResult(true);
    }

    /// <summary>
    /// 每个外层行(RwdList item = GoodCell)当前拥有的内层项(itemList item = ItemCell)数量。
    /// 作为演示数据模型：索引 = 行号，值 = 该行内层项个数。加按钮点击时自增。
    /// </summary>
    private static readonly List<int> _rowItemCounts = new() { 6, 8, 5, 9, 4, 7, 6, 10, 5, 8, 7, 6 };

    /// <summary>
    /// 已经绑定过点击事件的加按钮，避免 item 复用时重复挂载 handler。
    /// </summary>
    private static readonly HashSet<GObject> _wiredAddButtons = new();

    /// <summary>ItemCell 图标资源 url（id 形式：ui://包id+资源id，hjw0r = a4_png）。</summary>
    private const string ItemCellIconUrl = "ui://h0gundzihjw0r";

    /// <summary>数量随机数生成器。</summary>
    private static readonly Random _random = new();

    /// <summary>是否已经打印过 ItemCell 的子对象结构（只打一次，便于核对字段名）。</summary>
    private static bool _itemCellDumped;

    /// <summary>是否已经打印过 ItemCell 图标诊断（只打一次）。</summary>
    private static bool _itemCellIconLogged;

    /// <summary>btnAdd 上次点击时刻（毫秒），用于去抖 SCE 的重复点击回调。</summary>
    private static long _lastBtnAddClickTick;


    /// <summary>
    /// 填充 MainLayer 上的嵌套列表：
    /// RwdList(外层) → 每行 GoodCell 里的 itemList(内层) → ItemCell。
    /// 关键点：内层 itemList 的赋值必须放在外层 RwdList 的 ItemRenderer 里，
    /// 因为只有渲染到某一行时才拿得到那一行的 itemList 实例。
    /// </summary>
    private static void SetupRwdList(GComponent? main)
    {
        if (main == null)
        {
            return;
        }

        try
        {
            if (main.GetChild("RwdList") is not GList rwdList)
            {
                Game.Logger.LogWarning("[FGUI] 未找到 RwdList 或其类型不是 GList，跳过嵌套列表填充");
                return;
            }

            // rwdList.SetVirtual();
            // 外层：每个 item 是 GoodCell，内部含 title(行标题) / itemList(内层列表) / btn(加按钮)
            rwdList.ItemRenderer = (rowIndex, rowObj) =>
            {
                if (rowObj is not GComponent row)
                {
                    return;
                }

                // 行标题
                if (row.GetChild("title") is GTextField rowTitle)
                {
                    rowTitle.Text = $"分组 {rowIndex + 1}";
                }

                // 内层 itemList：给它设 ItemRenderer + NumItems 完成赋值
                if (row.GetChild("itemList") is GList inner)
                {
                    // 捕获当前 rowIndex；外层每次渲染这一行都会重设，故索引始终正确
                    inner.ItemRenderer = (cellIndex, cellObj) =>
                    {
                        if (cellObj is GComponent cell)
                        {
                            FillItemCell(cell, rowIndex, cellIndex);
                        }
                    };

                    int count = (rowIndex >= 0 && rowIndex < _rowItemCounts.Count) ? _rowItemCounts[rowIndex] : 0;
                    inner.NumItems = count;

                    // 加按钮：给这一行的内层列表追加一个 ItemCell。
                    // 用 HashSet 保证同一个物理按钮只绑定一次（item 会被复用）。
                    if (row.GetChild("btn") is GButton addBtn && _wiredAddButtons.Add(addBtn))
                    {
                        addBtn.OnClick.Add(_ =>
                        {
                            // 点击时按物理行对象反查当前逻辑行号（非虚拟列表：子索引==行号）
                            int r = rwdList.GetChildIndex(row);
                            if (r < 0 || r >= _rowItemCounts.Count)
                            {
                                return;
                            }

                            _rowItemCounts[r]++;
                            inner.NumItems = _rowItemCounts[r];
                        });
                    }
                }
            };

            rwdList.NumItems = _rowItemCounts.Count;

            Game.Logger.LogInformation(
                "[FGUI] RwdList 填充完成 Rows={Rows} W={W} H={H} Layout={L} defaultItem={DefaultItem}",
                rwdList.NumItems, rwdList.Width, rwdList.Height, rwdList.Layout, rwdList.DefaultItem ?? "(空)");

            // 诊断：确认外层/内层列表是否有 ScrollPane 且内容溢出（决定能否拖动滚动）
            LogScrollDiag("RwdList", rwdList);
            if (rwdList.NumChildren > 0 && rwdList.GetChildAt(0) is GComponent firstRow)
            {
                if (firstRow.GetChild("itemList") is GList firstInner)
                {
                    LogScrollDiag("itemList[0]", firstInner);
                }
                else
                {
                    Game.Logger.LogInformation("[FGUI][Scroll] itemList[0] 未找到内层 GList（row0 子对象里没有名为 itemList 的 GList）");
                }
            }
        }
        catch (Exception ex)
        {
            Game.Logger.LogWarning(ex, "[FGUI] RwdList 嵌套列表填充失败（不影响主界面）");
        }
    }

    /// <summary>
    /// 给 MainLayer 上的 btnAdd 按钮加点击事件：每次点击把控制器 btnControl 切到下一页（循环）。
    /// </summary>
    private static void SetupBtnControl(GComponent? main)
    {
        if (main == null)
        {
            return;
        }

        try
        {
            var controller = main.GetController("btnControl");
            if (main.GetChild("btnAdd") is not GButton btnAdd || controller == null)
            {
                Game.Logger.LogWarning(
                    "[FGUI] 未找到 btnAdd(GButton) 或控制器 btnControl，跳过绑定 btnAdd={HasBtn} btnControl={HasCtrl}",
                    main.GetChild("btnAdd") != null, controller != null);
                return;
            }

            btnAdd.OnClick.Add(_ =>
            {
                if (controller.PageCount <= 0)
                {
                    return;
                }

                // 去抖：SCE 指针路由会对一次物理点击发出两次回调。
                // 不拦截的话，2 页会被切两次正好切回原页，xiaohuangren 看着"开了又关"没变化。
                var nowTick = Environment.TickCount64;
                // 两条 SCE 指针回调可能同时到达。普通的“读取→判断→写入”会让
                // 两条回调同时穿过判断，最终把两页控制器切回原值；使用原子交换，
                // 让同一次物理点击只有第一条回调可以继续执行。
                var previousTick = System.Threading.Interlocked.Exchange(ref _lastBtnAddClickTick, nowTick);
                if (nowTick - previousTick >= 0 && nowTick - previousTick < 200)
                {
                    return;
                }

                // 循环切到下一页：show ↔ hide，即 xiaohuangren 关一次开一次
                controller.SelectedIndex = (controller.SelectedIndex + 1) % controller.PageCount;
                Game.Logger.LogInformation(
                    "[FGUI] btnAdd 点击 → btnControl 切到 index={Index} page={Page}",
                    controller.SelectedIndex, controller.SelectedPage);
            });

            Game.Logger.LogInformation("[FGUI] btnAdd 已绑定，btnControl 页数={PageCount}", controller.PageCount);
        }
        catch (Exception ex)
        {
            Game.Logger.LogWarning(ex, "[FGUI] btnAdd/btnControl 绑定失败（不影响主界面）");
        }
    }

    /// <summary>
    /// 给内层 ItemCell 赋值：设标题文本 + 图标图片。
    /// ItemCell 内部子对象命名以 FGUI 编辑器为准，这里做防御式查找：
    /// 文本优先取名为 "title" 的 GTextField；图标优先取名为 "icon" 的 GLoader，
    /// 找不到就扫描第一个 GLoader/GTextField，保证换名后仍能赋值。
    /// </summary>
    private static void FillItemCell(GComponent cell, int rowIndex, int cellIndex)
    {
        // 首次渲染时打印一次真实结构，便于核对字段名与类型
        DumpItemCellStructureOnce(cell);

        // 数量：随机数写进 count 文本（ItemCell 里的数量字段名为 "count"，默认 x99）
        int amount = _random.Next(1, 100);
        var countField = cell.GetChild("count") as GTextField ?? FindChild<GTextField>(cell);
        if (countField != null)
        {
            countField.Text = $"x{amount}";
        }

        // 图标：固定资源 ui://h0gundzihjw0r
        var loader = cell.GetChild("icon") as GLoader ?? FindChild<GLoader>(cell);
        if (loader != null)
        {
            loader.Url = ItemCellIconUrl;
            // 强制把 loader 内容同步到 native：ItemRenderer 里赋值时 native 可能已 apply 过一次空 url，
            // 直接再同步一次可避免图标不上屏（内容已由 Url setter 创建）。
            loader.SyncNativeContent();

            if (!_itemCellIconLogged)
            {
                _itemCellIconLogged = true;
                var item = UIPackage.GetItemByURL(loader.Url);
                var content = loader.Content;
                Game.Logger.LogInformation(
                    "[FGUI] ItemCell icon 诊断 Url={Url} 解析到Item={HasItem} Type={Type} ItemWH={IW}x{IH} Sprite={HasSprite} Atlas={HasAtlas} | Loader WH={LW}x{LH} Native={LNative} | Content={CType} 源WH={CW}x{CH} ContentNative={CNative}",
                    loader.Url,
                    item != null, item?.Type.ToString() ?? "(空)", item?.Width ?? -1, item?.Height ?? -1,
                    item?.Sprite != null, item?.Sprite?.Atlas != null,
                    loader.Width, loader.Height, loader.NativeObject != null,
                    content?.GetType().Name ?? "(空)", content?.SourceWidth ?? -1f, content?.SourceHeight ?? -1f,
                    content?.NativeObject != null);
            }
        }
    }

    /// <summary>打印列表滚动诊断：是否有 ScrollPane、内容/视口尺寸、是否溢出（可拖动的前提）。</summary>
    private static void LogScrollDiag(string tag, GList list)
    {
        var pane = list.ScrollPane;
        if (pane == null)
        {
            Game.Logger.LogInformation(
                "[FGUI][Scroll] {Tag} 无 ScrollPane（编辑器里该列表 overflow 需设为 Scroll 才能拖动）", tag);
            return;
        }

        var overflowX = MathF.Max(0f, pane.ContentWidth - pane.ViewWidth);
        var overflowY = MathF.Max(0f, pane.ContentHeight - pane.ViewHeight);
        Game.Logger.LogInformation(
            "[FGUI][Scroll] {Tag} ScrollType={Type} Content={CW}x{CH} View={VW}x{VH} 溢出X={OX} 溢出Y={OY} 可滚动={Scrollable}",
            tag, pane.ScrollType, pane.ContentWidth, pane.ContentHeight, pane.ViewWidth, pane.ViewHeight,
            overflowX, overflowY, overflowX > 0.01f || overflowY > 0.01f);
    }

    /// <summary>在组件的直接子对象里找第一个指定类型的对象。</summary>
    private static T? FindChild<T>(GComponent parent) where T : GObject
    {
        for (int i = 0; i < parent.NumChildren; i++)
        {
            if (parent.GetChildAt(i) is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>把 ItemCell 的直接子对象（名字/运行时类型/文本）打印一次，用于确认可赋值字段。</summary>
    private static void DumpItemCellStructureOnce(GComponent cell)
    {
        if (_itemCellDumped)
        {
            return;
        }

        _itemCellDumped = true;
        Game.Logger.LogInformation("[FGUI] ItemCell 子对象数量={Count}", cell.NumChildren);
        for (int i = 0; i < cell.NumChildren; i++)
        {
            var child = cell.GetChildAt(i);
            var childText = child is GTextField tf ? tf.Text : "(非文本)";
            Game.Logger.LogInformation(
                "[FGUI]   [{Index}] Name={Name} Type={Type} Text={Text}",
                i, child.Name ?? "(空)", child.GetType().Name, childText);
        }
    }

    /// <summary>
    /// TTF 字体测试：同屏展示三种字体，验证多 family 加载与切换。
    /// 字体三步：res/font/&lt;family&gt;/x.ttf + ref/fontref.txt 注册 font/&lt;family&gt; + 代码 Font="font/&lt;family&gt;"
    /// </summary>
    private static void TryShowFontTest(GComponent? main)
    {
        if (main == null)
        {
            return;
        }

        try
        {
            // AddFontSample(main, "font/dongfangdakai", "阿里妈妈东方大楷  你好世界  ABC 123", System.Drawing.Color.White, 60f);
        }
        catch (Exception ex)
        {
            Game.Logger.LogWarning(ex, "[FGUI] 字体测试失败（不影响主界面）");
        }
    }

    /// <summary>在主界面加一行指定字体的文字。</summary>
    private static void AddFontSample(GComponent parent, string fontFamily, string text, System.Drawing.Color color, float y)
    {
        var label = new GTextField
        {
            Name = "ttf_" + fontFamily.Replace('/', '_'),
            Font = fontFamily,
            FontSize = 48,
            Color = color,
            Align = AlignType.Left,
            VerticalAlign = VertAlignType.Middle,
            Text = text,
        };
        label.SetSize(1000f, 80f);
        label.SetXY(80f, y);
        parent.AddChild(label);
    }

    private static Task<bool> OnGameTickAsync(object sender, EventGameTick eventArgs)
    {
        if (!_initialized)
        {
            return Task.FromResult(true);
        }

        var deltaSeconds = Math.Max(0, eventArgs.RealTimeDeltaInMilliseconds) / 1000f;
        SCERenderContext.Instance.Tick(deltaSeconds);
        return Task.FromResult(true);
    }

    public static void EnsureInitialized(float designWidth = 0, float designHeight = 0)
    {
        if (_initialized)
        {
            return;
        }

        var adapter = new SCEAdapter();
        if (designWidth <= 0 || designHeight <= 0)
        {
            var screen = adapter.GetScreenSize();
            var isLandscape = screen.Width >= screen.Height;
            designWidth = isLandscape ? LandscapeDesignWidth : PortraitDesignWidth;
            designHeight = isLandscape ? LandscapeDesignHeight : PortraitDesignHeight;
        }

        UIRuntime.Initialize(adapter, designWidth, designHeight);
        UIRuntime.SetContentScaleFactor(designWidth, designHeight, ScreenMatchMode.Fill);
        UIRuntime.SetImageAssetMode(ImageAssetMode.Atlas); // 本项目用图集导出（FGUIProject 二进制+atlas）
        _initialized = true;
        SetRootInputEnabled(false, "bootstrap-init");
        Game.Logger.LogInformation(
            "[FGUI] Runtime initialized. design={DesignWidth}x{DesignHeight} match={MatchMode}",
            designWidth,
            designHeight,
            ScreenMatchMode.Fill);
    }

    public static void SetRootInputEnabled(bool enabled, string reason = "")
    {
        if (!_initialized)
        {
            return;
        }

        Game.Logger.LogWarning("[FGUI][ROOT-INPUT] bypass mode=sce-stage-direct enabled={Enabled} reason={Reason}", enabled, reason);
    }

    public static GComponent? LoadAndShow(string packagePath, string packageName, string componentName)
    {
        EnsureInitialized();
        SetRootInputEnabled(true, $"load:{packageName}/{componentName}");

        var pkg = EnsurePackageLoaded(packagePath, packageName);

        if (pkg == null)
        {
            Game.Logger.LogWarning("[FGUI] Failed to load package: {PackagePath}", packagePath);
            return null;
        }

        TryApplyDesignResolutionFromPackage(pkg);

        var view = CreateComponentWithFallback(pkg, packageName, componentName);
        if (view == null)
        {
            Game.Logger.LogWarning("[FGUI] Failed to create component: {Package}/{Component}", packageName, componentName);
            return null;
        }

        UIRuntime.AddToFullScreenRoot(view);
        Game.Logger.LogInformation("[FGUI] Show component: {Package}/{Component}", packageName, componentName);
        return view;
    }

    internal static UIPackage? EnsurePackageLoaded(string packagePath, string packageName)
    {
        var pkg = UIRuntime.GetPackage(packageName);
        if (pkg != null)
        {
            return pkg;
        }

        foreach (var candidate in EnumeratePackagePathCandidates(packagePath, packageName))
        {
            var trimmed = candidate.TrimEnd('/');
            var attempts = new (string DescName, string RuntimePath)[]
            {
                ($"{trimmed}_fui", trimmed),
                ($"{trimmed}/{packageName}_fui", $"{trimmed}/{packageName}"),
            };

            foreach (var attempt in attempts)
            {
                var descData = FGUIResourceLoader.LoadBytes(attempt.DescName, ".bytes");
                if (descData == null)
                {
                    continue;
                }

                try
                {
                    pkg = UIRuntime.AddPackage(descData, attempt.RuntimePath, FGUIResourceLoader.LoadBytes);
                    if (pkg != null)
                    {
                        return pkg;
                    }
                }
                catch (Exception ex)
                {
                    Game.Logger.LogWarning(
                        ex,
                        "[FGUI] AddPackage(data) failed. packagePath={PackagePath}, descName={DescName}",
                        attempt.RuntimePath,
                        attempt.DescName);
                }
            }
        }

        return null;
    }

    internal static GComponent? CreateComponentWithFallback(UIPackage pkg, string packageName, string componentName)
    {
        var direct = UIRuntime.CreateObject(packageName, componentName) as GComponent;
        if (direct != null)
        {
            return direct;
        }

        // FairyGUI exports may prepend folder names (for example "folder/BagWin").
        foreach (var item in pkg.GetItems())
        {
            if (item.Type != PackageItemType.Component || string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            if (!item.Name.EndsWith(componentName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fallback = pkg.CreateObject(item) as GComponent;
            if (fallback == null)
            {
                continue;
            }

            Game.Logger.LogInformation("[FGUI] Component fallback matched: requested={Requested}, actual={Actual}",
                componentName, item.Name);
            return fallback;
        }

        return null;
    }

    private static IReadOnlyList<string> EnumeratePackagePathCandidates(string packagePath, string packageName)
    {
        var normalized = (packagePath ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<string>();
        }

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>(12);
        static string ToScatterPath(string path)
        {
            var value = path.Replace('\\', '/').TrimEnd('/');
            const string marker = "ui/image/fgui/";
            var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return value;
            }

            var markerEnd = index + marker.Length;
            var start = value.Substring(0, markerEnd);
            var tail = value.Substring(markerEnd).TrimStart('/');
            if (tail.StartsWith("scatter/", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            return $"{start}scatter/{tail}";
        }

        void YieldPath(string path)
        {
            var value = path.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (yielded.Add(value))
            {
                paths.Add(value);
            }
        }
        var scatterPath = ToScatterPath(normalized);
        YieldPath(scatterPath);
        YieldPath($"{scatterPath}/{packageName}");
        YieldPath(normalized);
        YieldPath($"{normalized}/{packageName}");

        var suffix = "/" + packageName;
        if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            var basePath = normalized.Substring(0, normalized.Length - suffix.Length);
            YieldPath(ToScatterPath(basePath));
            YieldPath(basePath);
        }

        return paths;
    }

    private static void TryApplyDesignResolutionFromPackage(UIPackage pkg)
    {
        if (!pkg.TryGetDesignResolution(out var designWidth, out var designHeight))
        {
            return;
        }

        if (designWidth <= 0f || designHeight <= 0f)
        {
            return;
        }

        var changed = MathF.Abs(UIRuntime.DesignResolutionX - designWidth) > 0.01f
            || MathF.Abs(UIRuntime.DesignResolutionY - designHeight) > 0.01f;
        if (!changed)
        {
            return;
        }

        UIRuntime.SetContentScaleFactor(designWidth, designHeight, ScreenMatchMode.Fill);
        Game.Logger.LogInformation(
            "[FGUI] Design resolution from package applied: {DesignWidth}x{DesignHeight} package={Package}",
            designWidth,
            designHeight,
            pkg.Name);
    }

    internal static void ApplyRootSizedLayout(GComponent view, string packageName, string componentName)
    {
        var rootWidth = MathF.Max(1f, UIRuntime.RootWidth);
        var rootHeight = MathF.Max(1f, UIRuntime.RootHeight);
        var beforeWidth = view.Width;
        var beforeHeight = view.Height;
        var beforeX = view.X;
        var beforeY = view.Y;

        // Keep adaptation semantic: root view resized to runtime root, children follow FGUI relations.
        view.SetXY(0f, 0f);
        view.SetSize(rootWidth, rootHeight, true);
        Game.Logger.LogInformation(
            "[FGUI][LAYOUT] root-size-apply package={Package} component={Component} root={RootWidth}x{RootHeight} viewBefore={BeforeWidth}x{BeforeHeight}@{BeforeX},{BeforeY} viewAfter={AfterWidth}x{AfterHeight}@{AfterX},{AfterY}",
            packageName,
            componentName,
            rootWidth,
            rootHeight,
            beforeWidth,
            beforeHeight,
            beforeX,
            beforeY,
            view.Width,
            view.Height,
            view.X,
            view.Y);
    }
}
#endif

