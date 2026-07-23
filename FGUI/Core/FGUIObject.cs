#if CLIENT
using System.Drawing;
using FairyGUI;
using FairyGUI.Gears;
using FairyGUI.Render;
using FairyGUI.Utils;

namespace FairyGUI;

public class GObject : EventDispatcher
{
    private static uint _gInstanceCounter;

    public string Id { get; private set; }
    public string Name { get; set; } = string.Empty;
    public object? Data { get; set; }

    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public int InitWidth { get; set; }
    public int InitHeight { get; set; }
    public int MinWidth { get; set; }
    public int MaxWidth { get; set; }
    public int MinHeight { get; set; }
    public int MaxHeight { get; set; }

    public GComponent? Parent { get; internal set; }
    public PackageItem? PackageItem { get; set; }
    public GGroup? Group { get; set; }
    public string? ResourceUrl { get; internal set; }

    protected float _x, _y, _z;
    protected float _width, _height, _rawWidth, _rawHeight;
    protected float _pivotX, _pivotY;
    protected bool _pivotAsAnchor;
    protected float _scaleX = 1, _scaleY = 1;
    protected float _rotation;
    protected float _alpha = 1;
    protected bool _visible = true;
    protected bool _touchable = true;
    protected bool _grayed;
    protected bool _draggable;
    protected int _sortingOrder;
    protected BlendMode _blendMode = BlendMode.Normal;
    
    // Gear system
    protected GearBase?[] _gears = new GearBase?[10];
    protected bool _internalVisible = true;
    protected bool _handlingController;

    internal bool UnderConstruct;
    internal bool GearLocked;
    internal float SizePercentInGroup;
    internal bool Disposed;
    internal GTreeNode? _treeNode;
    
    // Gesture support
    private bool _touchBehaviorEnabled;
    private TouchBehaviorConfig? _touchBehaviorConfig;
    private Action? _longPressHandler;
    private Action? _doubleClickHandler;
    private Action<float, float>? _swipeHandler;
    private bool _isPointerCaptured;

    private object? _nativeObject;
    private bool _dragEnabled;  // 是否已经设置了拖拽事件
    
    public object? NativeObject 
    { 
        get => _nativeObject;
        set 
        { 
            _nativeObject = value;
            // NativeObject创建后，如果Draggable为true但还没有启用拖拽，则启用
            if (value != null && _draggable && !_dragEnabled)
            {
                EnableDrag();
            }
        }
    }
    
    public bool Draggable 
    { 
        get => _draggable; 
        set 
        { 
            _draggable = value; 
            if (value && !_dragEnabled) 
                EnableDrag(); 
        } 
    }
    public Relations? Relations { get; private set; }
    
    public void InitRelations() => Relations ??= new Relations(this);

    public GObject() { Id = "_n" + _gInstanceCounter++; }

    public float X { get => _x; set => SetPosition(value, _y, _z); }
    public float Y { get => _y; set => SetPosition(_x, value, _z); }
    public float Z { get => _z; set => SetPosition(_x, _y, value); }
    public PointF XY { get => new(_x, _y); set => SetPosition(value.X, value.Y, _z); }
    public float Width { get => _width; set => SetSize(value, _rawHeight); }
    public float Height { get => _height; set => SetSize(_rawWidth, value); }
    public SizeF Size { get => new(_width, _height); set => SetSize(value.Width, value.Height); }
    public float ActualWidth => _width * _scaleX;
    public float ActualHeight => _height * _scaleY;

    public void SetXY(float xv, float yv) => SetPosition(xv, yv, _z);

    public virtual void SetPosition(float xv, float yv, float zv)
    {
        if (_x != xv || _y != yv || _z != zv)
        {
            _x = xv; _y = yv; _z = zv;
            HandlePositionChanged();
            UpdateGear(1);
            Parent?.SetBoundsChangedFlag();
            Group?.SetBoundsChangedFlag(true);
            DispatchEvent("onPositionChanged", null);
        }
    }

    public virtual void SetSize(float wv, float hv, bool ignorePivot = false)
    {
        if (_rawWidth != wv || _rawHeight != hv)
        {
            _rawWidth = wv; _rawHeight = hv;
            if (wv < MinWidth) wv = MinWidth;
            else if (MaxWidth > 0 && wv > MaxWidth) wv = MaxWidth;
            if (hv < MinHeight) hv = MinHeight;
            else if (MaxHeight > 0 && hv > MaxHeight) hv = MaxHeight;
            float dWidth = wv - _width;
            float dHeight = hv - _height;
            _width = wv; _height = hv;
            HandleSizeChanged();
            if ((_pivotX != 0 || _pivotY != 0) && !_pivotAsAnchor && !ignorePivot)
                SetXY(_x - _pivotX * dWidth, _y - _pivotY * dHeight);
            UpdateGear(2);
            if (Parent != null)
            {
                Relations?.OnOwnerSizeChanged(dWidth, dHeight, _pivotAsAnchor || !ignorePivot);
                Parent.SetBoundsChangedFlag();
                Group?.SetBoundsChangedFlag();
            }
            else
            {
                Group?.SetBoundsChangedFlag();
            }
            DispatchEvent("onSizeChanged", null);

        }
    }

    protected void SetSizeDirectly(float wv, float hv)
    {
        _rawWidth = wv; _rawHeight = hv;
        _width = Math.Max(wv, 0); _height = Math.Max(hv, 0);
    }

    public float ScaleX { get => _scaleX; set => SetScale(value, _scaleY); }
    public float ScaleY { get => _scaleY; set => SetScale(_scaleX, value); }
    public PointF Scale { get => new(_scaleX, _scaleY); set => SetScale(value.X, value.Y); }

    public void SetScale(float sx, float sy)
    {
        if (_scaleX != sx || _scaleY != sy)
        {
            _scaleX = sx;
            _scaleY = sy;
            HandleScaleChanged();
            UpdateGear(2);

        }
    }

    public float Rotation { get => _rotation; set { if (_rotation != value) { _rotation = value; HandleRotationChanged(); UpdateGear(3); } } }

    public float PivotX { get => _pivotX; set => SetPivot(value, _pivotY, _pivotAsAnchor); }
    public float PivotY { get => _pivotY; set => SetPivot(_pivotX, value, _pivotAsAnchor); }
    public PointF Pivot { get => new(_pivotX, _pivotY); set => SetPivot(value.X, value.Y, _pivotAsAnchor); }
    public bool PivotAsAnchor { get => _pivotAsAnchor; set => SetPivot(_pivotX, _pivotY, value); }

    public void SetPivot(float xv, float yv, bool asAnchor = false)
    {
        if (_pivotX != xv || _pivotY != yv || _pivotAsAnchor != asAnchor)
        {
            _pivotX = xv; _pivotY = yv; _pivotAsAnchor = asAnchor;
            HandlePositionChanged();
        }
    }

    public float Alpha { get => _alpha; set { if (_alpha != value) { _alpha = value; HandleAlphaChanged(); UpdateGear(3); } } }

    public bool Visible
    {
        get => _visible;
        set { if (_visible != value) { _visible = value; HandleVisibleChanged(); Parent?.SetBoundsChangedFlag(); } }
    }
    /// <summary>
    /// Returns the final visibility considering Visible, internal visibility (GearDisplay), and Group visibility
    /// </summary>
    public bool FinalVisible => _visible && _internalVisible && (Group == null || Group.FinalVisible);

    public bool Touchable
    {
        get => _touchable;
        set { if (_touchable != value) { _touchable = value; HandleTouchableChanged(); UpdateGear(3); } }
    }

    public bool Grayed
    {
        get => _grayed;
        set { if (_grayed != value) { _grayed = value; HandleGrayedChanged(); UpdateGear(3); } }
    }

    public bool Enabled { get => !_grayed && _touchable; set { Grayed = !value; Touchable = value; } }

    public int SortingOrder
    {
        get => _sortingOrder;
        set
        {
            if (value < 0) value = 0;
            if (_sortingOrder != value)
            {
                int old = _sortingOrder;
                _sortingOrder = value;
                Parent?.ChildSortingOrderChanged(this, old, _sortingOrder);
            }
        }
    }

    public BlendMode BlendMode { get => _blendMode; set => _blendMode = value; }

    public virtual string? Text { get => null; set { UpdateGear(6); } }
    public virtual string? Icon { get => null; set { UpdateGear(7); } }

    public void RemoveFromParent() => Parent?.RemoveChild(this);

    public GObject? Root
    {
        get
        {
            GObject p = this;
            while (p.Parent != null) p = p.Parent;
            return p;
        }
    }

    public string? ResourceURL => PackageItem != null ? UIPackage.URL_PREFIX + PackageItem.Owner?.Id + PackageItem.Id : null;

    // Gear system
    public GearBase GetGear(int index)
    {
        var gear = _gears[index];
        if (gear == null)
        {
            gear = index switch
            {
                0 => new GearDisplay(this),
                1 => new GearXY(this),
                2 => new GearSize(this),
                3 => new GearLook(this),
                4 => new GearColor(this),
                5 => new GearAnimation(this),
                6 => new GearText(this),
                7 => new GearIcon(this),
                8 => new GearDisplay2(this),
                9 => new GearFontSize(this),
                _ => throw new Exception($"Invalid gear index: {index}")
            };
            _gears[index] = gear;
        }
        return gear;
    }

    internal void UpdateGear(int index)
    {
        if (UnderConstruct || GearLocked) return;
        var gear = _gears[index];
        if (gear != null && gear.Controller != null)
            gear.UpdateState();
    }

    internal void UpdateGearFromRelations(int index, float dx, float dy)
    {
        if (UnderConstruct || GearLocked)
            return;

        var gear = _gears[index];
        if (gear != null)
            gear.UpdateFromRelations(dx, dy);
    }

    internal bool CheckGearController(int index, Controller c) => _gears[index] != null && _gears[index]!.Controller == c;

    internal uint AddDisplayLock()
    {
        if (_gears[0] is GearDisplay displayGear)
        {
            uint token = displayGear.AddLock();
            CheckGearDisplay();
            return token;
        }

        return 0;
    }

    internal void ReleaseDisplayLock(uint token)
    {
        if (token == 0)
        {
            return;
        }

        if (_gears[0] is GearDisplay displayGear)
        {
            displayGear.ReleaseLock(token);
            CheckGearDisplay();
        }
    }

    void CheckGearDisplay()
    {
        if (_handlingController) return;
        bool connected = _gears[0] is not GearDisplay gd || gd.Connected;
        if (_gears[8] is GearDisplay2 gd2)
            connected = gd2.Evaluate(connected);
        if (string.Equals(Name, "btns", StringComparison.Ordinal))
//             Game.Logger.LogInformation(
//                 "[FGUI][GearDisplay][btns][CHECK] connected={Connected} internalVisible={InternalVisible} finalVisible={FinalVisible}",
//                 connected,
//                 _internalVisible,
//                 FinalVisible);
            ;
        if (connected != _internalVisible)
        {
            _internalVisible = connected;
            HandleVisibleChanged();
            if (Parent != null)
                Parent.ChildStateChanged(this);
        }
    }

    protected virtual void HandlePositionChanged() => SCERenderContext.Instance.UpdatePosition(this);
    protected virtual void HandleSizeChanged() => SCERenderContext.Instance.UpdateSize(this);
    protected virtual void HandleScaleChanged() { if (NativeObject != null) SCERenderContext.Instance.Adapter?.SetScale(NativeObject, _scaleX, _scaleY); }
    protected virtual void HandleRotationChanged() { if (NativeObject != null) SCERenderContext.Instance.Adapter?.SetRotation(NativeObject, _rotation); }
    protected virtual void HandleAlphaChanged() => SCERenderContext.Instance.UpdateAlpha(this);
    protected virtual void HandleVisibleChanged()
    {
        if (NativeObject != null)
        {
            SCERenderContext.Instance.Adapter?.SetVisible(NativeObject, _visible && _internalVisible);
            return;
        }

        // If a child becomes visible after being attached while invisible,
        // ensure parent materializes native control for this node.
        if (Parent != null && FinalVisible)
        {
            Parent.ChildStateChanged(this);
        }
    }
    protected virtual void HandleTouchableChanged() { if (NativeObject != null) SCERenderContext.Instance.Adapter?.SetTouchable(NativeObject, _touchable); }
    protected virtual void HandleGrayedChanged() { if (NativeObject != null) SCERenderContext.Instance.Adapter?.SetGrayed(NativeObject, _grayed); }

    public void CreateDisplay() { if (NativeObject == null) SCERenderContext.Instance.CreateNativeControl(this); }
    public void AddToStage()
    {
        CreateDisplay();
        UIRuntime.PrepareRootForStage(this);
        SCERenderContext.Instance.AddToRoot(this);
    }
    public void RemoveFromStage() => UIRuntime.RemoveFromRoot(this, dispose: false);

    public virtual void ConstructFromResource() { }

    public virtual void Setup_BeforeAdd(ByteBuffer buffer, int beginPos)
    {
        buffer.Seek(beginPos, 0);
        buffer.Skip(5);
        Id = buffer.ReadS() ?? Id;
        Name = buffer.ReadS() ?? "";
        float f1 = buffer.ReadInt(); float f2 = buffer.ReadInt();
        SetXY(f1, f2);
        if (buffer.ReadBool()) { InitWidth = buffer.ReadInt(); InitHeight = buffer.ReadInt(); SetSize(InitWidth, InitHeight, true); }
        if (buffer.ReadBool()) { MinWidth = buffer.ReadInt(); MaxWidth = buffer.ReadInt(); MinHeight = buffer.ReadInt(); MaxHeight = buffer.ReadInt(); }
        if (buffer.ReadBool()) { f1 = buffer.ReadFloat(); f2 = buffer.ReadFloat(); SetScale(f1, f2); }
        if (buffer.ReadBool()) { buffer.ReadFloat(); buffer.ReadFloat(); } // skew
        if (buffer.ReadBool()) { f1 = buffer.ReadFloat(); f2 = buffer.ReadFloat(); SetPivot(f1, f2, buffer.ReadBool()); }
        f1 = buffer.ReadFloat(); if (f1 != 1) Alpha = f1;
        f1 = buffer.ReadFloat(); if (f1 != 0) Rotation = f1;
        if (!buffer.ReadBool()) Visible = false;
        if (!buffer.ReadBool()) Touchable = false;
        if (buffer.ReadBool()) Grayed = true;
        BlendMode = (BlendMode)buffer.ReadByte();
        int filter = buffer.ReadByte();
        if (filter == 1) { buffer.ReadFloat(); buffer.ReadFloat(); buffer.ReadFloat(); buffer.ReadFloat(); }
        string? str = buffer.ReadS();
        if (str != null) Data = str;
    }

    public virtual void Setup_AfterAdd(ByteBuffer buffer, int beginPos)
    {
        if (buffer.Seek(beginPos, 1))
        {
            buffer.ReadS(); // tooltips

            int groupId = buffer.ReadShort();
            if (groupId >= 0 && Parent != null)
                Group = Parent.GetChildAt(groupId) as GGroup;
        }

        if (!buffer.Seek(beginPos, 2))
            return;

        int cnt = buffer.ReadShort();
        for (int i = 0; i < cnt; i++)
        {
            int nextPos = buffer.ReadUshort();
            nextPos += buffer.Position;

            int gearType = buffer.ReadByte();
            var gear = GetGear(gearType);
            gear.Setup(buffer);

            buffer.Position = nextPos;
        }
    }
    
    public virtual void HandleControllerChanged(Controller controller)
    {
        _handlingController = true;
        
        for (int i = 0; i < 10; i++)
        {
            var gear = _gears[i];
            if (gear != null && gear.Controller == controller)
                gear.Apply();
        }
        
        _handlingController = false;
        CheckGearDisplay();
    }

    // ===== Gesture Support =====
    
    /// <summary>
    /// Enable touch behavior with press animation and optional long press detection
    /// </summary>
    public void EnableTouchBehavior(TouchBehaviorConfig? config = null)
    {
        _touchBehaviorConfig = config ?? TouchBehaviorConfig.Default;
        _touchBehaviorEnabled = true;
        
        if (NativeObject != null)
        {
            SCERenderContext.Instance.Adapter?.EnableTouchBehavior(NativeObject, _touchBehaviorConfig);
        }
    }
    
    /// <summary>
    /// Disable touch behavior
    /// </summary>
    public void DisableTouchBehavior()
    {
        _touchBehaviorEnabled = false;
        _touchBehaviorConfig = null;
        
        if (NativeObject != null)
        {
            SCERenderContext.Instance.Adapter?.DisableTouchBehavior(NativeObject);
        }
    }
    
    /// <summary>
    /// Register a long press handler
    /// </summary>
    public void OnLongPress(Action handler)
    {
        _longPressHandler = handler;
        
        // Ensure touch behavior is enabled
        if (!_touchBehaviorEnabled)
        {
            EnableTouchBehavior();
        }
        
        if (NativeObject != null)
        {
            SCERenderContext.Instance.Adapter?.OnLongPress(NativeObject, handler);
        }
    }
    
    /// <summary>
    /// Register a double click handler
    /// </summary>
    public void OnDoubleClick(Action handler)
    {
        _doubleClickHandler = handler;
        
        if (NativeObject != null)
        {
            SCERenderContext.Instance.Adapter?.OnDoubleClick(NativeObject, handler);
        }
    }
    
    /// <summary>
    /// Enable drag support with swipe detection
    /// </summary>
    private void EnableDrag()
    {
        if (_nativeObject == null) return;
        if (_dragEnabled) return;  // 防止重复设置
        
        var adapter = SCERenderContext.Instance.Adapter;
        if (adapter == null) return;
        
        _dragEnabled = true;
        
        float startObjX = 0, startObjY = 0;  // 对象起始位置
        float startTouchX = 0, startTouchY = 0;  // 触摸起始位置
        float lastTouchX = 0, lastTouchY = 0;
        float scaleFactor = UIRuntime.ContentScaleFactor;
        
        // 使用带位置的按下事件
        adapter.OnPointerPressWithPosition(_nativeObject, (pressX, pressY) =>
        {
            // 记录对象起始位置和触摸起始位置
            startObjX = _x;
            startObjY = _y;
            startTouchX = pressX;
            startTouchY = pressY;
            lastTouchX = pressX;
            lastTouchY = pressY;
            
            // 先触发拖拽开始事件，让用户有机会调用PreventDefault()
            var ctx = new EventContext { Sender = this, Type = "onDragStart" };
            DispatchEventWithContext("onDragStart", ctx, null);
            
            // 如果用户没有阻止默认行为，才开始拖拽
            if (!ctx.DefaultPrevented)
            {
                adapter.CapturePointer(_nativeObject);
                _isPointerCaptured = true;
//                 Game.Logger.LogInformation($"[FGUI] Drag started: obj=({startObjX},{startObjY}), touch=({pressX},{pressY})");
            }
            else
            {
//                 Game.Logger.LogInformation($"[FGUI] Drag prevented by user");
            }
        });
        
        adapter.OnPointerCapturedMove(_nativeObject, (moveX, moveY) =>
        {
            if (_isPointerCaptured && _draggable)
            {
                lastTouchX = moveX;
                lastTouchY = moveY;

                // DragDropManager agent mode: move the drag agent instead of source object.
                if (DragDropManager.IsDragging && ReferenceEquals(DragDropManager.Source, this))
                {
                    DragDropManager.OnDragMove(new PointF(moveX, moveY));
                    return;
                }

                // 计算触摸位移（需要考虑缩放因子，因为触摸坐标是屏幕坐标）
                float deltaX = (moveX - startTouchX) / scaleFactor;
                float deltaY = (moveY - startTouchY) / scaleFactor;
                
                // 新位置 = 起始位置 + 位移
                float newX = startObjX + deltaX;
                float newY = startObjY + deltaY;
                
                SetPosition(newX, newY, _z);
                
                // 触发拖拽移动事件
                DispatchEvent("onDragMove", null);
                _swipeHandler?.Invoke(deltaX, deltaY);
            }
        });
        
        adapter.OnPointerRelease(_nativeObject, () =>
        {
            if (_isPointerCaptured)
            {
                DragDropManager.OnDragMove(new PointF(lastTouchX, lastTouchY));
                adapter.ReleasePointer(_nativeObject);
                _isPointerCaptured = false;
                
                // 触发拖拽结束事件
                DispatchEvent("onDragEnd", null);
//                 Game.Logger.LogInformation($"[FGUI] Drag ended: obj=({_x},{_y})");
            }
        });
    }
    
    /// <summary>
    /// Register a swipe/drag move handler
    /// </summary>
    public void OnSwipe(Action<float, float> handler)
    {
        _swipeHandler = handler;
        
        // Enable draggable to set up pointer capture
        if (!_draggable)
        {
            _draggable = true;
            EnableDrag();
        }
    }
    
    /// <summary>
    /// Start pointer capture manually (for custom drag implementations)
    /// </summary>
    public void StartDrag()
    {
        if (NativeObject != null && !_isPointerCaptured)
        {
            SCERenderContext.Instance.Adapter?.CapturePointer(NativeObject);
            _isPointerCaptured = true;
        }
    }
    
    /// <summary>
    /// Stop pointer capture
    /// </summary>
    public void StopDrag()
    {
        if (NativeObject != null && _isPointerCaptured)
        {
            SCERenderContext.Instance.Adapter?.ReleasePointer(NativeObject);
            _isPointerCaptured = false;
        }
    }

    public virtual void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        RemoveFromParent();
        RemoveAllEventListeners();
        for (int i = 0; i < 10; i++)
            _gears[i]?.Dispose();
        SCERenderContext.Instance.DisposeNative(this);
        Data = null;
    }
}
#endif


