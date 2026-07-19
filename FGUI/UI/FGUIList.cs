#if CLIENT
using System.Diagnostics;
using FairyGUI;
using FairyGUI.Utils;
using FairyGUI.Render;

namespace FairyGUI;

public class GList : GComponent
{
    private ListLayoutType _layout = ListLayoutType.SingleColumn;
    private int _lineCount, _columnCount, _lineGap, _columnGap;
    private AlignType _align = AlignType.Left;
    private VertAlignType _verticalAlign = VertAlignType.Top;
    private bool _autoResizeItem;
    private ListSelectionMode _selectionMode = ListSelectionMode.Single;
    private string? _defaultItem;
    private int _lastSelectedIndex = -1;
    private readonly List<int> _selectedIndices = new();
    private bool _virtual;
    private bool _scrollItemToViewOnClick;
    private bool _foldInvisibleItems;
    private int _numItems, _firstIndex;
    private float _itemWidth, _itemHeight;
    
    // SCE VirtualizingPanel bridge
    private object? _virtualPanel;
    private bool _useNativeVirtual;
    private bool _virtualPanelAttached;
    private bool _virtualScrollListenerBound;
    private bool _isDisposing;
    private readonly Dictionary<int, GObject> _virtualItems = new();
    private readonly Dictionary<object, object> _nativeVirtualSlotChildren = new();
    private readonly HashSet<GObject> _boundItemClickHandlers = new();
    private readonly Dictionary<string, Stack<GObject>> _itemPool = new(StringComparer.Ordinal);
    private readonly Dictionary<GObject, string> _itemPoolKeyByObject = new();
    private bool _batchAddingItems;
    private int _batchUpdateDepth;
    private const int MaxPoolSizePerKey = 128;
    private const bool EnablePoolDiagLogs = false;
    private const bool EnableListPerfDiagLogs = false;
    private const int PoolDiagLogLimit = 200;
    private const int ListPerfLogLimit = 120;
    private int _poolDiagLogCount;
    private int _listPerfLogCount;
    
    // 事件监听器
    private EventListener? _onClickItem;
    private EventListener? _onRightClickItem;

    public Action<int, GObject>? ItemRenderer { get; set; }
    public Func<int, string?>? ItemProvider { get; set; }
    
    /// <summary>
    /// 列表项点击事件
    /// </summary>
    public EventListener OnClickItem => _onClickItem ??= new EventListener();
    
    /// <summary>
    /// 列表项右键点击事件
    /// </summary>
    public EventListener OnRightClickItem => _onRightClickItem ??= new EventListener();

    public ListLayoutType Layout { get => _layout; set { if (_layout != value) { _layout = value; SetBoundsChangedFlag(); } } }
    public int LineCount { get => _lineCount; set => _lineCount = value; }
    public int ColumnCount { get => _columnCount; set => _columnCount = value; }
    public int LineGap { get => _lineGap; set { if (_lineGap != value) { _lineGap = value; SetBoundsChangedFlag(); } } }
    public int ColumnGap { get => _columnGap; set { if (_columnGap != value) { _columnGap = value; SetBoundsChangedFlag(); } } }
    public AlignType Align { get => _align; set => _align = value; }
    public VertAlignType VerticalAlign { get => _verticalAlign; set => _verticalAlign = value; }
    public bool AutoResizeItem { get => _autoResizeItem; set => _autoResizeItem = value; }
    public ListSelectionMode SelectionMode { get => _selectionMode; set => _selectionMode = value; }
    public string? DefaultItem { get => _defaultItem; set => _defaultItem = value; }
    public bool IsVirtual => _virtual;
    internal bool IsUsingNativeVirtualization => _virtual && _useNativeVirtual;
    internal bool SuppressAutoEnsureBounds => _batchAddingItems || _batchUpdateDepth > 0;

    public void BeginUpdate()
    {
        _batchUpdateDepth++;
    }

    public void EndUpdate()
    {
        if (_batchUpdateDepth <= 0)
        {
            _batchUpdateDepth = 0;
            return;
        }

        _batchUpdateDepth--;
        if (_batchUpdateDepth == 0 && !_virtual && !UnderConstruct)
        {
            EnsureBoundsCorrect();
        }
    }

    public int NumItems
    {
        get => _virtual ? _numItems : _children.Count;
        set
        {
            if (_virtual) { if (_numItems != value) { _numItems = value; RefreshVirtualList(); } }
            else
            {
                BeginUpdate();
                try
                {
                    int count = _children.Count;
                    if (value > count) { for (int i = count; i < value; i++) AddItemFromPool(); }
                    else RemoveChildrenToPool(value, count);

                    // FairyGUI semantics: in non-virtual mode, itemRenderer + NumItems
                    // should still populate each item by index.
                    if (ItemRenderer != null)
                    {
                        for (int i = 0; i < _children.Count; i++)
                        {
                            ItemRenderer(i, _children[i]);
                        }
                    }
                }
                finally
                {
                    EndUpdate();
                }
            }
        }
    }

    public int SelectedIndex { get => _selectedIndices.Count > 0 ? _selectedIndices[0] : -1; set { ClearSelection(); if (value >= 0 && value < NumItems) AddSelection(value); } }
    public IReadOnlyList<int> SelectedIndices => _selectedIndices.AsReadOnly();

    public void SetVirtual() 
    { 
        if (!_virtual) 
        { 
            _virtual = true; 
            RemoveChildren();
            BindVirtualScrollListener();
            
            // Create SCE VirtualizingPanel if adapter supports it
            var adapter = SCERenderContext.Instance.Adapter;
            if (adapter != null)
            {
                try
                {
                    _virtualPanel = adapter.CreateVirtualizingPanel();
                    _useNativeVirtual = true;
                    
                    // Configure the panel
                    // 交叉轴撑满：单列列表(垂直主轴) item 宽度=列表宽；单行列表(水平主轴) item 高度=列表高。
                    // 对齐 FairyGUI autoResizeItem 语义，否则槽宽只用 item 设计宽，item 不随列表缩放。
                    bool isHorizontal = _layout == ListLayoutType.SingleRow || _layout == ListLayoutType.FlowVertical;
                    var config = new VirtualPanelConfig
                    {
                        ItemWidth = (!isHorizontal && _autoResizeItem && Width > 0)
                            ? Width
                            : (_itemWidth > 0 ? _itemWidth : Width),
                        ItemHeight = (isHorizontal && _autoResizeItem && Height > 0)
                            ? Height
                            : (_itemHeight > 0 ? _itemHeight : 50),
                        IsHorizontal = isHorizontal
                    };
                    adapter.SetVirtualizingPanelConfig(_virtualPanel, config);
                    
                    // Add to native control
                    if (NativeObject != null && _virtualPanel != null)
                    {
                        adapter.AddChild(NativeObject, _virtualPanel);
                        adapter.SetSize(_virtualPanel, Width, Height);
                        _virtualPanelAttached = true;
                    }
                    else
                    {
                        _virtualPanelAttached = false;
                    }
                }
                catch
                {
                    _useNativeVirtual = false;
                    _virtualPanelAttached = false;
                }
            }
        } 
    }
    
    public void SetVirtualAndLoop() => SetVirtual();

    public GObject AddItemFromPool(string? url = null)
    {
        url ??= _defaultItem;
        if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("DefaultItem not set");
        var obj = GetFromPool(url);
        if (obj == null) throw new InvalidOperationException($"Failed to create item from '{url}'");

        ApplyListItemSafetyPipeline(obj);
        
        // 注册点击事件
        SetupItemClickHandler(obj);
        
        return AddChild(obj);
    }
    
    /// <summary>
    /// 为列表项设置点击事件处理
    /// </summary>
    private void SetupItemClickHandler(GObject item)
    {
        if (!_boundItemClickHandlers.Add(item))
        {
            return;
        }

        // 左键点击
        item.OnClick.Add((ctx) =>
        {
            int index = GetChildIndex(item);
            if (_virtual)
                index = ChildIndexToItemIndex(index);
            
            if (index >= 0)
            {
                DispatchItemEvent(item, ctx, false);
            }
        });
        
        // 右键点击
        item.OnRightClick.Add((ctx) =>
        {
            int index = GetChildIndex(item);
            if (_virtual)
                index = ChildIndexToItemIndex(index);
            
            if (index >= 0)
            {
                DispatchItemEvent(item, ctx, true);
            }
        });
    }
    
    /// <summary>
    /// 分发列表项事件
    /// </summary>
    private void DispatchItemEvent(GObject item, EventContext sourceCtx, bool isRightClick)
    {
        int index = GetChildIndex(item);
        if (_virtual)
            index = ChildIndexToItemIndex(index);
        
        if (index < 0) return;
        
        // 处理选择
        if (!isRightClick)
        {
            if (_selectionMode == ListSelectionMode.Single)
            {
                ClearSelection();
                AddSelection(index, false);
            }
            else if (_selectionMode == ListSelectionMode.Multiple)
            {
                // Ctrl+Click 切换选择状态
                if (sourceCtx.inputEvent?.ctrl == true)
                {
                    if (_selectedIndices.Contains(index))
                        RemoveSelection(index);
                    else
                        AddSelection(index, false);
                }
                // Shift+Click 范围选择
                else if (sourceCtx.inputEvent?.shift == true && _lastSelectedIndex >= 0)
                {
                    int start = Math.Min(_lastSelectedIndex, index);
                    int end = Math.Max(_lastSelectedIndex, index);
                    for (int i = start; i <= end; i++)
                        AddSelection(i, false);
                }
                else
                {
                    ClearSelection();
                    AddSelection(index, false);
                }
            }
            else if (_selectionMode == ListSelectionMode.Multiple_SingleClick)
            {
                if (_selectedIndices.Contains(index))
                    RemoveSelection(index);
                else
                    AddSelection(index, false);
            }
        }
        
        // 创建新的事件上下文
        var ctx = new EventContext
        {
            Sender = this,
            Type = isRightClick ? "onRightClickItem" : "onClickItem",
            Data = item,
            inputEvent = sourceCtx.inputEvent
        };
        
        // 触发事件
        if (isRightClick)
            _onRightClickItem?.Call(ctx);
        else
            _onClickItem?.Call(ctx);
    }

    private GObject? GetFromPool(string url)
    {
        if (!TryResolvePoolItem(url, out var item, out var poolKey) || item?.Owner == null)
        {
            return null;
        }

        if (_itemPool.TryGetValue(poolKey, out var stack) && stack.Count > 0)
        {
            var reused = stack.Pop();
            _itemPoolKeyByObject[reused] = poolKey;
            PreparePooledItemForReuse(reused);
            LogPoolDiag("reuse", poolKey, stack.Count, "ok");
            return reused;
        }

        var created = item.Owner.CreateObject(item);
        if (created != null)
        {
            _itemPoolKeyByObject[created] = poolKey;
            var poolCount = _itemPool.TryGetValue(poolKey, out var createdStack) ? createdStack.Count : 0;
            LogPoolDiag("create", poolKey, poolCount, "miss");
        }

        return created;
    }

    private void ApplyListItemSafetyPipeline(GObject itemRoot)
    {
        _ = itemRoot;
    }

    private void ReturnToPool(GObject obj)
    {
        if (obj.Disposed)
        {
            _boundItemClickHandlers.Remove(obj);
            _itemPoolKeyByObject.Remove(obj);
            LogPoolDiag("drop", "<unknown>", 0, "disposed");
            return;
        }

        if (!_itemPoolKeyByObject.TryGetValue(obj, out var poolKey) || string.IsNullOrWhiteSpace(poolKey))
        {
            _boundItemClickHandlers.Remove(obj);
            obj.Dispose();
            LogPoolDiag("drop", "<unknown>", 0, "no-key");
            return;
        }

        if (!_itemPool.TryGetValue(poolKey, out var stack))
        {
            stack = new Stack<GObject>();
            _itemPool[poolKey] = stack;
        }

        if (stack.Count >= MaxPoolSizePerKey)
        {
            _boundItemClickHandlers.Remove(obj);
            _itemPoolKeyByObject.Remove(obj);
            obj.Dispose();
            LogPoolDiag("drop", poolKey, stack.Count, "overflow");
            return;
        }

        PreparePooledItemForRecycle(obj);
        stack.Push(obj);
        LogPoolDiag("recycle", poolKey, stack.Count, "ok");
    }

    private static void PreparePooledItemForRecycle(GObject obj)
    {
        obj.Visible = true;
        obj.Touchable = true;
        obj.Grayed = false;
        obj.Alpha = 1f;
        obj.Rotation = 0f;
        obj.SetScale(1f, 1f);
        obj.SetXY(0f, 0f);
        if (obj is GButton button)
        {
            button.Selected = false;
        }
    }

    private static void PreparePooledItemForReuse(GObject obj)
    {
        obj.Visible = true;
        obj.Touchable = true;
        obj.Grayed = false;
        obj.Alpha = 1f;
        obj.Rotation = 0f;
        obj.SetScale(1f, 1f);
    }

    private bool TryResolvePoolItem(string url, out PackageItem? item, out string poolKey)
    {
        item = UIPackage.GetItemByURL(url);

        // If not a valid URL and we have a parent with PackageItem,
        // it might be just an item ID/name from the same package.
        if (item == null && !url.StartsWith(UIPackage.URL_PREFIX))
        {
            var ownerPackage = PackageItem?.Owner;
            if (ownerPackage != null)
            {
                item = ownerPackage.GetItem(url);
                item ??= ownerPackage.GetItemByName(url);
            }

            if (item == null)
            {
                foreach (var pkg in UIPackage.GetPackages())
                {
                    item = pkg.GetItem(url);
                    if (item != null) break;
                }
            }
        }

        if (item?.Owner == null || string.IsNullOrWhiteSpace(item.Id))
        {
            poolKey = string.Empty;
            return false;
        }

        poolKey = $"{item.Owner.Id}:{item.Id}";
        return true;
    }

    public void RemoveChildrenToPool(int beginIndex = 0, int endIndex = -1)
    {
        if (endIndex < 0 || endIndex >= _children.Count) endIndex = _children.Count;
        for (int i = endIndex - 1; i >= beginIndex; i--) ReturnToPool(RemoveChildAt(i));
    }

    public GObject GetItemAt(int index)
    {
        if (_virtual)
        {
            if (index < _firstIndex || index >= _firstIndex + _children.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _children[index - _firstIndex];
        }
        return GetChildAt(index);
    }

    public void AddSelection(int index, bool scrollItToView = false)
    {
        if (_selectionMode == ListSelectionMode.None) return;
        if (_selectionMode == ListSelectionMode.Single) ClearSelection();
        if (!_selectedIndices.Contains(index))
        {
            _selectedIndices.Add(index);
            _lastSelectedIndex = index;
            if (!_virtual && index < _children.Count) { var obj = _children[index] as GButton; if (obj != null) obj.Selected = true; }
        }
    }

    public void RemoveSelection(int index)
    {
        if (_selectedIndices.Remove(index) && !_virtual && index < _children.Count)
        {
            var obj = _children[index] as GButton;
            if (obj != null) obj.Selected = false;
        }
    }

    public void ClearSelection()
    {
        foreach (int index in _selectedIndices.ToArray())
            if (!_virtual && index < _children.Count) { var obj = _children[index] as GButton; if (obj != null) obj.Selected = false; }
        _selectedIndices.Clear();
    }

    public void SelectAll()
    {
        if (_selectionMode == ListSelectionMode.Single || _selectionMode == ListSelectionMode.None) return;
        ClearSelection();
        for (int i = 0; i < NumItems; i++) AddSelection(i);
    }

    public void SelectReverse()
    {
        if (_selectionMode == ListSelectionMode.Single || _selectionMode == ListSelectionMode.None) return;
        var current = _selectedIndices.ToList();
        ClearSelection();
        for (int i = 0; i < NumItems; i++) if (!current.Contains(i)) AddSelection(i);
    }

    /// <summary>
    /// 滚动列表使指定索引的项目可见
    /// </summary>
    /// <param name="index">项目索引</param>
    public void ScrollToView(int index)
    {
        ScrollToView(index, false, false);
    }

    /// <summary>
    /// 滚动列表使指定索引的项目可见
    /// </summary>
    /// <param name="index">项目索引</param>
    /// <param name="animate">是否使用动画</param>
    public void ScrollToView(int index, bool animate)
    {
        ScrollToView(index, animate, false);
    }

    /// <summary>
    /// 滚动列表使指定索引的项目可见
    /// </summary>
    /// <param name="index">项目索引</param>
    /// <param name="animate">是否使用动画</param>
    /// <param name="setFirst">如果为true，滚动到顶部/左侧；如果为false，滚动到视图中的任意位置</param>
    public void ScrollToView(int index, bool animate, bool setFirst)
    {
        if (index < 0 || index >= NumItems)
            return;

        if (_virtual)
        {
            // 虚拟列表模式
            if (_numItems == 0)
                return;

            // 简化实现：计算项目的大概位置
            // 完整实现需要 UpdateBounds() 提供准确的位置信息
            System.Drawing.RectangleF rect;

            if (_layout == ListLayoutType.SingleColumn || _layout == ListLayoutType.FlowHorizontal)
            {
                // 垂直布局
                float itemH = _itemHeight > 0 ? _itemHeight : 50;
                float pos = index * (itemH + _lineGap);
                rect = new System.Drawing.RectangleF(0, pos, _itemWidth > 0 ? _itemWidth : Width, itemH);
            }
            else if (_layout == ListLayoutType.SingleRow || _layout == ListLayoutType.FlowVertical)
            {
                // 水平布局
                float itemW = _itemWidth > 0 ? _itemWidth : 100;
                float pos = index * (itemW + _columnGap);
                rect = new System.Drawing.RectangleF(pos, 0, itemW, _itemHeight > 0 ? _itemHeight : Height);
            }
            else
            {
                // 分页布局 - 简化实现
                float itemW = _itemWidth > 0 ? _itemWidth : 100;
                float itemH = _itemHeight > 0 ? _itemHeight : 50;
                int colCount = _columnCount > 0 ? _columnCount : 1;
                int row = index / colCount;
                int col = index % colCount;
                rect = new System.Drawing.RectangleF(
                    col * (itemW + _columnGap),
                    row * (itemH + _lineGap),
                    itemW, itemH);
            }

            if (ScrollPane != null)
                ScrollPane.ScrollToView(rect, animate, setFirst);
            else if (Parent?.ScrollPane != null)
                Parent.ScrollPane.ScrollToView(rect, animate, setFirst);
        }
        else
        {
            // 非虚拟列表模式
            if (index >= _children.Count)
                return;

            var obj = GetChildAt(index);
            if (ScrollPane != null)
                ScrollPane.ScrollToView(obj, animate, setFirst);
            else if (Parent?.ScrollPane != null)
                Parent.ScrollPane.ScrollToView(obj, animate, setFirst);
        }
    }

    /// <summary>
    /// 获取第一个可见的子项索引
    /// </summary>
    /// <returns>第一个可见子项的索引，如果没有可见项返回-1</returns>
    public int GetFirstChildInView()
    {
        if (ScrollPane == null)
            return 0;

        for (int i = 0; i < NumItems; i++)
        {
            if (_virtual)
            {
                // 虚拟列表：检查索引是否在可见范围内
                if (i >= _firstIndex && i < _firstIndex + _children.Count)
                {
                    int childIndex = i - _firstIndex;
                    if (childIndex < _children.Count && ScrollPane.IsChildInView(_children[childIndex]))
                        return i;
                }
            }
            else
            {
                // 普通列表
                if (i < _children.Count && ScrollPane.IsChildInView(_children[i]))
                    return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 将子项索引转换为项目索引（用于虚拟列表）
    /// </summary>
    /// <param name="index">子项索引</param>
    /// <returns>项目索引</returns>
    public int ChildIndexToItemIndex(int index)
    {
        if (!_virtual)
            return index;

        return _firstIndex + index;
    }

    /// <summary>
    /// 将项目索引转换为子项索引（用于虚拟列表）
    /// </summary>
    /// <param name="index">项目索引</param>
    /// <returns>子项索引，如果不在可见范围内返回-1</returns>
    public int ItemIndexToChildIndex(int index)
    {
        if (!_virtual)
            return index;

        if (index < _firstIndex || index >= _firstIndex + _children.Count)
            return -1;

        return index - _firstIndex;
    }

    /// <summary>
    /// 调整列表大小以适应内容
    /// </summary>
    /// <param name="itemCount">要适应的项目数量，默认为所有项目</param>
    /// <param name="minSize">最小尺寸</param>
    public void ResizeToFit(int itemCount = int.MaxValue, int minSize = 0)
    {
        EnsureItemSizeHintsFromChildren();

        if (itemCount > NumItems)
            itemCount = NumItems;

        if (itemCount == 0)
        {
            if (_layout == ListLayoutType.SingleColumn || _layout == ListLayoutType.FlowHorizontal)
                SetSize(Width, minSize);
            else
                SetSize(minSize, Height);
            return;
        }

        // 简化实现：基于项目尺寸和间距计算
        if (_layout == ListLayoutType.SingleColumn)
        {
            float itemH = _itemHeight > 0 ? _itemHeight : 50;
            float totalHeight = itemCount * itemH + (itemCount - 1) * _lineGap;
            SetSize(Width, Math.Max(totalHeight, minSize));
        }
        else if (_layout == ListLayoutType.SingleRow)
        {
            float itemW = _itemWidth > 0 ? _itemWidth : 100;
            float totalWidth = itemCount * itemW + (itemCount - 1) * _columnGap;
            SetSize(Math.Max(totalWidth, minSize), Height);
        }
        else if (_layout == ListLayoutType.FlowHorizontal || _layout == ListLayoutType.FlowVertical)
        {
            // 流式布局需要更复杂的计算
            // 这里提供简化版本
            int colCount = _columnCount > 0 ? _columnCount : 1;
            int rowCount = (itemCount + colCount - 1) / colCount;

            if (_layout == ListLayoutType.FlowHorizontal)
            {
                float itemH = _itemHeight > 0 ? _itemHeight : 50;
                float totalHeight = rowCount * itemH + (rowCount - 1) * _lineGap;
                SetSize(Width, Math.Max(totalHeight, minSize));
            }
            else
            {
                float itemW = _itemWidth > 0 ? _itemWidth : 100;
                float totalWidth = rowCount * itemW + (rowCount - 1) * _columnGap;
                SetSize(Math.Max(totalWidth, minSize), Height);
            }
        }
    }

    private int _curLineItemCount = 1;
    private int _curLineItemCount2 = 1;
    private bool _virtualListChanged = true;

    /// <summary>
    /// 让 item 在交叉轴上撑满列表（对齐 FairyGUI autoResizeItem）：
    /// 单列列表撑满宽度，单行列表撑满高度；主轴尺寸保持 item 自身。
    /// </summary>
    private void ApplyItemCrossAxisSize(GObject item)
    {
        if (!_autoResizeItem)
        {
            return;
        }

        bool isHorizontal = _layout == ListLayoutType.SingleRow || _layout == ListLayoutType.FlowVertical;
        if (isHorizontal)
        {
            if (Height > 0f)
            {
                item.SetSize(item.Width, Height, true);
            }
        }
        else if (Width > 0f)
        {
            item.SetSize(Width, item.Height, true);
        }
    }

    private void RefreshVirtualList()
    {
        if (!_virtual) return;

        var adapter = SCERenderContext.Instance.Adapter;

        // Use SCE native VirtualizingPanel if available
        if (_useNativeVirtual && _virtualPanel != null && adapter != null)
        {
            var renderedByNativeVirtual = false;
            EnsureNativeVirtualPanelAttached(adapter);
            adapter.SetVirtualizingPanelItems(_virtualPanel, _numItems, (index, nativeControl) =>
            {
                renderedByNativeVirtual = true;
                // Get or create FGUI item for this index
                if (!_virtualItems.TryGetValue(index, out var item))
                {
                    item = CreateItemFromPool();
                    if (item != null)
                    {
                        _virtualItems[index] = item;
                    }
                }

                if (item != null)
                {
                    // Call user's ItemRenderer
                    ItemRenderer?.Invoke(index, item);

                    // 交叉轴撑满列表（autoResizeItem）：否则 item 保持设计宽(如 300)，不随列表宽度缩放
                    ApplyItemCrossAxisSize(item);

                    // Sync FGUI item to native control
                    var itemNative = item.NativeObject ?? SCERenderContext.Instance.CreateNativeControl(item);
                    if (itemNative != null)
                    {
                        if (_nativeVirtualSlotChildren.TryGetValue(nativeControl, out var previousNative) &&
                            !ReferenceEquals(previousNative, itemNative))
                        {
                            adapter.RemoveChild(nativeControl, previousNative);
                        }

                        // The native control from VirtualizingPanel should be used as parent
                        adapter.AddChild(nativeControl, itemNative);
                        _nativeVirtualSlotChildren[nativeControl] = itemNative;
                    }
                }
            });

            adapter.RefreshVirtualizingPanel(_virtualPanel);
            if (renderedByNativeVirtual || _numItems <= 0)
            {
                return;
            }

            // Native virtualization callback did not fire; fall back to manual virtual path.
            _useNativeVirtual = false;
        }

        // Manual fallback implementation (stability-first):
        // when native virtual panel is unavailable, render all items once and let native scroll container
        // handle viewport movement. This avoids repeated recycle/reposition jitter.
        if (ScrollPane == null)
            return;

        // Calculate line item count for different layouts
        if (_virtualListChanged)
        {
            _virtualListChanged = false;

            if (_layout == ListLayoutType.SingleColumn || _layout == ListLayoutType.SingleRow)
            {
                _curLineItemCount = 1;
            }
            else if (_layout == ListLayoutType.FlowHorizontal)
            {
                if (_columnCount > 0)
                    _curLineItemCount = _columnCount;
                else
                {
                    float itemW = _itemWidth > 0 ? _itemWidth : 100;
                    _curLineItemCount = (int)Math.Floor((ScrollPane.ViewWidth + _columnGap) / (itemW + _columnGap));
                    if (_curLineItemCount <= 0)
                        _curLineItemCount = 1;
                }
            }
            else if (_layout == ListLayoutType.FlowVertical)
            {
                if (_lineCount > 0)
                    _curLineItemCount = _lineCount;
                else
                {
                    float itemH = _itemHeight > 0 ? _itemHeight : 50;
                    _curLineItemCount = (int)Math.Floor((ScrollPane.ViewHeight + _lineGap) / (itemH + _lineGap));
                    if (_curLineItemCount <= 0)
                        _curLineItemCount = 1;
                }
            }
            else // Pagination
            {
                if (_columnCount > 0)
                    _curLineItemCount = _columnCount;
                else
                {
                    float itemW = _itemWidth > 0 ? _itemWidth : 100;
                    _curLineItemCount = (int)Math.Floor((ScrollPane.ViewWidth + _columnGap) / (itemW + _columnGap));
                    if (_curLineItemCount <= 0)
                        _curLineItemCount = 1;
                }

                if (_lineCount > 0)
                    _curLineItemCount2 = _lineCount;
                else
                {
                    float itemH = _itemHeight > 0 ? _itemHeight : 50;
                    _curLineItemCount2 = (int)Math.Floor((ScrollPane.ViewHeight + _lineGap) / (itemH + _lineGap));
                    if (_curLineItemCount2 <= 0)
                        _curLineItemCount2 = 1;
                }
            }
        }

        // Calculate content size
        float ch = 0, cw = 0;
        if (_numItems > 0)
        {
            int len = (int)Math.Ceiling((float)_numItems / _curLineItemCount) * _curLineItemCount;
            int len2 = Math.Min(_curLineItemCount, _numItems);

            if (_layout == ListLayoutType.SingleColumn || _layout == ListLayoutType.FlowHorizontal)
            {
                // Vertical scrolling
                float itemH = _itemHeight > 0 ? _itemHeight : 50;
                int rows = (int)Math.Ceiling((float)_numItems / _curLineItemCount);
                ch = rows * itemH + (rows - 1) * _lineGap;

                if (_autoResizeItem)
                    cw = ScrollPane.ViewWidth;
                else
                {
                    float itemW = _itemWidth > 0 ? _itemWidth : 100;
                    cw = len2 * itemW + (len2 - 1) * _columnGap;
                }
            }
            else if (_layout == ListLayoutType.SingleRow || _layout == ListLayoutType.FlowVertical)
            {
                // Horizontal scrolling
                float itemW = _itemWidth > 0 ? _itemWidth : 100;
                int cols = (int)Math.Ceiling((float)_numItems / _curLineItemCount);
                cw = cols * itemW + (cols - 1) * _columnGap;

                if (_autoResizeItem)
                    ch = ScrollPane.ViewHeight;
                else
                {
                    float itemH = _itemHeight > 0 ? _itemHeight : 50;
                    ch = len2 * itemH + (len2 - 1) * _lineGap;
                }
            }
            else // Pagination
            {
                int pageCount = (int)Math.Ceiling((float)len / (_curLineItemCount * _curLineItemCount2));
                cw = pageCount * ScrollPane.ViewWidth;
                ch = ScrollPane.ViewHeight;
            }
        }

        ScrollPane.SetContentSize(cw, ch);
        RenderManualFallbackAllItems();
    }

    /// <summary>
    /// 处理滚动，更新可见项目
    /// </summary>
    private void HandleScroll()
    {
        if (!_virtual || ScrollPane == null)
            return;

        // Calculate which items should be visible
        int newFirstIndex = 0;
        int newLastIndex = _numItems - 1;

        if (_layout == ListLayoutType.SingleColumn || _layout == ListLayoutType.FlowHorizontal)
        {
            // Vertical scrolling
            float scrollY = ScrollPane.ScrollingPosY;
            float viewHeight = ScrollPane.ViewHeight;
            float itemH = _itemHeight > 0 ? _itemHeight : 50;

            // Calculate first visible row
            int firstRow = (int)Math.Floor(scrollY / (itemH + _lineGap));
            // Calculate last visible row (add buffer)
            int lastRow = (int)Math.Ceiling((scrollY + viewHeight) / (itemH + _lineGap)) + 1;

            newFirstIndex = Math.Max(0, firstRow * _curLineItemCount);
            newLastIndex = Math.Min(_numItems - 1, (lastRow + 1) * _curLineItemCount - 1);
        }
        else if (_layout == ListLayoutType.SingleRow || _layout == ListLayoutType.FlowVertical)
        {
            // Horizontal scrolling
            float scrollX = ScrollPane.ScrollingPosX;
            float viewWidth = ScrollPane.ViewWidth;
            float itemW = _itemWidth > 0 ? _itemWidth : 100;

            // Calculate first visible column
            int firstCol = (int)Math.Floor(scrollX / (itemW + _columnGap));
            // Calculate last visible column (add buffer)
            int lastCol = (int)Math.Ceiling((scrollX + viewWidth) / (itemW + _columnGap)) + 1;

            newFirstIndex = Math.Max(0, firstCol * _curLineItemCount);
            newLastIndex = Math.Min(_numItems - 1, (lastCol + 1) * _curLineItemCount - 1);
        }
        else // Pagination
        {
            float scrollX = ScrollPane.ScrollingPosX;
            float viewWidth = ScrollPane.ViewWidth;
            int page = (int)Math.Floor(scrollX / viewWidth);
            int itemsPerPage = _curLineItemCount * _curLineItemCount2;

            newFirstIndex = page * itemsPerPage;
            newLastIndex = Math.Min(_numItems - 1, (page + 1) * itemsPerPage - 1);
        }

        // Clamp indices
        newFirstIndex = Math.Max(0, Math.Min(newFirstIndex, _numItems - 1));
        newLastIndex = Math.Max(0, Math.Min(newLastIndex, _numItems - 1));

        var rangeChanged = newFirstIndex != _firstIndex || newLastIndex - newFirstIndex + 1 != _children.Count;
        if (rangeChanged)
        {
            _firstIndex = newFirstIndex;
            int visibleCount = newLastIndex - newFirstIndex + 1;

            // Adjust children count
            while (_children.Count < visibleCount)
            {
                var item = AddItemFromPool();
                if (item == null) break;
            }
            while (_children.Count > visibleCount)
            {
                ReturnToPool(RemoveChildAt(_children.Count - 1));
            }

        }

        if (rangeChanged)
        {
            for (int i = 0; i < _children.Count; i++)
            {
                int itemIndex = _firstIndex + i;
                if (itemIndex >= _numItems) break;

                var child = _children[i];
                CalculateItemPosition(itemIndex, out float x, out float y);
                child.SetXY(x, y);
                ItemRenderer?.Invoke(itemIndex, child);
            }
        }
    }

    /// <summary>
    /// 计算虚拟列表项目的位置
    /// </summary>
    private void CalculateItemPosition(int index, out float x, out float y)
    {
        float itemW = _itemWidth > 0 ? _itemWidth : 100;
        float itemH = _itemHeight > 0 ? _itemHeight : 50;

        if (_layout == ListLayoutType.SingleColumn)
        {
            x = 0;
            y = index * (itemH + _lineGap);
        }
        else if (_layout == ListLayoutType.SingleRow)
        {
            x = index * (itemW + _columnGap);
            y = 0;
        }
        else if (_layout == ListLayoutType.FlowHorizontal)
        {
            int row = index / _curLineItemCount;
            int col = index % _curLineItemCount;
            x = col * (itemW + _columnGap);
            y = row * (itemH + _lineGap);
        }
        else if (_layout == ListLayoutType.FlowVertical)
        {
            int col = index / _curLineItemCount;
            int row = index % _curLineItemCount;
            x = col * (itemW + _columnGap);
            y = row * (itemH + _lineGap);
        }
        else // Pagination
        {
            int itemsPerPage = _curLineItemCount * _curLineItemCount2;
            int page = index / itemsPerPage;
            int indexInPage = index % itemsPerPage;
            int row = indexInPage / _curLineItemCount;
            int col = indexInPage % _curLineItemCount;

            x = page * ScrollPane.ViewWidth + col * (itemW + _columnGap);
            y = row * (itemH + _lineGap);
        }
    }
    
    private GObject? CreateItemFromPool()
    {
        if (string.IsNullOrEmpty(_defaultItem)) return null;
        return GetFromPool(_defaultItem);
    }

    private int CalculateVisibleCount()
    {
        if (ScrollPane == null) return _numItems;
        float size = _layout == ListLayoutType.SingleColumn || _layout == ListLayoutType.FlowHorizontal ? ScrollPane.ViewHeight : ScrollPane.ViewWidth;
        float itemSize = _layout == ListLayoutType.SingleColumn || _layout == ListLayoutType.FlowHorizontal ? _itemHeight : _itemWidth;
        if (itemSize <= 0) return _numItems;
        return Math.Min((int)Math.Ceiling(size / itemSize) + 1, _numItems);
    }

    /// <summary>
    /// 更新列表布局和边界
    /// </summary>
    protected override void UpdateBounds()
    {
        // 虚拟列表不需要UpdateBounds，由RefreshVirtualList处理
        if (_virtual)
            return;

        int cnt = _children.Count;
        if (cnt == 0)
        {
            if (ScrollPane != null)
                ScrollPane.SetContentSize(0, 0);
            _boundsChanged = false;
            return;
        }

        int i, j = 0;
        GObject child;
        float curX = 0;
        float curY = 0;
        float cw, ch;
        float maxWidth = 0;
        float maxHeight = 0;
        float viewWidth = Width;
        float viewHeight = Height;

        if (_layout == ListLayoutType.SingleColumn)
        {
            // 单列垂直布局
            for (i = 0; i < cnt; i++)
            {
                child = GetChildAt(i);
                if (!child.Visible)
                    continue;

                if (curY != 0)
                    curY += _lineGap;

                child.Y = curY;

                if (_autoResizeItem)
                    child.SetSize(viewWidth, child.Height, true);

                curY += (float)Math.Ceiling(child.Height);

                if (child.Width > maxWidth)
                    maxWidth = child.Width;
            }

            ch = curY;
            cw = (float)Math.Ceiling(maxWidth);
        }
        else if (_layout == ListLayoutType.SingleRow)
        {
            // 单行水平布局
            for (i = 0; i < cnt; i++)
            {
                child = GetChildAt(i);
                if (!child.Visible)
                    continue;

                if (curX != 0)
                    curX += _columnGap;

                child.X = curX;

                if (_autoResizeItem)
                    child.SetSize(child.Width, viewHeight, true);

                curX += (float)Math.Ceiling(child.Width);

                if (child.Height > maxHeight)
                    maxHeight = child.Height;
            }

            cw = curX;
            ch = (float)Math.Ceiling(maxHeight);
        }
        else if (_layout == ListLayoutType.FlowHorizontal)
        {
            // 水平流式布局
            if (_autoResizeItem && _columnCount > 0)
            {
                // 自动调整项目尺寸以填充行
                float lineSize = 0;
                int lineStart = 0;
                float remainSize, remainPercent;

                for (i = 0; i < cnt; i++)
                {
                    child = GetChildAt(i);
                    if (!child.Visible)
                        continue;

                    lineSize += child.SourceWidth;
                    j++;

                    if (j == _columnCount || i == cnt - 1)
                    {
                        remainSize = viewWidth - (j - 1) * _columnGap;
                        remainPercent = 1;
                        curX = 0;

                        for (int k = lineStart; k <= i; k++)
                        {
                            child = GetChildAt(k);
                            if (!child.Visible)
                                continue;

                            child.SetXY(curX, curY);
                            float perc = child.SourceWidth / lineSize;
                            child.SetSize((float)Math.Round(perc / remainPercent * remainSize), child.Height, true);
                            remainSize -= child.Width;
                            remainPercent -= perc;
                            curX += child.Width + _columnGap;

                            if (child.Height > maxHeight)
                                maxHeight = child.Height;
                        }

                        // 新行
                        curY += (float)Math.Ceiling(maxHeight) + _lineGap;
                        maxHeight = 0;
                        j = 0;
                        lineStart = i + 1;
                        lineSize = 0;
                    }
                }

                ch = curY + (float)Math.Ceiling(maxHeight);
                cw = viewWidth;
            }
            else
            {
                // 自然流式布局
                for (i = 0; i < cnt; i++)
                {
                    child = GetChildAt(i);
                    if (!child.Visible)
                        continue;

                    if (curX != 0)
                        curX += _columnGap;

                    if ((_columnCount != 0 && j >= _columnCount) ||
                        (_columnCount == 0 && curX + child.Width > viewWidth && maxHeight != 0))
                    {
                        // 新行
                        curX = 0;
                        curY += (float)Math.Ceiling(maxHeight) + _lineGap;
                        maxHeight = 0;
                        j = 0;
                    }

                    child.SetXY(curX, curY);
                    curX += (float)Math.Ceiling(child.Width);

                    if (curX > maxWidth)
                        maxWidth = curX;
                    if (child.Height > maxHeight)
                        maxHeight = child.Height;

                    j++;
                }

                ch = curY + (float)Math.Ceiling(maxHeight);
                cw = (float)Math.Ceiling(maxWidth);
            }
        }
        else if (_layout == ListLayoutType.FlowVertical)
        {
            // 垂直流式布局
            if (_autoResizeItem && _lineCount > 0)
            {
                // 自动调整项目尺寸以填充列
                float lineSize = 0;
                int lineStart = 0;
                float remainSize, remainPercent;

                for (i = 0; i < cnt; i++)
                {
                    child = GetChildAt(i);
                    if (!child.Visible)
                        continue;

                    lineSize += child.SourceHeight;
                    j++;

                    if (j == _lineCount || i == cnt - 1)
                    {
                        remainSize = viewHeight - (j - 1) * _lineGap;
                        remainPercent = 1;
                        curY = 0;

                        for (int k = lineStart; k <= i; k++)
                        {
                            child = GetChildAt(k);
                            if (!child.Visible)
                                continue;

                            child.SetXY(curX, curY);
                            float perc = child.SourceHeight / lineSize;
                            child.SetSize(child.Width, (float)Math.Round(perc / remainPercent * remainSize), true);
                            remainSize -= child.Height;
                            remainPercent -= perc;
                            curY += child.Height + _lineGap;

                            if (child.Width > maxWidth)
                                maxWidth = child.Width;
                        }

                        // 新列
                        curX += (float)Math.Ceiling(maxWidth) + _columnGap;
                        maxWidth = 0;
                        j = 0;
                        lineStart = i + 1;
                        lineSize = 0;
                    }
                }

                cw = curX + (float)Math.Ceiling(maxWidth);
                ch = viewHeight;
            }
            else
            {
                // 自然流式布局
                for (i = 0; i < cnt; i++)
                {
                    child = GetChildAt(i);
                    if (!child.Visible)
                        continue;

                    if (curY != 0)
                        curY += _lineGap;

                    if ((_lineCount != 0 && j >= _lineCount) ||
                        (_lineCount == 0 && curY + child.Height > viewHeight && maxWidth != 0))
                    {
                        // 新列
                        curY = 0;
                        curX += (float)Math.Ceiling(maxWidth) + _columnGap;
                        maxWidth = 0;
                        j = 0;
                    }

                    child.SetXY(curX, curY);
                    curY += child.Height;

                    if (curY > maxHeight)
                        maxHeight = curY;
                    if (child.Width > maxWidth)
                        maxWidth = child.Width;

                    j++;
                }

                cw = curX + (float)Math.Ceiling(maxWidth);
                ch = (float)Math.Ceiling(maxHeight);
            }
        }
        else // Pagination
        {
            // 分页布局
            int page = 0;
            int k = 0;
            float eachHeight = 0;

            if (_autoResizeItem && _lineCount > 0)
                eachHeight = (float)Math.Floor((viewHeight - (_lineCount - 1) * _lineGap) / _lineCount);

            if (_autoResizeItem && _columnCount > 0)
            {
                // 自动调整尺寸的分页布局
                float lineSize = 0;
                int lineStart = 0;
                float remainSize, remainPercent;

                for (i = 0; i < cnt; i++)
                {
                    child = GetChildAt(i);
                    if (!child.Visible)
                        continue;

                    if (j == 0 && ((_lineCount != 0 && k >= _lineCount) ||
                        (_lineCount == 0 && curY + (_lineCount > 0 ? eachHeight : child.Height) > viewHeight)))
                    {
                        // 新页
                        page++;
                        curY = 0;
                        k = 0;
                    }

                    lineSize += child.SourceWidth;
                    j++;

                    if (j == _columnCount || i == cnt - 1)
                    {
                        remainSize = viewWidth - (j - 1) * _columnGap;
                        remainPercent = 1;
                        curX = 0;

                        for (int m = lineStart; m <= i; m++)
                        {
                            child = GetChildAt(m);
                            if (!child.Visible)
                                continue;

                            child.SetXY(page * viewWidth + curX, curY);
                            float perc = child.SourceWidth / lineSize;
                            child.SetSize((float)Math.Round(perc / remainPercent * remainSize),
                                         _lineCount > 0 ? eachHeight : child.Height, true);
                            remainSize -= child.Width;
                            remainPercent -= perc;
                            curX += child.Width + _columnGap;

                            if (child.Height > maxHeight)
                                maxHeight = child.Height;
                        }

                        // 新行
                        curY += (float)Math.Ceiling(maxHeight) + _lineGap;
                        maxHeight = 0;
                        j = 0;
                        lineStart = i + 1;
                        lineSize = 0;
                        k++;
                    }
                }
            }
            else
            {
                // 自然尺寸的分页布局
                for (i = 0; i < cnt; i++)
                {
                    child = GetChildAt(i);
                    if (!child.Visible)
                        continue;

                    if (curX != 0)
                        curX += _columnGap;

                    if (_autoResizeItem && _lineCount > 0)
                        child.SetSize(child.Width, eachHeight, true);

                    if ((_columnCount != 0 && j >= _columnCount) ||
                        (_columnCount == 0 && curX + child.Width > viewWidth && maxHeight != 0))
                    {
                        curX = 0;
                        curY += maxHeight + _lineGap;
                        maxHeight = 0;
                        j = 0;
                        k++;

                        if ((_lineCount != 0 && k >= _lineCount) ||
                            (_lineCount == 0 && curY + child.Height > viewHeight && maxWidth != 0))
                        {
                            // 新页
                            page++;
                            curY = 0;
                            k = 0;
                        }
                    }

                    child.SetXY(page * viewWidth + curX, curY);
                    curX += (float)Math.Ceiling(child.Width);

                    if (curX > maxWidth)
                        maxWidth = curX;
                    if (child.Height > maxHeight)
                        maxHeight = child.Height;

                    j++;
                }
            }

            ch = page > 0 ? viewHeight : (curY + (float)Math.Ceiling(maxHeight));
            cw = (page + 1) * viewWidth;
        }

        // 处理对齐
        HandleAlign(cw, ch);

        // 设置内容边界
        if (ScrollPane != null)
            ScrollPane.SetContentSize(cw, ch);
        _boundsChanged = false;
    }

    /// <summary>
    /// 处理内容对齐
    /// </summary>
    private void HandleAlign(float contentWidth, float contentHeight)
    {
        float viewWidth = Width;
        float viewHeight = Height;

        if (contentWidth < viewWidth)
        {
            float offsetX = 0;
            if (_align == AlignType.Center)
                offsetX = (viewWidth - contentWidth) / 2;
            else if (_align == AlignType.Right)
                offsetX = viewWidth - contentWidth;

            if (offsetX != 0)
            {
                foreach (var child in _children)
                {
                    child.X += offsetX;
                }
            }
        }

        if (contentHeight < viewHeight)
        {
            float offsetY = 0;
            if (_verticalAlign == VertAlignType.Middle)
                offsetY = (viewHeight - contentHeight) / 2;
            else if (_verticalAlign == VertAlignType.Bottom)
                offsetY = viewHeight - contentHeight;

            if (offsetY != 0)
            {
                foreach (var child in _children)
                {
                    child.Y += offsetY;
                }
            }
        }
    }

    public override void Setup_BeforeAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_BeforeAdd(buffer, beginPos);
        if (!buffer.Seek(beginPos, 5))
        {
            return;
        }

        _layout = (ListLayoutType)buffer.ReadByte();
        _selectionMode = (ListSelectionMode)buffer.ReadByte();
        _align = (AlignType)buffer.ReadByte();
        _verticalAlign = (VertAlignType)buffer.ReadByte();
        _lineGap = buffer.ReadShort();
        _columnGap = buffer.ReadShort();
        _lineCount = buffer.ReadShort();
        _columnCount = buffer.ReadShort();
        _autoResizeItem = buffer.ReadBool();

        try
        {
            _childrenRenderOrder = (ChildrenRenderOrder)buffer.ReadByte();
            _apexIndex = buffer.ReadShort();

            if (buffer.ReadBool())
            {
                _margin.Top = buffer.ReadInt();
                _margin.Bottom = buffer.ReadInt();
                _margin.Left = buffer.ReadInt();
                _margin.Right = buffer.ReadInt();
            }

            var overflow = (OverflowType)buffer.ReadByte();
            if (overflow == OverflowType.Scroll)
            {
                var savedPos = buffer.Position;
                if (buffer.Seek(beginPos, 7))
                {
                    SetupScroll(buffer);
                }

                buffer.Position = savedPos;
            }
            else
            {
                SetupOverflowAndClip(overflow);
            }

            if (buffer.ReadBool())
            {
                // clipSoftness：裁剪边缘羽化像素(Vector2，两个 int)。原生无软裁剪，交给
                // GComponent.UpdateClipSoftness 用逐项 Alpha 渐隐近似(见 ScrollPane.SetClipSoftness)。
                int clipSoftnessX = buffer.ReadInt();
                int clipSoftnessY = buffer.ReadInt();
                _scrollPane?.SetClipSoftness(clipSoftnessX, clipSoftnessY);
            }

            if (buffer.Version >= 2)
            {
                _scrollItemToViewOnClick = buffer.ReadBool();
                _foldInvisibleItems = buffer.ReadBool();
            }
        }
        catch (Exception ex)
        {
            System.GC.KeepAlive(0);
        }

        string? defaultItem = null;
        var hasItemsBlock = buffer.Seek(beginPos, 8);
        if (hasItemsBlock)
        {
            try
            {
                defaultItem = buffer.ReadS();
            }
            catch (Exception ex)
            {
                System.GC.KeepAlive(0);
            }
        }

        _defaultItem = defaultItem;
        if (hasItemsBlock)
        {
            ReadItems(buffer);
        }
    }

    protected virtual void ReadItems(ByteBuffer buffer)
    {
        var itemCount = 0;
        var perfStart = EnableListPerfDiagLogs ? Stopwatch.GetTimestamp() : 0L;
        var addedCount = 0;
        try
        {
            itemCount = buffer.ReadShort();
        }
        catch (Exception ex)
        {
            System.GC.KeepAlive(0);
            return;
        }

        _batchAddingItems = true;
        try
        {
            for (int i = 0; i < itemCount; i++)
            {
                int nextPos;
                try
                {
                    nextPos = buffer.ReadUshort() + buffer.Position;
                }
                catch (Exception ex)
                {
                    System.GC.KeepAlive(0);
                    return;
                }

                try
                {
                    var resource = buffer.ReadS();
                    if (string.IsNullOrWhiteSpace(resource))
                    {
                        resource = _defaultItem;
                    }

                    if (string.IsNullOrWhiteSpace(resource))
                    {
                        buffer.Position = nextPos;
                        continue;
                    }

                    var obj = GetFromPool(resource);
                    if (obj == null)
                    {
                        buffer.Position = nextPos;
                        continue;
                    }

                    ApplyListItemSafetyPipeline(obj);
                    SetupItemClickHandler(obj);
                    AddChild(obj);
                    SetupItem(buffer, obj);
                    addedCount++;
                }
                catch (Exception ex)
                {
                    System.GC.KeepAlive(0);
                }
                finally
                {
                    buffer.Position = nextPos;
                }
            }
        }
        finally
        {
            _batchAddingItems = false;
            if (!_virtual)
            {
                EnsureBoundsCorrect();
            }

            if (EnableListPerfDiagLogs)
            {
                var elapsedMs = (Stopwatch.GetTimestamp() - perfStart) * 1000.0 / Stopwatch.Frequency;
                LogListPerf("ReadItems", itemCount, addedCount, elapsedMs);
            }
        }
    }

    private void EnsureNativeVirtualPanelAttached(ISCEAdapter adapter)
    {
        if (_virtualPanel == null || NativeObject == null)
        {
            return;
        }

        if (!_virtualPanelAttached)
        {
            adapter.AddChild(NativeObject, _virtualPanel);
            _virtualPanelAttached = true;
        }

        adapter.SetSize(_virtualPanel, Width, Height);
    }

    private void EnsureItemSizeHintsFromChildren()
    {
        if (_itemWidth > 0 && _itemHeight > 0)
        {
            return;
        }

        for (int i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            if (!child.Visible)
            {
                continue;
            }

            if (_itemWidth <= 0 && child.Width > 0)
            {
                _itemWidth = child.Width;
            }

            if (_itemHeight <= 0 && child.Height > 0)
            {
                _itemHeight = child.Height;
            }

            if (_itemWidth > 0 && _itemHeight > 0)
            {
                break;
            }
        }
    }

    private void LogPoolDiag(string action, string key, int poolCount, string reason)
    {
        if (!EnablePoolDiagLogs || _poolDiagLogCount >= PoolDiagLogLimit)
        {
            return;
        }

        _poolDiagLogCount++;
        System.GC.KeepAlive(0);
    }

    private void LogListPerf(string stage, int requestedItems, int addedItems, double elapsedMs)
    {
        if (_listPerfLogCount >= ListPerfLogLimit)
        {
            return;
        }

        var shouldLog = _listPerfLogCount < 10 || elapsedMs >= 8.0;
        if (!shouldLog)
        {
            return;
        }

        _listPerfLogCount++;
        System.GC.KeepAlive(0);
    }

    protected virtual void SetupItem(ByteBuffer buffer, GObject obj)
    {
        var text = buffer.ReadS();
        if (text != null)
        {
            obj.Text = text;
        }

        var selectedTitle = buffer.ReadS();
        if (selectedTitle != null && obj is GButton buttonForTitle)
        {
            buttonForTitle.SelectedTitle = selectedTitle;
        }

        var icon = buffer.ReadS();
        if (icon != null)
        {
            obj.Icon = icon;
        }

        var selectedIcon = buffer.ReadS();
        if (selectedIcon != null && obj is GButton buttonForIcon)
        {
            buttonForIcon.SelectedIcon = selectedIcon;
        }

        var name = buffer.ReadS();
        if (name != null)
        {
            obj.Name = name;
        }

        if (obj is not GComponent comp)
        {
            return;
        }

        var controllerStateCount = buffer.ReadShort();
        for (int i = 0; i < controllerStateCount; i++)
        {
            var controllerName = buffer.ReadS();
            var pageId = buffer.ReadS();
            if (controllerName == null || pageId == null)
            {
                continue;
            }

            var controller = comp.GetController(controllerName);
            if (controller != null)
            {
                controller.SelectedPageId = pageId;
            }
        }

        if (buffer.Version < 2)
        {
            return;
        }

        var propertyCount = buffer.ReadShort();
        for (int i = 0; i < propertyCount; i++)
        {
            var targetPath = buffer.ReadS();
            var propertyId = buffer.ReadShort();
            var value = buffer.ReadS();
            if (targetPath == null || value == null)
            {
                continue;
            }

            var target = comp.GetChildByPath(targetPath);
            if (target == null)
            {
                continue;
            }

            if (propertyId == 0)
            {
                target.Text = value;
            }
            else if (propertyId == 1)
            {
                target.Icon = value;
            }
        }
    }
    
    public override void Dispose()
    {
        if (Disposed || _isDisposing) return;
        _isDisposing = true;

        try
        {
            // Snapshot first: item.Dispose may indirectly mutate list internals.
            var virtualItems = new List<GObject>(_virtualItems.Values);
            _virtualItems.Clear();
            for (var i = virtualItems.Count - 1; i >= 0; i--)
            {
                virtualItems[i].Dispose();
            }

            foreach (var stack in _itemPool.Values)
            {
                while (stack.Count > 0)
                {
                    var pooled = stack.Pop();
                    if (!pooled.Disposed)
                    {
                        pooled.Dispose();
                    }
                }
            }

            _itemPool.Clear();
            _itemPoolKeyByObject.Clear();
            _boundItemClickHandlers.Clear();

            // Clean up virtual panel
            var adapter = SCERenderContext.Instance.Adapter;
            if (_virtualPanel != null && adapter != null)
            {
                adapter.Dispose(_virtualPanel);
                _virtualPanel = null;
            }
            _virtualPanelAttached = false;

            _nativeVirtualSlotChildren.Clear();
            UnbindVirtualScrollListener();
            base.Dispose();
        }
        finally
        {
            _isDisposing = false;
        }
    }

    private void BindVirtualScrollListener()
    {
        if (_virtualScrollListenerBound)
        {
            return;
        }

        AddEventListener("onScroll", HandleVirtualScrollEvent);
        AddEventListener("onScrollEnd", HandleVirtualScrollEvent);
        _virtualScrollListenerBound = true;
    }

    private void UnbindVirtualScrollListener()
    {
        if (!_virtualScrollListenerBound)
        {
            return;
        }

        RemoveEventListener("onScroll", HandleVirtualScrollEvent);
        RemoveEventListener("onScrollEnd", HandleVirtualScrollEvent);
        _virtualScrollListenerBound = false;
    }

    private void HandleVirtualScrollEvent(EventContext _)
    {
        if (!_virtual || _numItems <= 0 || ScrollPane == null)
        {
            return;
        }

        // Native virtual path is driven by VirtualizingPanel callbacks.
        // Manual fallback path renders all items at data-refresh time.
        // Neither mode should re-run per-scroll reposition here.
        return;
    }

    private void RenderManualFallbackAllItems()
    {
        if (!_virtual)
        {
            return;
        }

        var targetCount = Math.Max(0, _numItems);
        while (_children.Count < targetCount)
        {
            var item = AddItemFromPool();
            if (item == null)
            {
                break;
            }
        }

        while (_children.Count > targetCount)
        {
            ReturnToPool(RemoveChildAt(_children.Count - 1));
        }

        for (int i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            CalculateItemPosition(i, out float x, out float y);
            child.SetXY(x, y);
            ItemRenderer?.Invoke(i, child);
        }
    }

    public void EnableArrowKeyNavigation(bool enabled)
    {
        if (enabled)
        {
            // this.tabStopChildren = true; // TODO: Implement tabStopChildren
            OnKeyDown.Add(__keydown);
        }
        else
        {
            // this.tabStopChildren = false;
            OnKeyDown.Remove(__keydown);
        }
    }

    private void __keydown(EventContext context)
    {
        if (context.inputEvent == null) return;
        
        int index = -1;
        switch (context.inputEvent.keyCode)
        {
            case KeyCode.LeftArrow:
                index = HandleArrowKey(7);
                break;

            case KeyCode.RightArrow:
                index = HandleArrowKey(3);
                break;

            case KeyCode.UpArrow:
                index = HandleArrowKey(1);
                break;

            case KeyCode.DownArrow:
                index = HandleArrowKey(5);
                break;
        }

        if (index != -1)
        {
            index = ItemIndexToChildIndex(index);
            if (index != -1)
            {
                // DispatchItemEvent(GetChildAt(index), context);
                // For now, assume selection change is enough
            }

            context.StopPropagation();
        }
    }

    public int HandleArrowKey(int dir)
    {
        int curIndex = this.SelectedIndex;
        if (curIndex == -1)
            return -1;

        int index = curIndex;
        switch (dir)
        {
            case 1://up
                if (_layout == ListLayoutType.SingleColumn || _layout == ListLayoutType.FlowVertical)
                {
                    index--;
                }
                else if (_layout == ListLayoutType.FlowHorizontal || _layout == ListLayoutType.Pagination)
                {
                    if (_virtual)
                    {
                        index -= _curLineItemCount;
                    }
                    else
                    {
                        GObject current = _children[index];
                        int k = 0;
                        int i;
                        for (i = index - 1; i >= 0; i--)
                        {
                            GObject obj = _children[i];
                            if (obj.Y != current.Y)
                            {
                                current = obj;
                                break;
                            }
                            k++;
                        }
                        for (; i >= 0; i--)
                        {
                            GObject obj = _children[i];
                            if (obj.Y != current.Y)
                            {
                                index = i + k + 1;
                                break;
                            }
                        }
                    }
                }
                break;

            case 3://right
                if (_layout == ListLayoutType.SingleRow || _layout == ListLayoutType.FlowHorizontal || _layout == ListLayoutType.Pagination)
                {
                    index++;
                }
                else if (_layout == ListLayoutType.FlowVertical)
                {
                    if (_virtual)
                    {
                        index += _curLineItemCount;
                    }
                    else
                    {
                        GObject current = _children[index];
                        int k = 0;
                        int cnt = _children.Count;
                        int i;
                        for (i = index + 1; i < cnt; i++)
                        {
                            GObject obj = _children[i];
                            if (obj.X != current.X)
                            {
                                current = obj;
                                break;
                            }
                            k++;
                        }
                        for (; i < cnt; i++)
                        {
                            GObject obj = _children[i];
                            if (obj.X != current.X)
                            {
                                index = i - k - 1;
                                break;
                            }
                        }
                    }
                }
                break;

            case 5://down
                if (_layout == ListLayoutType.SingleColumn || _layout == ListLayoutType.FlowVertical)
                {
                    index++;
                }
                else if (_layout == ListLayoutType.FlowHorizontal || _layout == ListLayoutType.Pagination)
                {
                    if (_virtual)
                    {
                        index += _curLineItemCount;
                    }
                    else
                    {
                        GObject current = _children[index];
                        int k = 0;
                        int cnt = _children.Count;
                        int i;
                        for (i = index + 1; i < cnt; i++)
                        {
                            GObject obj = _children[i];
                            if (obj.Y != current.Y)
                            {
                                current = obj;
                                break;
                            }
                            k++;
                        }
                        for (; i < cnt; i++)
                        {
                            GObject obj = _children[i];
                            if (obj.Y != current.Y)
                            {
                                index = i - k - 1;
                                break;
                            }
                        }
                    }
                }
                break;

            case 7://left
                if (_layout == ListLayoutType.SingleRow || _layout == ListLayoutType.FlowHorizontal || _layout == ListLayoutType.Pagination)
                {
                    index--;
                }
                else if (_layout == ListLayoutType.FlowVertical)
                {
                    if (_virtual)
                    {
                        index -= _curLineItemCount;
                    }
                    else
                    {
                        GObject current = _children[index];
                        int k = 0;
                        int i;
                        for (i = index - 1; i >= 0; i--)
                        {
                            GObject obj = _children[i];
                            if (obj.X != current.X)
                            {
                                current = obj;
                                break;
                            }
                            k++;
                        }
                        for (; i >= 0; i--)
                        {
                            GObject obj = _children[i];
                            if (obj.X != current.X)
                            {
                                index = i + k + 1;
                                break;
                            }
                        }
                    }
                }
                break;
        }

        if (index != curIndex && index >= 0 && index < NumItems)
        {
            ClearSelection();
            AddSelection(index, true);
            return index;
        }
        else
            return -1;
    }
}
#endif

