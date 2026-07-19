#if CLIENT
using System.Drawing;
using FairyGUI.Utils;

namespace FairyGUI;

public class GComponent : GObject
{
    protected readonly List<GObject> _children = new();
    protected readonly List<Controller> _controllers = new();
    protected readonly List<Transition> _transitions = new();
    protected int _sortingChildCount;
    protected ChildrenRenderOrder _childrenRenderOrder = ChildrenRenderOrder.Ascent;
    protected int _apexIndex;
    protected bool _boundsChanged;
    protected ScrollPane? _scrollPane;
    protected RectangleF? _clipRect;
    protected OverflowType _overflow = OverflowType.Visible;
    protected Margin _margin;
    // clipSoftness 是显示层效果，不能破坏列表项（或动画）原有的 Alpha。缓存每项的基础 Alpha 与上次应用的系数，
    // 便于关闭羽化/项目离开列表时无损恢复，也避免每帧重复写入原生控件。
    private readonly Dictionary<GObject, (float BaseAlpha, float Factor)> _clipSoftnessAlphaStates = new();
    private bool _isDisposing;
    internal int BuildingDisplayList;
    internal Controller? _applyingController;

    public int NumChildren => _children.Count;
    public ScrollPane? ScrollPane => _scrollPane;
    public OverflowType Overflow { get => _overflow; set => _overflow = value; }
    public Margin Margin { get => _margin; set { _margin = value; SetBoundsChangedFlag(); } }
    public bool Opaque { get; set; } = true;
    /// <summary>遮罩子对象（FGUI 二进制第4块）。SDK 无稳定遮罩 API，渲染层尽力应用、找不到则降级。</summary>
    public GObject? MaskObject { get; private set; }
    /// <summary>是否反向遮罩。</summary>
    public bool MaskInverted { get; private set; }

    /// <summary>
    /// 裁剪边缘羽化(近似 FGUI clipSoftness)。原生无软裁剪能力，这里按各子对象中心到视口边缘的距离，
    /// 在 softness 像素带内把子对象 Alpha 从 1 线性渐隐到 0——任意背景下都是真 alpha 渐隐(选型 B)。
    /// 由 <see cref="ScrollPane.UpdateScrollPosition"/> 在每次滚动时调用；无 softness 时不会进来。
    /// 原生不支持逐像素软裁剪，因此按项的中心位置近似；项目自身的基础 Alpha 会与羽化系数相乘。
    /// </summary>
    internal void UpdateClipSoftness()
    {
        var pane = _scrollPane;
        if (pane == null) return;

        float sx = pane.ClipSoftnessX;
        float sy = pane.ClipSoftnessY;
        if (sx <= 0 && sy <= 0)
        {
            RestoreClipSoftnessAlphas();
            return;
        }

        float posX = pane.ScrollingPosX;
        float posY = pane.ScrollingPosY;
        float viewW = pane.ViewWidth;
        float viewH = pane.ViewHeight;

        for (int i = 0; i < _children.Count; i++)
        {
            var child = _children[i];

            // 先算羽化系数（只用到位置，便宜）。
            float factor = 1f;
            if (sy > 0)
            {
                // 子对象中心相对视口顶部的纵坐标（child.Y 为内容坐标，减去滚动偏移得视口坐标）。
                float center = child.Y - posY + child.Height * 0.5f;
                float a = Math.Clamp(Math.Min(center, viewH - center) / sy, 0f, 1f);
                if (a < factor) factor = a;
            }
            if (sx > 0)
            {
                float center = child.X - posX + child.Width * 0.5f;
                float a = Math.Clamp(Math.Min(center, viewW - center) / sx, 0f, 1f);
                if (a < factor) factor = a;
            }

            bool hasState = _clipSoftnessAlphaStates.TryGetValue(child, out var state);

            // 羽化带之外（factor≈1，滚动时占绝大多数项）：不接管 Alpha。
            // 若此前被羽化过则还原其基础 Alpha 并移除状态；否则完全不碰、不写字典——
            // 这样状态字典只保留真正在羽化的少数边缘项，避免随列表规模膨胀与每帧冗余写入。
            if (factor >= 0.9999f)
            {
                if (hasState) RestoreClipSoftnessAlpha(child);
                continue;
            }

            // 需要羽化：确定基础 Alpha（吸收外部在两次刷新间对 Alpha 的改动），应用并记录状态。
            float baseAlpha;
            if (hasState)
            {
                float expectedAlpha = state.BaseAlpha * state.Factor;
                baseAlpha = MathF.Abs(child.Alpha - expectedAlpha) > 0.0001f
                    ? child.Alpha
                    : state.BaseAlpha;
            }
            else
            {
                baseAlpha = child.Alpha;
            }

            if (!hasState || MathF.Abs(state.BaseAlpha - baseAlpha) > 0.0001f || MathF.Abs(state.Factor - factor) > 0.0001f)
            {
                _clipSoftnessAlphaStates[child] = (baseAlpha, factor);
            }

            float appliedAlpha = baseAlpha * factor;
            if (MathF.Abs(child.Alpha - appliedAlpha) > 0.0001f)
            {
                child.Alpha = appliedAlpha;
            }
        }
    }

    private void RestoreClipSoftnessAlpha(GObject child)
    {
        if (_clipSoftnessAlphaStates.Remove(child, out var state))
        {
            child.Alpha = state.BaseAlpha;
        }
    }

    private void RestoreClipSoftnessAlphas()
    {
        foreach (var pair in _clipSoftnessAlphaStates)
        {
            pair.Key.Alpha = pair.Value.BaseAlpha;
        }

        _clipSoftnessAlphaStates.Clear();
    }

    public GObject AddChild(GObject child) => AddChildAt(child, _children.Count);

    public GObject AddChildAt(GObject child, int index)
    {
        if (child.Parent == this) { SetChildIndex(child, index); }
        else
        {
            child.RemoveFromParent();
            child.Parent = this;
            int count = _children.Count;
            if (child.SortingOrder != 0) { _sortingChildCount++; index = GetInsertPosForSortingChild(child); }
            else if (_sortingChildCount > 0 && index > count - _sortingChildCount) index = count - _sortingChildCount;
            if (index > count) index = count;
            _children.Insert(index, child);
            ChildStateChanged(child);
            SetBoundsChangedFlag();
            TryAutoEnsureListBounds();
        }
        return child;
    }
    
    internal void ChildStateChanged(GObject child)
    {
        if (NativeObject != null)
        {
            bool finalVisible = child.FinalVisible;
            if (child.NativeObject == null && finalVisible)
                Render.SCERenderContext.Instance.CreateNativeControl(child);
            if (child.NativeObject != null)
            {
                if (finalVisible)
                    Render.SCERenderContext.Instance.RenderChild(this, child);
                else
                    Render.SCERenderContext.Instance.RemoveFromParent(child);
            }
        }
    }

    private int GetInsertPosForSortingChild(GObject child)
    {
        for (int i = 0; i < _children.Count; i++)
            if (_children[i].SortingOrder > child.SortingOrder) return i;
        return _children.Count;
    }

    public GObject RemoveChild(GObject child, bool dispose = false)
    {
        int index = _children.IndexOf(child);
        if (index >= 0) return RemoveChildAt(index, dispose);
        return child;
    }

    public GObject RemoveChildAt(int index, bool dispose = false)
    {
        if (index < 0 || index >= _children.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var child = _children[index];
        CloseComboDropdownsRecursive(child);
        RestoreClipSoftnessAlpha(child);
        child.Parent = null;
        if (child.SortingOrder != 0) _sortingChildCount--;
        _children.RemoveAt(index);
        if (child.NativeObject != null)
            Render.SCERenderContext.Instance.RemoveFromParent(child);
        SetBoundsChangedFlag();
        TryAutoEnsureListBounds();
        if (dispose) child.Dispose();
        return child;
    }

    private static void CloseComboDropdownsRecursive(GObject node)
    {
        if (node is GComboBox combo)
        {
            combo.CloseDropdownByOwnerDetach();
        }

        if (node is not GComponent component)
        {
            return;
        }

        for (var i = 0; i < component._children.Count; i++)
        {
            CloseComboDropdownsRecursive(component._children[i]);
        }
    }

    public void RemoveChildren(int beginIndex = 0, int endIndex = -1, bool dispose = false)
    {
        if (endIndex < 0 || endIndex >= _children.Count) endIndex = _children.Count - 1;
        for (int i = endIndex; i >= beginIndex; i--) RemoveChildAt(i, dispose);
    }

    public GObject GetChildAt(int index)
    {
        if (index < 0 || index >= _children.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _children[index];
    }

    public GObject? GetChild(string name) => _children.FirstOrDefault(c => c.Name == name);
    public GObject? GetChildById(string id) => _children.FirstOrDefault(c => c.Id == id);

    public GObject? GetChildByPath(string path)
    {
        var parts = path.Split('.');
        GComponent? current = this;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            current = current?.GetChild(parts[i]) as GComponent;
            if (current == null) return null;
        }
        return current?.GetChild(parts[^1]);
    }

    public int GetChildIndex(GObject child) => _children.IndexOf(child);

    public void SetChildIndex(GObject child, int index)
    {
        int oldIndex = _children.IndexOf(child);
        if (oldIndex == -1) throw new ArgumentException("Not a child", nameof(child));
        if (child.SortingOrder != 0) return;
        int count = _children.Count;
        if (_sortingChildCount > 0 && index > count - _sortingChildCount - 1) index = count - _sortingChildCount - 1;
        if (oldIndex == index) return;
        _children.RemoveAt(oldIndex);
        if (index > _children.Count) index = _children.Count;
        _children.Insert(index, child);
        SetBoundsChangedFlag();
        TryAutoEnsureListBounds();
    }

    public void SwapChildren(GObject child1, GObject child2)
    {
        int index1 = _children.IndexOf(child1);
        int index2 = _children.IndexOf(child2);
        if (index1 < 0 || index2 < 0) throw new ArgumentException("Not a child");
        SwapChildrenAt(index1, index2);
    }

    public void SwapChildrenAt(int index1, int index2)
    {
        var child1 = _children[index1];
        var child2 = _children[index2];
        SetChildIndex(child1, index2);
        SetChildIndex(child2, index1);
    }

    public IReadOnlyList<GObject> Children => _children.AsReadOnly();

    public int NumControllers => _controllers.Count;
    public Controller? GetController(string name) => _controllers.FirstOrDefault(c => c.Name == name);
    public Controller? GetControllerAt(int index) => (index >= 0 && index < _controllers.Count) ? _controllers[index] : null;
    public void AddController(Controller controller) { _controllers.Add(controller); controller.Parent = this; }
    public void RemoveController(Controller controller) { int index = _controllers.IndexOf(controller); if (index >= 0) { controller.Parent = null; _controllers.RemoveAt(index); } }

    public Transition? GetTransition(string name) => _transitions.FirstOrDefault(t => t.Name == name);
    public Transition? GetTransitionAt(int index) => (index >= 0 && index < _transitions.Count) ? _transitions[index] : null;

    internal void UpdateTransitionsFromRelations(GObject target, float dx, float dy)
    {
        if (_transitions.Count == 0)
        {
            return;
        }

        for (var i = 0; i < _transitions.Count; i++)
        {
            _transitions[i].UpdateFromRelations(target, dx, dy);
        }
    }

    public void ApplyController(Controller controller)
    {
        _applyingController = controller;
        try
        {
            ApplyControllerToDescendants(controller, this);
            controller.RunActions();
        }
        finally
        {
            _applyingController = null;
        }
    }

    private static void ApplyControllerToDescendants(Controller controller, GComponent root)
    {
        foreach (var child in root._children)
        {
            child.HandleControllerChanged(controller);
            if (child is GComponent childComponent)
            {
                ApplyControllerToDescendants(controller, childComponent);
            }
        }
    }

    public void ApplyAllControllers()
    {
        foreach (var controller in _controllers)
            ApplyController(controller);
    }

    public void SetBoundsChangedFlag()
    {
        if (_boundsChanged)
        {
            return;
        }

        _boundsChanged = true;
        if (_scrollPane != null && NativeObject != null && !UnderConstruct && BuildingDisplayList == 0)
        {
            Render.SCERenderContext.Instance.RefreshComponentScrollState(this);
        }
    }
    public void EnsureBoundsCorrect()
    {
        if (!_boundsChanged)
        {
            return;
        }

        UpdateBounds();
        // 子项的尺寸/布局变化未必会改变内容总尺寸；布局完成后仍需刷新一次软裁剪，
        // 否则静止列表的边缘项目可能保留旧的羽化系数。
        if (_scrollPane != null && (_scrollPane.ClipSoftnessX > 0 || _scrollPane.ClipSoftnessY > 0))
        {
            UpdateClipSoftness();
        }
    }
    protected virtual void UpdateBounds() => _boundsChanged = false;

    private void TryAutoEnsureListBounds()
    {
        if (this is GList list && !list.IsVirtual && !list.UnderConstruct && !list.SuppressAutoEnsureBounds)
        {
            list.EnsureBoundsCorrect();
        }
    }

    internal void ChildSortingOrderChanged(GObject child, int oldValue, int newValue)
    {
        if (oldValue == 0) _sortingChildCount++;
        else if (newValue == 0) _sortingChildCount--;
        int oldIndex = _children.IndexOf(child);
        int index = GetInsertPosForSortingChild(child);
        if (oldIndex < index) index--;
        _children.RemoveAt(oldIndex);
        _children.Insert(index, child);
    }

    public override void ConstructFromResource()
    {
        var packageItem = PackageItem;
        if (packageItem?.RawData == null || packageItem.Owner == null) return;
        
        ByteBuffer buffer = packageItem.RawData;
        buffer.Seek(0, 0);
        UnderConstruct = true;
        
        // Block 0: Basic info
        SourceWidth = buffer.ReadInt();
        SourceHeight = buffer.ReadInt();
        InitWidth = SourceWidth;
        InitHeight = SourceHeight;
        SetSize(SourceWidth, SourceHeight);
        
        if (buffer.ReadBool()) { MinWidth = buffer.ReadInt(); MaxWidth = buffer.ReadInt(); MinHeight = buffer.ReadInt(); MaxHeight = buffer.ReadInt(); }
        if (buffer.ReadBool()) { float f1 = buffer.ReadFloat(); float f2 = buffer.ReadFloat(); SetPivot(f1, f2, buffer.ReadBool()); }
        if (buffer.ReadBool()) { _margin.Top = buffer.ReadInt(); _margin.Bottom = buffer.ReadInt(); _margin.Left = buffer.ReadInt(); _margin.Right = buffer.ReadInt(); }
        
        OverflowType overflow = (OverflowType)buffer.ReadByte();
        if (overflow == OverflowType.Scroll) { int savedPos = buffer.Position; buffer.Seek(0, 7); SetupScroll(buffer); buffer.Position = savedPos; }
        else SetupOverflowAndClip(overflow);
        if (buffer.ReadBool()) buffer.Skip(8);
        
        BuildingDisplayList = 1;

        // Block 1: Controllers
        buffer.Seek(0, 1);
        int controllerCount = buffer.ReadShort();
        for (int i = 0; i < controllerCount; i++)
        {
            int nextPos = buffer.ReadUshort() + buffer.Position;
            var controller = new Controller();
            _controllers.Add(controller);
            controller.Parent = this;
            controller.Setup(buffer);
            buffer.Position = nextPos;
        }

        // Block 2: Children - Setup_BeforeAdd
        buffer.Seek(0, 2);
        int childCount = buffer.ReadShort();
        for (int i = 0; i < childCount; i++)
        {
            int dataLen = buffer.ReadShort();
            int curPos = buffer.Position;
            buffer.Seek(curPos, 0);
            ObjectType type = (ObjectType)buffer.ReadByte();
            string? src = buffer.ReadS();
            string? pkgId = buffer.ReadS();
            PackageItem? pi = null;
            if (src != null)
            {
                UIPackage? pkg = pkgId != null ? UIPackage.GetById(pkgId) : packageItem.Owner;
                pi = pkg?.GetItem(src);
            }
            GObject? child = pi != null ? UIObjectFactory.NewObject(pi) : UIObjectFactory.NewObject(type);
            if (child != null)
            {
                if (pi != null)
                {
                    child.PackageItem = pi;
                    pi.Owner?.GetItemAsset(pi);
                    child.ConstructFromResource();
                }
                child.UnderConstruct = true;
                child.Setup_BeforeAdd(buffer, curPos);
                child.Parent = this;
                _children.Add(child);
            }
            buffer.Position = curPos + dataLen;
        }

        // Block 3: Component's own relations (parent to child)
        buffer.Seek(0, 3);
        InitRelations();
        Relations!.Setup(buffer, true);

        // Read child relations from Block 2's sub-block 3
        buffer.Seek(0, 2);
        buffer.Skip(2);
        for (int i = 0; i < childCount; i++)
        {
            int nextPos = buffer.ReadUshort();
            nextPos += buffer.Position;
            
            // Seek to sub-block 3 within this child's data (child relations)
            buffer.Seek(buffer.Position, 3);
            _children[i].InitRelations();
            _children[i].Relations!.Setup(buffer, false);
            
            buffer.Position = nextPos;
        }

        // Children - Setup_AfterAdd
        buffer.Seek(0, 2);
        buffer.Skip(2);
        for (int i = 0; i < childCount; i++)
        {
            int nextPos = buffer.ReadUshort();
            nextPos += buffer.Position;
            _children[i].Setup_AfterAdd(buffer, buffer.Position);
            _children[i].UnderConstruct = false;
            buffer.Position = nextPos;
        }

        // Block 4: Mask, custom data, opaque
        buffer.Seek(0, 4);
        buffer.Skip(2);
        Opaque = buffer.ReadBool();
        // 遮罩：maskId 为子对象索引，-1 表示无；随后 inverted 标记反向遮罩
        int maskId = buffer.ReadShort();
        if (maskId != -1)
        {
            MaskObject = GetChildAt(maskId);
            MaskInverted = buffer.ReadBool();
        }
        
        // Block 5: Transitions
        if (buffer.Seek(0, 5))
        {
            int transitionCount = buffer.ReadShort();
            for (int i = 0; i < transitionCount; i++)
            {
                int nextPos = buffer.ReadUshort() + buffer.Position;
                if (nextPos > buffer.Length) break;
                var transition = new Transition();
                transition.Owner = this;
                transition.Setup(buffer);
                _transitions.Add(transition);
                buffer.Position = nextPos;
            }
        }
        
        // Apply all controllers
        ApplyAllControllers();
        
        BuildingDisplayList = 0;
        UnderConstruct = false;
        
        SetBoundsChangedFlag();
        EnsureBoundsCorrect();

        // Call ConstructExtension for extended types (Button, Label, etc.)
        // This is called AFTER all children are set up, so we can access their properties
        if (packageItem.ObjectType != ObjectType.Component)
            ConstructExtension(buffer);

        ConstructFromXML(new XML());
        
        Game.Logger.LogInformation($"[FGUI] Component parsed: {Name ?? PackageItem?.Name}, Size: {_width}x{_height}, Children: {_children.Count}, Controllers: {_controllers.Count}");
    }

    public virtual void ConstructFromXML(XML xml) { }

    protected virtual void ConstructExtension(ByteBuffer buffer) { }

    public override void Setup_AfterAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_AfterAdd(buffer, beginPos);

        if (!buffer.Seek(beginPos, 4))
        {
            return;
        }

        var pageController = buffer.ReadShort();
        if (pageController != -1 && _scrollPane != null && _scrollPane.PageMode && Parent != null)
        {
            _scrollPane.PageController = Parent.GetControllerAt(pageController);
        }

        var cnt = buffer.ReadShort();
        for (var i = 0; i < cnt; i++)
        {
            var controllerName = buffer.ReadS();
            var pageId = buffer.ReadS();
            if (controllerName == null || pageId == null)
            {
                continue;
            }

            var cc = GetController(controllerName);
            if (cc != null)
            {
                cc.SelectedPageId = pageId;
            }
        }

        if (buffer.Version < 2)
        {
            return;
        }

        cnt = buffer.ReadShort();
        for (var i = 0; i < cnt; i++)
        {
            var target = buffer.ReadS();
            var propertyId = buffer.ReadShort();
            var value = buffer.ReadS();
            if (target == null || value == null)
            {
                continue;
            }

            var obj = GetChildByPath(target);
            if (obj == null)
            {
                continue;
            }

            if (propertyId == 0)
            {
                obj.Text = value;
            }
            else if (propertyId == 1)
            {
                obj.Icon = value;
            }
        }
    }

    protected void SetupScroll(ByteBuffer buffer)
    {
        _scrollPane = new ScrollPane { Owner = this };
        _scrollPane.Setup(buffer);
    }

    protected void SetupOverflowAndClip(OverflowType overflow)
    {
        _overflow = overflow;
        if (overflow == OverflowType.Hidden) _clipRect = new RectangleF(0, 0, _width, _height);
    }

    public override void Dispose()
    {
        if (Disposed || _isDisposing) return;
        _isDisposing = true;

        // Avoid collection-version invalidation when child.Dispose() mutates parent links.
        try
        {
            _clipSoftnessAlphaStates.Clear();
            while (_children.Count > 0)
            {
                var last = _children.Count - 1;
                var child = _children[last];
                _children.RemoveAt(last);
                child.Parent = null;
                child.Dispose();
            }

            _scrollPane?.Dispose();
            _scrollPane = null;

            for (var i = _transitions.Count - 1; i >= 0; i--)
            {
                _transitions[i].Dispose();
            }

            _transitions.Clear();
            base.Dispose();
        }
        finally
        {
            _isDisposing = false;
        }
    }
}
#endif

