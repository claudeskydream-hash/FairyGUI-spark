#if CLIENT
using System.Drawing;
using FairyGUI;
using FairyGUI.Utils;

namespace FairyGUI;

public class GLoader : GObject
{
    private string _url = "";
    private AlignType _align = AlignType.Left;
    private VertAlignType _verticalAlign = VertAlignType.Top;
    private FillType _fill = FillType.None;
    private bool _shrinkOnly;
    private bool _autoSize;
    private bool _useResize;
    private bool _updatingLayout;
    private bool _contentPlaying = true;
    private int _contentFrame;
    private GObject? _content;
    private bool _contentAttached;
    private string? _externalResolvedPath;

    public string Url { get => _url; set { if (_url != value) { _url = value; LoadContent(); } } }
    public override string? Icon { get => _url; set => Url = value ?? ""; }
    public AlignType Align { get => _align; set => _align = value; }
    public VertAlignType VerticalAlign { get => _verticalAlign; set => _verticalAlign = value; }
    public FillType Fill { get => _fill; set => _fill = value; }
    public bool ShrinkOnly { get => _shrinkOnly; set => _shrinkOnly = value; }
    public bool AutoSize { get => _autoSize; set => _autoSize = value; }
    public GObject? Content => _content;

    private void LoadContent()
    {
        if (string.IsNullOrEmpty(_url)) { ClearContent(); return; }
        if (_url.StartsWith(UIPackage.URL_PREFIX))
        {
            var item = UIPackage.GetItemByURL(_url);
            if (item != null) LoadFromPackage(item);
            else LoadExternal();
        }
        else
        {
            LoadExternal();
        }
    }

    private void LoadFromPackage(PackageItem item)
    {
        SourceWidth = item.Width;
        SourceHeight = item.Height;

        if (item.Type == PackageItemType.Image)
        {
            var image = new GImage { PackageItem = item };
            image.ConstructFromResource();
            SetContent(image);
        }
        else if (item.Type == PackageItemType.MovieClip)
        {
            var clip = new GMovieClip { PackageItem = item };
            clip.ConstructFromResource();
            clip.Playing = _contentPlaying;
            clip.Frame = _contentFrame;
            SetContent(clip);
        }
        else if (item.Type == PackageItemType.Component)
        {
            var obj = item.Owner?.CreateObject(item);
            if (obj != null)
            {
                SetContent(obj);
            }
        }
    }

    /// <summary>
    /// Override this to implement custom external loading
    /// </summary>
    protected virtual void LoadExternal()
    {
        var resolved = ResolveExternalImagePath(_url);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            OnExternalLoadFailed(_url, resolved);
            return;
        }

        var image = new GImage();
        var fallbackWidth = ResolveExternalFallbackWidth();
        var fallbackHeight = ResolveExternalFallbackHeight();
        image.SourceWidth = (int)MathF.Max(1, fallbackWidth);
        image.SourceHeight = (int)MathF.Max(1, fallbackHeight);
        image.InitWidth = image.SourceWidth;
        image.InitHeight = image.SourceHeight;
        image.SetSize(fallbackWidth, fallbackHeight, true);
        image.Icon = resolved;

        OnExternalLoadSuccess(image, resolved);
    }

    protected virtual void OnExternalLoadSuccess(GImage image, string resolvedPath)
    {
        SourceWidth = image.SourceWidth;
        SourceHeight = image.SourceHeight;
        _externalResolvedPath = resolvedPath;
        SetContent(image);
//         Game.Logger.LogInformation("[FGUI][Loader] external load success url={Url} resolved={Resolved}", _url, resolvedPath);
    }

    protected virtual void OnExternalLoadFailed(string url, string? resolvedPath)
    {
        Game.Logger.LogWarning("[FGUI][Loader] external load failed url={Url} resolved={Resolved}", url, resolvedPath);
        ClearContent();
    }

    private float ResolveExternalFallbackWidth()
    {
        if (SourceWidth > 0) return SourceWidth;
        if (InitWidth > 0) return InitWidth;
        if (Width > 0) return Width;
        return 50;
    }

    private float ResolveExternalFallbackHeight()
    {
        if (SourceHeight > 0) return SourceHeight;
        if (InitHeight > 0) return InitHeight;
        if (Height > 0) return Height;
        return 30;
    }

    private void ClearNativeBackgroundImage()
    {
        if (NativeObject == null)
            return;

        Render.SCERenderContext.Instance.Adapter?.SetBackgroundImage(NativeObject, string.Empty);
    }

    /// <summary>
    /// Override this to free external resources
    /// </summary>
    protected virtual void FreeExternal()
    {
        if (string.IsNullOrWhiteSpace(_externalResolvedPath))
            return;

        ClearNativeBackgroundImage();
        _externalResolvedPath = null;
    }

    protected void SetContent(GObject obj)
    {
        ClearContent();
        _content = obj;
        _contentAttached = false;
        ClearNativeBackgroundImage();
        UpdateLayout();
        SyncNativeContent();
    }
    
    private void ClearContent()
    {
        if (_content != null)
        {
            DetachContentFromNative();
            FreeExternal();
            _content.Dispose(); 
            _content = null; 
            _contentAttached = false;
        }
        else
        {
            FreeExternal();
        }
    }
    
    private void UpdateLayout()
    {
        if (_content == null || Disposed)
        {
            if (_autoSize && !_updatingLayout)
            {
                _updatingLayout = true;
                SetSize(50, 30);
                _updatingLayout = false;
            }
            return;
        }

        var sourceWidth = ResolveSourceWidth();
        var sourceHeight = ResolveSourceHeight();

        if (_autoSize)
        {
            _updatingLayout = true;
            if (sourceWidth <= 0)
                sourceWidth = 50;
            if (sourceHeight <= 0)
                sourceHeight = 30;

            SetSize(sourceWidth, sourceHeight);
            _updatingLayout = false;

            if (MathF.Abs(Width - sourceWidth) < 0.01f && MathF.Abs(Height - sourceHeight) < 0.01f)
            {
                ApplyContentLayout(0, 0, sourceWidth, sourceHeight, 1, 1);
                return;
            }
        }

        if (sourceWidth <= 0)
            sourceWidth = Width > 0 ? Width : 50;
        if (sourceHeight <= 0)
            sourceHeight = Height > 0 ? Height : 30;

        var sx = 1f;
        var sy = 1f;
        var contentWidth = sourceWidth;
        var contentHeight = sourceHeight;
        if (_fill != FillType.None)
        {
            sx = Width / sourceWidth;
            sy = Height / sourceHeight;

            if (MathF.Abs(sx - 1f) > 0.0001f || MathF.Abs(sy - 1f) > 0.0001f)
            {
                switch (_fill)
                {
                    case FillType.ScaleMatchHeight:
                        sx = sy;
                        break;
                    case FillType.ScaleMatchWidth:
                        sy = sx;
                        break;
                    case FillType.Scale:
                        if (sx > sy) sx = sy;
                        else sy = sx;
                        break;
                    case FillType.ScaleNoBorder:
                        if (sx > sy) sy = sx;
                        else sx = sy;
                        break;
                    case FillType.ScaleFree:
                        break;
                }

                if (_shrinkOnly)
                {
                    if (sx > 1f) sx = 1f;
                    if (sy > 1f) sy = 1f;
                }
            }

            contentWidth = sourceWidth * sx;
            contentHeight = sourceHeight * sy;
        }

        var nx = _align switch
        {
            AlignType.Center => (Width - contentWidth) * 0.5f,
            AlignType.Right => Width - contentWidth,
            _ => 0f
        };
        var ny = _verticalAlign switch
        {
            VertAlignType.Middle => (Height - contentHeight) * 0.5f,
            VertAlignType.Bottom => Height - contentHeight,
            _ => 0f
        };

        ApplyContentLayout(nx, ny, contentWidth, contentHeight, sx, sy);
    }

    private float ResolveSourceWidth()
    {
        if (SourceWidth > 0)
            return SourceWidth;

        if (_content == null)
            return 0;

        if (_content.SourceWidth > 0)
            return _content.SourceWidth;

        if (_content.InitWidth > 0)
            return _content.InitWidth;

        return _content.Width;
    }

    private float ResolveSourceHeight()
    {
        if (SourceHeight > 0)
            return SourceHeight;

        if (_content == null)
            return 0;

        if (_content.SourceHeight > 0)
            return _content.SourceHeight;

        if (_content.InitHeight > 0)
            return _content.InitHeight;

        return _content.Height;
    }

    private void ApplyContentLayout(float nx, float ny, float contentWidth, float contentHeight, float sx, float sy)
    {
        if (_content == null)
            return;

        if (_content is GComponent component)
        {
            if (_useResize)
            {
                component.SetScale(1, 1);
                component.SetSize(contentWidth, contentHeight, true);
            }
            else
            {
                component.SetScale(sx, sy);
            }
        }
        else
        {
            _content.SetScale(1, 1);
            _content.SetSize(contentWidth, contentHeight, true);
        }

        _content.SetXY(nx, ny);
    }

    internal void SyncNativeContent()
    {
        if (NativeObject == null)
            return;

        if (_content == null)
            return;

        var adapter = Render.SCERenderContext.Instance.Adapter;
        if (adapter == null)
            return;

        if (_content.NativeObject == null)
            Render.SCERenderContext.Instance.CreateNativeControl(_content);

        if (_content.NativeObject == null)
            return;

        if (!_contentAttached)
        {
            adapter.AddChild(NativeObject, _content.NativeObject);
            _contentAttached = true;
        }

        _content.SetXY(0, 0);
        UpdateLayout();
        adapter.SetVisible(_content.NativeObject, _visible && _internalVisible);
    }

    private void DetachContentFromNative()
    {
        if (!_contentAttached || _content?.NativeObject == null || NativeObject == null)
            return;

        Render.SCERenderContext.Instance.Adapter?.RemoveChild(NativeObject, _content.NativeObject);
        _contentAttached = false;
    }

    protected override void HandleVisibleChanged()
    {
        base.HandleVisibleChanged();
        SyncNativeContent();
    }

    protected override void HandleSizeChanged()
    {
        base.HandleSizeChanged();
        if (!_updatingLayout)
            SyncNativeContent();
    }

    private static string? ResolveExternalImagePath(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var normalized = url.Replace('\\', '/').Trim();
        if (normalized.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return normalized;

        if (normalized.StartsWith("/images/", StringComparison.OrdinalIgnoreCase))
            return "image/fgui/" + normalized.Substring("/images/".Length);

        if (normalized.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
            return "image/fgui/" + normalized.Substring("images/".Length);

        return normalized.TrimStart('/');
    }

    public override void Setup_BeforeAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_BeforeAdd(buffer, beginPos);
        buffer.Seek(beginPos, 5);
        _url = buffer.ReadS() ?? "";
        _align = (AlignType)buffer.ReadByte();
        _verticalAlign = (VertAlignType)buffer.ReadByte();
        _fill = (FillType)buffer.ReadByte();
        _shrinkOnly = buffer.ReadBool();
        _autoSize = buffer.ReadBool();
        buffer.ReadBool(); // showErrorSign
        _contentPlaying = buffer.ReadBool();
        _contentFrame = buffer.ReadInt();
        if (buffer.ReadBool())
            buffer.ReadColor();
        var fillMethod = (FillMethod)buffer.ReadByte();
        if (fillMethod != FillMethod.None)
        {
            buffer.ReadByte(); // fillOrigin
            buffer.ReadBool(); // fillClockwise
            buffer.ReadFloat(); // fillAmount
        }
        if (buffer.Version >= 7)
            _useResize = buffer.ReadBool();

        if (!string.IsNullOrEmpty(_url)) LoadContent();
    }

    public override void Dispose() { ClearContent(); base.Dispose(); }
}

public class GGroup : GObject
{
    private GroupLayoutType _layout = GroupLayoutType.None;
    private int _lineGap, _columnGap;
    private bool _excludeInvisibles, _autoSizeDisabled;
    private bool _boundsChanged;
    private bool _movingChildren;

    public GGroup()
    {
        // Group is logical-only; it should never capture pointer input.
        Touchable = false;
    }

    public GroupLayoutType Layout { get => _layout; set => _layout = value; }
    public int LineGap { get => _lineGap; set { _lineGap = value; SetBoundsChangedFlag(); } }
    public int ColumnGap { get => _columnGap; set { _columnGap = value; SetBoundsChangedFlag(); } }
    public bool ExcludeInvisibles { get => _excludeInvisibles; set => _excludeInvisibles = value; }
    public void SetBoundsChangedFlag(bool positionChanged = false) => _boundsChanged = true;

    protected override void HandleVisibleChanged()
    {
        base.HandleVisibleChanged();
        // When Group visibility changes, notify all children that belong to this group
        if (Parent != null)
        {
            int cnt = Parent.NumChildren;
            for (int i = 0; i < cnt; i++)
            {
                var child = Parent.GetChildAt(i);
                if (child.Group == this)
                    Parent.ChildStateChanged(child);
            }
        }
    }

    public override void SetPosition(float xv, float yv, float zv)
    {
        if (_movingChildren)
        {
            base.SetPosition(xv, yv, zv);
            return;
        }

        var dx = xv - _x;
        var dy = yv - _y;
        base.SetPosition(xv, yv, zv);

        if (Parent == null)
        {
            return;
        }

        if (MathF.Abs(dx) <= 0.001f && MathF.Abs(dy) <= 0.001f)
        {
            return;
        }

        _movingChildren = true;
        try
        {
            var cnt = Parent.NumChildren;
            for (var i = 0; i < cnt; i++)
            {
                var child = Parent.GetChildAt(i);
                if (child.Group == this)
                {
                    child.SetXY(child.X + dx, child.Y + dy);
                }
            }
        }
        finally
        {
            _movingChildren = false;
        }
    }

    public override void Setup_BeforeAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_BeforeAdd(buffer, beginPos);
        buffer.Seek(beginPos, 5);
        _layout = (GroupLayoutType)buffer.ReadByte();
        _lineGap = buffer.ReadInt();
        _columnGap = buffer.ReadInt();
        if (buffer.Version >= 2) { _excludeInvisibles = buffer.ReadBool(); _autoSizeDisabled = buffer.ReadBool(); buffer.ReadShort(); }
    }
}

public class GProgressBar : GComponent
{
    private const float MinRenderableBarWidth = 1f;
    private const float MinRenderableBarHeight = 1f;
    private float _min, _max = 100, _value;
    private ProgressTitleType _titleType = ProgressTitleType.Percent;
    private bool _reverse;
    private GObject? _titleObject;
    private GMovieClip? _aniObject;
    private GObject? _barObjectH;
    private GObject? _barObjectV;
    private float _barMaxWidth;
    private float _barMaxHeight;
    private float _barMaxWidthDelta;
    private float _barMaxHeightDelta;
    private float _barStartX;
    private float _barStartY;

    public float Min { get => _min; set { _min = value; Update(); } }
    public float Max { get => _max; set { _max = value; Update(); } }
    public float Value { get => _value; set { _value = Math.Clamp(value, _min, _max); Update(); } }
    public double Percent => _max > _min ? (_value - _min) / (_max - _min) : 0;
    public ProgressTitleType TitleType { get => _titleType; set { _titleType = value; Update(); } }
    public bool Reverse { get => _reverse; set { _reverse = value; Update(); } }

    public override void ConstructFromResource()
    {
        base.ConstructFromResource();
        _titleObject = GetChild("title");
        _barObjectH = GetChild("bar");
        _barObjectV = GetChild("bar_v");
        _aniObject = GetChild("ani") as GMovieClip;
        if (_barObjectH != null)
        {
            _barMaxWidth = _barObjectH.Width;
            _barMaxWidthDelta = Width - _barMaxWidth;
            _barStartX = _barObjectH.X;
        }

        if (_barObjectV != null)
        {
            _barMaxHeight = _barObjectV.Height;
            _barMaxHeightDelta = Height - _barMaxHeight;
            _barStartY = _barObjectV.Y;
        }
        
        var buffer = PackageItem?.RawData;
        if (buffer != null)
        {
            buffer.Seek(0, 6);
            _titleType = (ProgressTitleType)buffer.ReadByte();
            _reverse = buffer.ReadBool();
        }
    }

    public override void Setup_AfterAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_AfterAdd(buffer, beginPos);
        if (!buffer.Seek(beginPos, 6)) return;
        if ((ObjectType)buffer.ReadByte() != PackageItem?.ObjectType) return;
        _value = buffer.ReadInt();
        _max = buffer.ReadInt();
        if (buffer.Version >= 2 && buffer.Position + 4 <= buffer.Length)
            _min = buffer.ReadInt();
        Update();
    }

    private void Update()
    {
        var percent = Math.Clamp((float)Percent, 0f, 1f);
        if (_titleObject != null)
        {
            _titleObject.Text = _titleType switch
            {
                ProgressTitleType.Percent => $"{(int)(percent * 100)}%",
                ProgressTitleType.ValueAndMax => $"{(int)_value}/{(int)_max}",
                ProgressTitleType.Value => $"{(int)_value}",
                ProgressTitleType.Max => $"{(int)_max}",
                _ => ""
            };
        }

        var fullWidth = Math.Max(0f, Width - _barMaxWidthDelta);
        var fullHeight = Math.Max(0f, Height - _barMaxHeightDelta);

        if (!_reverse)
        {
            if (_barObjectH != null)
            {
                if (!SetFillAmount(_barObjectH, percent))
                {
                    var barWidth = MathF.Round(fullWidth * percent);
                    if (fullWidth > 0f && barWidth <= 0f)
                        barWidth = MathF.Min(MinRenderableBarWidth, fullWidth);
                    _barObjectH.Width = Math.Clamp(barWidth, 0f, fullWidth);
                    _barObjectH.X = _barStartX;
                }
            }

            if (_barObjectV != null)
            {
                if (!SetFillAmount(_barObjectV, percent))
                {
                    var barHeight = MathF.Round(fullHeight * percent);
                    if (fullHeight > 0f && barHeight <= 0f)
                        barHeight = MathF.Min(MinRenderableBarHeight, fullHeight);
                    _barObjectV.Height = Math.Clamp(barHeight, 0f, fullHeight);
                    _barObjectV.Y = _barStartY;
                }
            }
        }
        else
        {
            if (_barObjectH != null)
            {
                if (!SetFillAmount(_barObjectH, 1f - percent))
                {
                    var barWidth = MathF.Round(fullWidth * percent);
                    if (fullWidth > 0f && barWidth <= 0f)
                        barWidth = MathF.Min(MinRenderableBarWidth, fullWidth);
                    _barObjectH.Width = Math.Clamp(barWidth, 0f, fullWidth);
                    _barObjectH.X = _barStartX + (fullWidth - _barObjectH.Width);
                }
            }

            if (_barObjectV != null)
            {
                if (!SetFillAmount(_barObjectV, 1f - percent))
                {
                    var barHeight = MathF.Round(fullHeight * percent);
                    if (fullHeight > 0f && barHeight <= 0f)
                        barHeight = MathF.Min(MinRenderableBarHeight, fullHeight);
                    _barObjectV.Height = Math.Clamp(barHeight, 0f, fullHeight);
                    _barObjectV.Y = _barStartY + (fullHeight - _barObjectV.Height);
                }
            }
        }

        if (_aniObject != null)
        {
            _aniObject.Frame = Math.Clamp((int)MathF.Round(percent * 100f), 0, Math.Max(0, _aniObject.FrameCount - 1));
        }
    }

    private static bool SetFillAmount(GObject barObject, float amount)
    {
        if (barObject is GImage image && image.FillMethod != FillMethod.None)
        {
            image.FillAmount = amount;
            return true;
        }

        return false;
    }

    protected override void HandleSizeChanged()
    {
        base.HandleSizeChanged();

        if (_barObjectH != null)
            _barMaxWidth = Width - _barMaxWidthDelta;
        if (_barObjectV != null)
            _barMaxHeight = Height - _barMaxHeightDelta;

        if (!UnderConstruct)
            Update();
    }
}

public class GSlider : GComponent
{
    private const bool EnableSliderDiagLogs = true;
    private const int SliderDiagLogLimit = 160;
    private static int _sliderDiagLogCount;
    private const float MinRenderableBarWidth = 1f;
    private float _min, _max = 100, _value = 50;
    private bool _wholeNumbers;
    private bool _reverse;
    private bool _changeOnClick = true;
    private bool _dragging;
    private GObject? _titleObject;
    private GObject? _barObjectH;
    private GObject? _barObjectV;
    private GObject? _gripObject;
    private ProgressTitleType _titleType;
    private float _barMaxWidth;
    private float _barMaxHeight;
    private float _barMaxWidthDelta;
    private float _barMaxHeightDelta;
    private float _barStartX;
    private float _barStartY;
    private PointF _clickPos;
    private float _clickPercent;
    private float _gripOffsetXFromBarEdge;
    private float _gripOffsetYFromBarEdge;

    public float Min { get => _min; set { _min = value; Update(); } }
    public float Max { get => _max; set { _max = value; Update(); } }
    public float Value { get => _value; set { _value = Math.Clamp(value, _min, _max); if (_wholeNumbers) _value = MathF.Round(_value); Update(); DispatchEvent("onChanged", null); } }
    public bool WholeNumbers { get => _wholeNumbers; set => _wholeNumbers = value; }

    public override void ConstructFromResource()
    {
        base.ConstructFromResource();
        _titleObject = GetChild("title");
        _barObjectH = GetChild("bar");
        _barObjectV = GetChild("bar_v");
        _gripObject = GetChild("grip");

        if (_barObjectH != null)
        {
            _barMaxWidth = _barObjectH.Width;
            _barMaxWidthDelta = Width - _barMaxWidth;
            _barStartX = _barObjectH.X;
        }

        if (_barObjectV != null)
        {
            _barMaxHeight = _barObjectV.Height;
            _barMaxHeightDelta = Height - _barMaxHeight;
            _barStartY = _barObjectV.Y;
        }

        // SCE 侧按“bar + grip”组合语义实现 slider：
        // grip 的坐标跟随 bar 已绘制长度边缘，支持拖拽与点击跳转。
        if (_gripObject != null)
        {
            _gripObject.OnTouchBegin.Add(OnGripTouchBegin);
            _gripObject.OnTouchMove.Add(OnGripTouchMove);
            _gripObject.OnTouchEnd.Add(OnGripTouchEnd);
        }
        OnTouchBegin.Add(OnBarTouchBegin);
        LogSliderDiag(
            "[FGUI][SLIDER][BIND] name={Name} hasGrip={HasGrip} hasBarH={HasBarH} hasBarV={HasBarV} changeOnClick={ChangeOnClick}",
            Name,
            _gripObject != null,
            _barObjectH != null,
            _barObjectV != null,
            _changeOnClick);

        var buffer = PackageItem?.RawData;
        if (buffer != null && buffer.Seek(0, 6))
        {
            _titleType = (ProgressTitleType)buffer.ReadByte();
            _reverse = buffer.ReadBool();
            if (buffer.Version >= 2)
            {
                _wholeNumbers = buffer.ReadBool();
                if (buffer.Position < buffer.Length)
                    _changeOnClick = buffer.ReadBool();
            }
        }

        if (_gripObject != null)
        {
            if (_barObjectH != null)
            {
                var initialHorizontalEdge = _reverse ? _barObjectH.X : (_barObjectH.X + _barObjectH.Width);
                _gripOffsetXFromBarEdge = _gripObject.X - initialHorizontalEdge;
            }

            if (_barObjectV != null)
            {
                var initialVerticalEdge = _reverse ? _barObjectV.Y : (_barObjectV.Y + _barObjectV.Height);
                _gripOffsetYFromBarEdge = _gripObject.Y - initialVerticalEdge;
            }
        }

        Update();
    }

    public override void Setup_AfterAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_AfterAdd(buffer, beginPos);
        if (!buffer.Seek(beginPos, 6)) return;
        if ((ObjectType)buffer.ReadByte() != PackageItem?.ObjectType) return;
        _value = buffer.ReadInt();
        _max = buffer.ReadInt();
        if (buffer.Version >= 2 && buffer.Position + 4 <= buffer.Length)
            _min = buffer.ReadInt();
        Update();
    }

    protected override void HandleSizeChanged()
    {
        base.HandleSizeChanged();
        if (_barObjectH != null)
            _barMaxWidth = Width - _barMaxWidthDelta;
        if (_barObjectV != null)
            _barMaxHeight = Height - _barMaxHeightDelta;

        if (!UnderConstruct)
            Update();
    }

    private void Update()
    {
        UpdateWithPercent(_max > _min ? (_value - _min) / (_max - _min) : 0f, false);
    }

    private void UpdateWithPercent(float percent, bool manual)
    {
        percent = Math.Clamp(percent, 0f, 1f);
        if (manual)
        {
            var newValue = _min + (_max - _min) * percent;
            newValue = Math.Clamp(newValue, _min, _max);
            if (_wholeNumbers)
            {
                newValue = MathF.Round(newValue);
                percent = _max > _min ? (newValue - _min) / (_max - _min) : 0f;
            }

            if (MathF.Abs(newValue - _value) > 0.001f)
            {
                _value = newValue;
                DispatchEvent("onChanged", null);
            }
        }

        if (_titleObject != null)
            _titleObject.Text = _titleType switch
            {
                ProgressTitleType.Percent => $"{(int)MathF.Floor(percent * 100)}%",
                ProgressTitleType.ValueAndMax => $"{(int)MathF.Round(_value)}/{(int)MathF.Round(_max)}",
                ProgressTitleType.Value => $"{(int)MathF.Round(_value)}",
                ProgressTitleType.Max => $"{(int)MathF.Round(_max)}",
                _ => $"{(int)MathF.Floor(percent * 100)}%"
            };

        var fullWidth = MathF.Max(0f, Width - _barMaxWidthDelta);
        var fullHeight = MathF.Max(0f, Height - _barMaxHeightDelta);
        var horizontalEdge = _barStartX;
        var verticalEdge = _barStartY;

        if (_barObjectH != null)
        {
            float barWidth = fullWidth * percent;
            if (fullWidth > 0 && barWidth <= 0)
                barWidth = MathF.Min(MinRenderableBarWidth, fullWidth);
            barWidth = Math.Clamp(barWidth, 0f, fullWidth);

            if (_reverse)
            {
                _barObjectH.X = _barStartX + (fullWidth - barWidth);
            }
            else
            {
                _barObjectH.X = _barStartX;
            }

            _barObjectH.Width = barWidth;
            horizontalEdge = _reverse ? _barObjectH.X : (_barObjectH.X + barWidth);
        }

        if (_barObjectV != null)
        {
            float barHeight = fullHeight * percent;
            if (fullHeight > 0 && barHeight <= 0)
                barHeight = MathF.Min(MinRenderableBarWidth, fullHeight);
            barHeight = Math.Clamp(barHeight, 0f, fullHeight);

            if (_reverse)
            {
                _barObjectV.Y = _barStartY + (fullHeight - barHeight);
            }
            else
            {
                _barObjectV.Y = _barStartY;
            }

            _barObjectV.Height = barHeight;
            verticalEdge = _reverse ? _barObjectV.Y : (_barObjectV.Y + barHeight);
        }

        if (_gripObject != null)
        {
            if (_barObjectH != null)
                _gripObject.X = horizontalEdge + _gripOffsetXFromBarEdge;
            if (_barObjectV != null)
                _gripObject.Y = verticalEdge + _gripOffsetYFromBarEdge;
        }
    }

    private void OnGripTouchBegin(EventContext ctx)
    {
        if (!TryGetPointerPosition(ctx, out var point))
        {
            LogSliderDiag("[FGUI][SLIDER][BEGIN] name={Name} skip=no-pointer", Name);
            return;
        }

        _dragging = true;
        _clickPos = ScreenToLocal(point);
        _clickPercent = _max > _min ? (_value - _min) / (_max - _min) : 0f;
        LogSliderDiag(
            "[FGUI][SLIDER][BEGIN] name={Name} raw={RawX:0.##},{RawY:0.##} local={LocalX:0.##},{LocalY:0.##} clickPercent={ClickPercent:0.###} bar={BarW:0.##}x{BarH:0.##}",
            Name,
            point.X,
            point.Y,
            _clickPos.X,
            _clickPos.Y,
            _clickPercent,
            _barMaxWidth,
            _barMaxHeight);
        ctx.StopPropagation();
    }

    private void OnGripTouchMove(EventContext ctx)
    {
        if (!_dragging)
        {
            LogSliderDiag("[FGUI][SLIDER][MOVE] name={Name} skip=not-dragging", Name);
            return;
        }

        if (!TryGetPointerPosition(ctx, out var point))
        {
            LogSliderDiag("[FGUI][SLIDER][MOVE] name={Name} skip=no-pointer", Name);
            return;
        }

        var pt = ScreenToLocal(point);
        float deltaX = pt.X - _clickPos.X;
        float deltaY = pt.Y - _clickPos.Y;
        if (_reverse)
        {
            deltaX = -deltaX;
            deltaY = -deltaY;
        }

        float percent = _clickPercent;
        if (_barObjectH != null && _barMaxWidth > 0)
            percent += deltaX / _barMaxWidth;
        else if (_barObjectV != null && _barMaxHeight > 0)
            percent += deltaY / _barMaxHeight;

        LogSliderDiag(
            "[FGUI][SLIDER][MOVE] name={Name} raw={RawX:0.##},{RawY:0.##} local={LocalX:0.##},{LocalY:0.##} delta={DX:0.##},{DY:0.##} percent={Percent:0.###}",
            Name,
            point.X,
            point.Y,
            pt.X,
            pt.Y,
            deltaX,
            deltaY,
            percent);
        UpdateWithPercent(percent, true);
    }

    private void OnGripTouchEnd(EventContext ctx)
    {
        LogSliderDiag("[FGUI][SLIDER][END] name={Name} value={Value:0.###}", Name, _value);
        _dragging = false;
        DispatchEvent("onGripTouchEnd", null);
    }

    private void OnBarTouchBegin(EventContext ctx)
    {
        if (!_changeOnClick || _gripObject == null || !TryGetPointerPosition(ctx, out var point))
        {
            LogSliderDiag(
                "[FGUI][SLIDER][BAR] name={Name} skip changeOnClick={ChangeOnClick} hasGrip={HasGrip}",
                Name,
                _changeOnClick,
                _gripObject != null);
            return;
        }

        var local = ScreenToLocal(point);
        float percent = _max > _min ? (_value - _min) / (_max - _min) : 0f;
        if (_barObjectH != null && _barMaxWidth > 0)
        {
            var localPos = (local.X - _barStartX) / _barMaxWidth;
            percent = _reverse ? (1f - localPos) : localPos;
        }
        else if (_barObjectV != null && _barMaxHeight > 0)
        {
            var localPos = (local.Y - _barStartY) / _barMaxHeight;
            percent = _reverse ? (1f - localPos) : localPos;
        }

        LogSliderDiag(
            "[FGUI][SLIDER][BAR] name={Name} raw={RawX:0.##},{RawY:0.##} local={LocalX:0.##},{LocalY:0.##} percent={Percent:0.###}",
            Name,
            point.X,
            point.Y,
            local.X,
            local.Y,
            percent);
        UpdateWithPercent(percent, true);
    }

    private PointF ScreenToLocal(PointF screenPoint)
    {
        var scale = UIRuntime.ContentScaleFactor;
        if (scale <= 0)
            scale = 1f;

        var globalX = X;
        var globalY = Y;
        var p = Parent;
        while (p != null)
        {
            globalX += p.X;
            globalY += p.Y;
            p = p.Parent;
        }

        return new PointF(screenPoint.X / scale - globalX, screenPoint.Y / scale - globalY);
    }

    private static bool TryGetPointerPosition(EventContext ctx, out PointF point)
    {
        if (ctx.Data is PointF p)
        {
            point = p;
            return true;
        }

        point = default;
        return false;
    }

    private static void LogSliderDiag(string message, params object?[] args)
    {
        if (!EnableSliderDiagLogs || _sliderDiagLogCount >= SliderDiagLogLimit)
        {
            return;
        }

        _sliderDiagLogCount++;
        Game.Logger.LogWarning(message, args);
    }
}

public class GScrollBar : GComponent
{
    private GObject? _grip;
    private GObject? _bar;
    private GObject? _arrowButton1;
    private GObject? _arrowButton2;
    private ScrollPane? _target;
    private bool _vertical;
    private float _scrollPerc;
    private bool _fixedGripSize;
    private bool _gripDragging;
    private float _dragOffset;

    public bool GripDragging => _gripDragging;

    public void SetScrollPane(ScrollPane target, bool vertical)
    {
        _target = target;
        _vertical = vertical;
    }

    public void SetDisplayPerc(float value)
    {
        if (_grip == null || _bar == null) return;
        
        if (_vertical)
        {
            if (!_fixedGripSize)
                _grip.Height = value * _bar.Height;
            _grip.Y = _bar.Y + (_bar.Height - _grip.Height) * _scrollPerc;
        }
        else
        {
            if (!_fixedGripSize)
                _grip.Width = value * _bar.Width;
            _grip.X = _bar.X + (_bar.Width - _grip.Width) * _scrollPerc;
        }
        _grip.Visible = value != 0 && value != 1;
    }

    public void SetScrollPerc(float value)
    {
        _scrollPerc = value;
        if (_grip == null || _bar == null) return;
        
        if (_vertical)
            _grip.Y = _bar.Y + (_bar.Height - _grip.Height) * _scrollPerc;
        else
            _grip.X = _bar.X + (_bar.Width - _grip.Width) * _scrollPerc;
    }

    public float MinSize => _vertical
        ? ((_arrowButton1?.Height ?? 0) + (_arrowButton2?.Height ?? 0))
        : ((_arrowButton1?.Width ?? 0) + (_arrowButton2?.Width ?? 0));

    protected override void ConstructExtension(ByteBuffer buffer)
    {
        buffer.Seek(0, 6);
        _fixedGripSize = buffer.ReadBool();
        
        _grip = GetChild("grip");
        _bar = GetChild("bar");
        _arrowButton1 = GetChild("arrow1");
        _arrowButton2 = GetChild("arrow2");
        
        // SCE暂时没有slider原生控件。
        // GScrollBar 当前不启用拖拽交互，避免与全局指针链路冲突。
        // if (_grip != null)
        // {
        //     _grip.OnTouchBegin.Add(OnGripTouchBegin);
        //     _grip.OnTouchMove.Add(OnGripTouchMove);
        //     _grip.OnTouchEnd.Add(OnGripTouchEnd);
        // }
    }

    private void OnGripTouchBegin(EventContext ctx)
    {
        if (_bar == null || _target == null) return;
        ctx.StopPropagation();
        _gripDragging = true;
    }

    private void OnGripTouchMove(EventContext ctx)
    {
        if (_bar == null || _target == null || _grip == null) return;
        // Touch move handling would require position data from ctx
    }

    private void OnGripTouchEnd(EventContext ctx)
    {
        _gripDragging = false;
    }
}

public class GComboBox : GComponent
{
    private const bool EnableComboDiagLogs = false;
    private const int DropdownCloseDebounceMs = 120;
    private readonly List<string> _items = [];
    private readonly List<string> _values = [];
    private List<string>? _icons;
    private int _selectedIndex = -1;
    private string _title = string.Empty;
    private GObject? _titleObject;
    private GObject? _iconObject;
    private Controller? _buttonController;
    private Controller? _selectionController;
    private GComponent? _dropdown;
    private GList? _list;
    private bool _itemsUpdated = true;
    private bool _over;
    private long _lastDropdownOpenTickMs = -1;
    private long _lastDropdownCloseTickMs = -1;
    private int _visibleItemCount = 10;
    private PopupDirection _popupDirection = PopupDirection.Auto;

    public IList<string> Items => _items;
    public IList<string> Values => _values;
    public IList<string> IconItems => _icons ??= [];
    public int VisibleItemCount { get => _visibleItemCount; set => _visibleItemCount = Math.Max(1, value); }
    public PopupDirection PopupDirection { get => _popupDirection; set => _popupDirection = value; }
    public GComponent? Dropdown => _dropdown;
    public Controller? SelectionController
    {
        get => _selectionController;
        set => _selectionController = value;
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value)
            {
                return;
            }

            if (value < -1 || value >= _items.Count)
            {
                return;
            }

            _selectedIndex = value;
            UpdateCurrentSelectionVisuals();
            UpdateSelectionController();
            DispatchEvent("onChanged", null);
        }
    }

    public string? SelectedValue
    {
        get => _selectedIndex >= 0 && _selectedIndex < _values.Count ? _values[_selectedIndex] : null;
        set
        {
            var search = value ?? string.Empty;
            var index = _values.IndexOf(search);
            if (index < 0 && value == null)
            {
                index = _values.IndexOf(string.Empty);
            }

            if (index < 0 && _items.Count > 0)
            {
                index = 0;
            }

            if (index >= 0)
            {
                SelectedIndex = index;
            }
        }
    }

    public override string? Text
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            if (_titleObject != null)
            {
                _titleObject.Text = _title;
            }

            UpdateGear(6);
        }
    }

    public override string? Icon
    {
        get => _iconObject?.Icon;
        set
        {
            if (_iconObject != null)
            {
                _iconObject.Icon = value;
            }

            UpdateGear(7);
        }
    }

    public override void ConstructFromResource()
    {
        base.ConstructFromResource();

        _buttonController = GetController("button");
        _titleObject = GetChild("title");
        _iconObject = GetChild("icon");

        TryBuildDropdownFromPackage();
        RegisterInteractionHandlers();
    }

    public override void Setup_AfterAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_AfterAdd(buffer, beginPos);
        if (!buffer.Seek(beginPos, 6) || (ObjectType)buffer.ReadByte() != PackageItem?.ObjectType)
        {
            return;
        }

        _items.Clear();
        _values.Clear();
        _icons?.Clear();

        var itemCount = buffer.ReadShort();
        for (var i = 0; i < itemCount; i++)
        {
            var nextPos = buffer.ReadUshort() + buffer.Position;
            var title = buffer.ReadS();
            var value = buffer.ReadS();
            var icon = buffer.ReadS();

            if (!string.IsNullOrEmpty(title))
            {
                _items.Add(title);
                _values.Add(value ?? string.Empty);
                if (icon != null)
                {
                    _icons ??= [];
                    _icons.Add(icon);
                }
                else if (_icons != null)
                {
                    _icons.Add(string.Empty);
                }
            }

            buffer.Position = nextPos;
        }

        var titleText = buffer.ReadS();
        if (titleText != null)
        {
            Text = titleText;
            _selectedIndex = _items.IndexOf(titleText);
        }
        else if (_items.Count > 0)
        {
            _selectedIndex = 0;
            Text = _items[0];
        }
        else
        {
            _selectedIndex = -1;
            Text = string.Empty;
        }

        var iconText = buffer.ReadS();
        if (iconText != null)
        {
            Icon = iconText;
        }

        if (buffer.ReadBool())
        {
            _ = buffer.ReadColor(); // titleColor, current SCE text color pipeline is unified elsewhere.
        }

        var visibleCount = buffer.ReadInt();
        if (visibleCount > 0)
        {
            _visibleItemCount = visibleCount;
        }

        _popupDirection = (PopupDirection)buffer.ReadByte();

        var controllerIndex = buffer.ReadShort();
        if (controllerIndex >= 0 && Parent != null)
        {
            _selectionController = Parent.GetControllerAt(controllerIndex);
        }

        _itemsUpdated = true;
        UpdateCurrentSelectionVisuals();
        UpdateSelectionController();
        if (EnableComboDiagLogs)
        {
            Game.Logger.LogWarning(
                "[FGUI][COMBO] setup name={Name} items={ItemCount} values={ValueCount} selectedIndex={SelectedIndex} visibleItemCount={VisibleItemCount} popupDir={PopupDirection}",
                Name,
                _items.Count,
                _values.Count,
                _selectedIndex,
                _visibleItemCount,
                _popupDirection);
        }
    }

    public override void HandleControllerChanged(Controller controller)
    {
        base.HandleControllerChanged(controller);
        if (_selectionController == controller)
        {
            SelectedIndex = controller.SelectedIndex;
        }
    }

    public override void Dispose()
    {
        if (_list != null)
        {
            _list.OnClickItem.Remove(HandleDropdownItemClick);
        }

        if (_dropdown != null)
        {
            _dropdown.Dispose();
            _dropdown = null;
        }

        _list = null;
        _selectionController = null;
        base.Dispose();
    }

    private void TryBuildDropdownFromPackage()
    {
        var buffer = PackageItem?.RawData;
        if (buffer == null || !buffer.Seek(0, 6))
        {
            return;
        }

        var dropdownUrl = buffer.ReadS();
        if (string.IsNullOrEmpty(dropdownUrl))
        {
            return;
        }

        _dropdown = UIPackage.CreateObjectFromURL(dropdownUrl) as GComponent;
        if (_dropdown == null)
        {
            Game.Logger.LogWarning("[FGUI][ComboBox] dropdown create failed url={Url}", dropdownUrl);
            return;
        }

        _list = _dropdown.GetChild("list") as GList;
        if (_list == null)
        {
            Game.Logger.LogWarning("[FGUI][ComboBox] dropdown missing list url={Url}", dropdownUrl);
            _dropdown.Dispose();
            _dropdown = null;
            return;
        }

        // Match upstream FairyGUI combo relation semantics:
        // list follows dropdown width; dropdown follows list height.
        _list.InitRelations();
        _list.Relations!.Add(_dropdown, RelationType.Width);
        _list.Relations.Remove(_dropdown, RelationType.Height);

        _dropdown.InitRelations();
        _dropdown.Relations!.Add(_list, RelationType.Height);
        _dropdown.Relations.Remove(_list, RelationType.Width);

        _list.OnClickItem.Add(HandleDropdownItemClick);
        if (EnableComboDiagLogs)
        {
            Game.Logger.LogWarning(
                "[FGUI][COMBO] dropdown ready name={Name} url={Url} dropdown={Dropdown} list={List}",
                Name,
                dropdownUrl,
                _dropdown.Name,
                _list.Name);
        }
    }

    private void RegisterInteractionHandlers()
    {
        AddEventListener("onRollOver", HandleRollOver);
        AddEventListener("onRollOut", HandleRollOut);
        AddEventListener("onClick", HandleClickOpenDropdown);
    }

    private void HandleRollOver(EventContext _)
    {
        _over = true;
        SetCurrentState();
    }

    private void HandleRollOut(EventContext _)
    {
        _over = false;
        SetCurrentState();
    }

    private void HandleClickOpenDropdown(EventContext _)
    {
        if (EnableComboDiagLogs)
        {
            Game.Logger.LogWarning(
                "[FGUI][COMBO] click name={Name} text={Text} selectedIndex={SelectedIndex} hasDropdown={HasDropdown} hasList={HasList} parent={Parent}",
                Name,
                _title,
                _selectedIndex,
                _dropdown != null,
                _list != null,
                Parent?.Name ?? "<none>");
        }

        if (_dropdown?.Parent != null)
        {
            if (ShouldSuppressImmediateClose())
            {
                if (EnableComboDiagLogs)
                {
                    Game.Logger.LogWarning(
                        "[FGUI][COMBO] close suppressed name={Name} elapsedMs={Elapsed}",
                        Name,
                        System.Environment.TickCount64 - _lastDropdownOpenTickMs);
                }
                return;
            }

            CloseDropdown();
            if (EnableComboDiagLogs)
            {
                Game.Logger.LogWarning("[FGUI][COMBO] close by self click name={Name}", Name);
            }
            return;
        }

        if (ShouldSuppressImmediateReopenAfterClose())
        {
            if (EnableComboDiagLogs)
            {
                Game.Logger.LogWarning(
                    "[FGUI][COMBO] reopen suppressed name={Name} elapsedMs={Elapsed}",
                    Name,
                    System.Environment.TickCount64 - _lastDropdownCloseTickMs);
            }
            return;
        }

        ShowDropdown();
    }

    private void ShowDropdown()
    {
        if (_dropdown == null || _list == null)
        {
            if (EnableComboDiagLogs)
            {
                Game.Logger.LogWarning(
                    "[FGUI][COMBO] show aborted name={Name} hasDropdown={HasDropdown} hasList={HasList}",
                    Name,
                    _dropdown != null,
                    _list != null);
            }
            return;
        }

        if (_dropdown.Parent != null)
        {
            if (EnableComboDiagLogs)
            {
                Game.Logger.LogWarning(
                    "[FGUI][COMBO] show skip name={Name} reason=already-open parent={Parent}",
                    Name,
                    _dropdown.Parent.Name ?? "<none>");
            }
            return;
        }

        if (EnableComboDiagLogs)
        {
            Game.Logger.LogWarning(
                "[FGUI][COMBO] show begin name={Name} items={ItemCount} visibleItemCount={VisibleItemCount} popupDir={PopupDirection} width={Width}",
                Name,
                _items.Count,
                _visibleItemCount,
                _popupDirection,
                Width);
        }

        UpdateDropdownList();
        if (_list.SelectionMode == ListSelectionMode.Single)
        {
            _list.SelectedIndex = -1;
        }

        if (_dropdown.MinWidth > Width)
        {
            _dropdown.MinWidth = 0;
        }

        if (_dropdown.MaxWidth > 0 && _dropdown.MaxWidth < Width)
        {
            _dropdown.MaxWidth = 0;
        }

        _dropdown.Width = Width;
        _list.SetSize(_dropdown.Width, _list.Height, true);
        _list.ResizeToFit(_visibleItemCount);
        _list.EnsureBoundsCorrect();
        NormalizeDropdownItemsVisualWidth();
        SyncDropdownHeightWithList();
        ShowDropdownOnViewHost();
        _lastDropdownOpenTickMs = System.Environment.TickCount64;
        SetCurrentState();

        if (EnableComboDiagLogs)
        {
            Game.Logger.LogWarning(
                "[FGUI][COMBO] show end name={Name} dropdownParent={DropdownParent} dropdownVisible={DropdownVisible} dropdownXY={X},{Y} dropdownSize={W}x{H}",
                Name,
                _dropdown.Parent?.Name ?? "<none>",
                _dropdown.Visible,
                _dropdown.X,
                _dropdown.Y,
                _dropdown.Width,
                _dropdown.Height);
            Game.Logger.LogWarning(
                "[FGUI][COMBO] show state name={Name} comboFinal={ComboFinal} dropdownFinal={DropdownFinal} comboTouchable={ComboTouchable} dropdownTouchable={DropdownTouchable}",
                Name,
                FinalVisible,
                _dropdown.FinalVisible,
                Touchable,
                _dropdown.Touchable);
            Game.Logger.LogWarning(
                "[FGUI][COMBO] list metrics name={Name} items={Items} visibleReq={VisibleReq} listChildren={ListChildren} listLayout={Layout} listSize={ListW}x{ListH} dropdownSize={DropW}x{DropH}",
                Name,
                _items.Count,
                _visibleItemCount,
                _list.NumChildren,
                _list.Layout,
                _list.Width,
                _list.Height,
                _dropdown.Width,
                _dropdown.Height);
        }
    }

    private void UpdateDropdownList()
    {
        if (!_itemsUpdated || _list == null)
        {
            return;
        }

        _itemsUpdated = false;
        _list.RemoveChildrenToPool();
        var count = _items.Count;
        for (var i = 0; i < count; i++)
        {
            var item = _list.AddItemFromPool();
            item.Text = _items[i];
            item.Icon = _icons != null && i < _icons.Count ? _icons[i] : null;
            item.Name = i < _values.Count ? _values[i] : string.Empty;
        }
    }

    private void HandleDropdownItemClick(EventContext context)
    {
        if (_dropdown == null || _list == null)
        {
            return;
        }

        CloseDropdown();

        if (context.Data is not GObject item)
        {
            return;
        }

        var index = _list.GetChildIndex(item);
        if (index < 0)
        {
            return;
        }

        _selectedIndex = int.MinValue; // force setter path even when re-selecting.
        SelectedIndex = index;

        if (EnableComboDiagLogs)
        {
            Game.Logger.LogWarning(
                "[FGUI][COMBO] item click name={Name} pickedIndex={Index} pickedText={Text} pickedValue={Value}",
                Name,
                index,
                index >= 0 && index < _items.Count ? _items[index] : "<out-of-range>",
                index >= 0 && index < _values.Count ? _values[index] : "<out-of-range>");
        }
    }

    private void UpdateCurrentSelectionVisuals()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
        {
            Text = _items[_selectedIndex];
            if (_icons != null && _selectedIndex < _icons.Count)
            {
                Icon = _icons[_selectedIndex];
            }
        }
        else
        {
            Text = string.Empty;
            if (_icons != null)
            {
                Icon = null;
            }
        }
    }

    private void UpdateSelectionController()
    {
        if (_selectionController == null || _selectionController.Changing)
        {
            return;
        }

        if (_selectedIndex < 0 || _selectedIndex >= _selectionController.PageCount)
        {
            return;
        }

        var controller = _selectionController;
        _selectionController = null;
        controller.SelectedIndex = _selectedIndex;
        _selectionController = controller;
    }

    private void SetCurrentState()
    {
        if (_buttonController == null)
        {
            return;
        }

        if (Grayed && _buttonController.HasPage(GButton.DISABLED))
        {
            SetState(GButton.DISABLED);
            return;
        }

        var isPopupOpen = _dropdown?.Parent != null;
        SetState(isPopupOpen ? GButton.DOWN : (_over ? GButton.OVER : GButton.UP));
    }

    private bool ShouldSuppressImmediateClose()
    {
        if (_lastDropdownOpenTickMs < 0)
        {
            return false;
        }

        return System.Environment.TickCount64 - _lastDropdownOpenTickMs <= DropdownCloseDebounceMs;
    }

    private bool ShouldSuppressImmediateReopenAfterClose()
    {
        if (_lastDropdownCloseTickMs < 0)
        {
            return false;
        }

        return System.Environment.TickCount64 - _lastDropdownCloseTickMs <= DropdownCloseDebounceMs;
    }

    private void CloseDropdown()
    {
        if (_dropdown == null || _dropdown.Parent == null)
        {
            return;
        }

        _dropdown.RemoveFromParent();
        _lastDropdownCloseTickMs = System.Environment.TickCount64;
        SetCurrentState();
    }

    internal void CloseDropdownByOwnerDetach()
    {
        CloseDropdown();
    }

    private void ShowDropdownOnViewHost()
    {
        if (_dropdown == null)
        {
            return;
        }

        var host = ResolvePopupHost();
        if (host == null)
        {
            if (EnableComboDiagLogs)
            {
                Game.Logger.LogWarning("[FGUI][COMBO] show aborted name={Name} reason=no-host", Name);
            }
            return;
        }

        if (_dropdown.Parent != host)
        {
            _dropdown.RemoveFromParent();
            host.AddChild(_dropdown);
        }
        else
        {
            host.SetChildIndex(_dropdown, host.NumChildren - 1);
        }

        NormalizeDropdownContainerVisualWidth();

        var anchor = ResolvePositionRelativeToHost(this, host);
        var x = anchor.X;
        var y = anchor.Y;
        if (_popupDirection == PopupDirection.Down || _popupDirection == PopupDirection.Auto)
        {
            y = anchor.Y + Height;
            if (y + _dropdown.Height > host.Height)
            {
                y = anchor.Y - _dropdown.Height;
            }
        }
        else if (_popupDirection == PopupDirection.Up)
        {
            y = anchor.Y - _dropdown.Height;
            if (y < 0)
            {
                y = anchor.Y + Height;
            }
        }

        var maxX = Math.Max(0f, host.Width - _dropdown.Width);
        var maxY = Math.Max(0f, host.Height - _dropdown.Height);
        x = Math.Clamp(x, 0f, maxX);
        y = Math.Clamp(y, 0f, maxY);
        _dropdown.SetXY(x, y);

        if (EnableComboDiagLogs)
        {
            Game.Logger.LogWarning(
                "[FGUI][COMBO] show host name={Name} host={Host} hostSize={HostW}x{HostH} anchor={AnchorX},{AnchorY} popupXY={PopupX},{PopupY}",
                Name,
                host.Name ?? "<unnamed>",
                host.Width,
                host.Height,
                anchor.X,
                anchor.Y,
                x,
                y);
        }
    }

    private GComponent? ResolvePopupHost()
    {
        GComponent? host = null;
        GObject? cursor = Parent;
        while (cursor != null)
        {
            if (cursor is GComponent component)
            {
                host = component;
            }

            cursor = cursor.Parent;
        }

        return host;
    }

    private static PointF ResolvePositionRelativeToHost(GObject target, GComponent host)
    {
        var x = target.X;
        var y = target.Y;
        GObject? cursor = target.Parent;
        while (cursor != null && cursor != host)
        {
            x += cursor.X;
            y += cursor.Y;
            cursor = cursor.Parent;
        }

        return cursor == host ? new PointF(x, y) : new PointF(target.X, target.Y);
    }

    private void SyncDropdownHeightWithList()
    {
        if (_dropdown == null || _list == null)
        {
            return;
        }

        var visibleCount = Math.Min(_visibleItemCount, Math.Min(_items.Count, _list.NumChildren));
        if (visibleCount > 0)
        {
            var desiredListHeight = 0f;
            for (var i = 0; i < visibleCount; i++)
            {
                var child = _list.GetChildAt(i);
                if (!child.Visible)
                {
                    continue;
                }

                desiredListHeight = Math.Max(desiredListHeight, child.Y + child.Height);
            }

            if (desiredListHeight > 0f)
            {
                _list.SetSize(_list.Width, desiredListHeight, true);
            }
        }

        if (_list.Parent == _dropdown)
        {
            var desiredDropdownHeight = Math.Max(_dropdown.Height, _list.Y + _list.Height);
            _dropdown.SetSize(_dropdown.Width, desiredDropdownHeight, true);
        }
    }

    private void NormalizeDropdownItemsVisualWidth()
    {
        if (_list == null)
        {
            return;
        }

        var targetWidth = _list.Width;
        for (var i = 0; i < _list.NumChildren; i++)
        {
            var item = _list.GetChildAt(i);
            item.SetSize(targetWidth, item.Height, true);

            if (item is not GComponent component)
            {
                continue;
            }

            for (var j = 0; j < component.NumChildren; j++)
            {
                var child = component.GetChildAt(j);
                if (child.X <= 1f && child.Width > targetWidth)
                {
                    child.SetSize(targetWidth, child.Height, true);
                }
            }
        }
    }

    private void NormalizeDropdownContainerVisualWidth()
    {
        if (_dropdown == null)
        {
            return;
        }

        var targetWidth = Width;
        var adjusted = false;
        if (_dropdown.Width != targetWidth)
        {
            _dropdown.SetSize(targetWidth, _dropdown.Height, true);
            adjusted = true;
        }

        for (var i = 0; i < _dropdown.NumChildren; i++)
        {
            var child = _dropdown.GetChildAt(i);
            if (ReferenceEquals(child, _list))
            {
                continue;
            }

            if (child.X <= 1f && child.Width > targetWidth)
            {
                child.SetSize(targetWidth, child.Height, true);
                adjusted = true;
            }
        }

        if (adjusted && EnableComboDiagLogs)
        {
            Game.Logger.LogWarning(
                "[FGUI][COMBO] container normalize name={Name} targetWidth={TargetWidth} dropdownWidth={DropdownWidth}",
                Name,
                targetWidth,
                _dropdown.Width);
        }
    }

    private void SetState(string state)
    {
        if (_buttonController == null || !_buttonController.HasPage(state))
        {
            return;
        }

        _buttonController.SelectedPage = state;
    }
}

public class GTree : GList
{
    public delegate void TreeNodeRenderDelegate(GTreeNode node, GComponent obj);
    public delegate void TreeNodeWillExpandDelegate(GTreeNode node, bool expand);
    
    public TreeNodeRenderDelegate? TreeNodeRender;
    public TreeNodeWillExpandDelegate? TreeNodeWillExpand;
    
    private int _indent = 30;
    private readonly GTreeNode _rootNode;
    private int _clickToExpand;
    private bool _expandedStatusInEvt;

    public GTree()
    {
        _rootNode = new GTreeNode(true);
        _rootNode.SetTree(this);
        _rootNode.Expanded = true;
        OnClickItem.Add(OnClickItemInternal);
    }

    public GTreeNode RootNode => _rootNode;
    public int Indent { get => _indent; set => _indent = value; }
    public int ClickToExpand { get => _clickToExpand; set => _clickToExpand = value; }

    public GTreeNode? GetSelectedNode()
    {
        int i = SelectedIndex;
        return i >= 0 ? GetChildAt(i)?._treeNode : null;
    }

    public List<GTreeNode> GetSelectedNodes()
    {
        var result = new List<GTreeNode>();
        foreach (int i in SelectedIndices)
        {
            var node = GetChildAt(i)?._treeNode;
            if (node != null) result.Add(node);
        }
        return result;
    }

    public void SelectNode(GTreeNode node, bool scrollItToView = false)
    {
        var parentNode = node.Parent;
        while (parentNode != null && parentNode != _rootNode)
        {
            parentNode.Expanded = true;
            parentNode = parentNode.Parent;
        }

        var cell = node.Cell;
        if (cell == null) return;
        int index = GetChildIndex(cell);
        if (index >= 0) AddSelection(index, scrollItToView);
    }

    public void UnselectNode(GTreeNode node)
    {
        var cell = node.Cell;
        if (cell == null) return;
        int index = GetChildIndex(cell);
        if (index >= 0) RemoveSelection(index);
    }

    public void ExpandAll(GTreeNode? folderNode = null)
    {
        folderNode ??= _rootNode;
        folderNode.Expanded = true;
        foreach (var child in folderNode.Children)
            if (child.IsFolder) ExpandAll(child);
    }

    public void CollapseAll(GTreeNode? folderNode = null)
    {
        folderNode ??= _rootNode;
        if (folderNode != _rootNode) folderNode.Expanded = false;
        foreach (var child in folderNode.Children)
            if (child.IsFolder) CollapseAll(child);
    }

    public override void Setup_BeforeAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_BeforeAdd(buffer, beginPos);
        if (!buffer.Seek(beginPos, 9))
        {
            return;
        }

        _indent = buffer.ReadInt();
        _clickToExpand = buffer.ReadByte();
    }

    protected override void ReadItems(ByteBuffer buffer)
    {
        GTreeNode? lastNode = null;
        var prevLevel = 0;

        var count = buffer.ReadShort();
        for (var i = 0; i < count; i++)
        {
            var nextPos = buffer.ReadUshort() + buffer.Position;
            try
            {
                var resource = buffer.ReadS();
                if (string.IsNullOrWhiteSpace(resource))
                {
                    resource = DefaultItem;
                    if (string.IsNullOrWhiteSpace(resource))
                    {
                        continue;
                    }
                }

                var isFolder = buffer.ReadBool();
                var level = buffer.ReadByte();

                var node = new GTreeNode(isFolder, resource)
                {
                    Expanded = true,
                };

                if (i == 0)
                {
                    _rootNode.AddChild(node);
                }
                else
                {
                    if (lastNode == null)
                    {
                        _rootNode.AddChild(node);
                    }
                    else if (level > prevLevel)
                    {
                        lastNode.AddChild(node);
                    }
                    else if (level < prevLevel)
                    {
                        for (var j = level; j <= prevLevel && lastNode.Parent != null; j++)
                        {
                            lastNode = lastNode.Parent;
                        }

                        lastNode.AddChild(node);
                    }
                    else
                    {
                        if (lastNode.Parent != null)
                        {
                            lastNode.Parent.AddChild(node);
                        }
                        else
                        {
                            _rootNode.AddChild(node);
                        }
                    }
                }

                lastNode = node;
                prevLevel = level;

                if (node.Cell != null)
                {
                    SetupItem(buffer, node.Cell);
                }
            }
            finally
            {
                buffer.Position = nextPos;
            }
        }

        Game.Logger.LogWarning(
            "[FGUI][TREE] read-items name={Name} count={Count} rootChildren={RootChildren}",
            string.IsNullOrWhiteSpace(Name) ? PackageItem?.Name ?? "<unnamed>" : Name,
            count,
            _rootNode.NumChildren);
        EnsureBoundsCorrect();
    }

    private void CreateCell(GTreeNode node)
    {
        var resource = string.IsNullOrWhiteSpace(node._resUrl) ? DefaultItem : node._resUrl;
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new InvalidOperationException("FairyGUI: cannot create tree node object.");
        }

        var child = AddItemFromPool(resource);
        RemoveChild(child, dispose: false);
        if (child is not GComponent comp)
        {
            throw new InvalidOperationException("FairyGUI: tree node template must be GComponent.");
        }

        child._treeNode = node;
        node._cell = comp;

        var indentObj = comp.GetChild("indent");
        if (indentObj != null)
        {
            indentObj.Width = (node.Level - 1) * _indent;
        }

        var expandedController = comp.GetController("expanded");
        if (expandedController != null)
        {
            expandedController.RemoveEventListener("onChange", OnExpandedStateChanged);
            expandedController.AddEventListener("onChange", OnExpandedStateChanged);
            expandedController.SelectedIndex = node.Expanded ? 1 : 0;
        }

        var leafController = comp.GetController("leaf");
        if (leafController != null)
        {
            leafController.SelectedIndex = node.IsFolder ? 0 : 1;
        }

        if (node.IsFolder)
        {
            comp.OnTouchBegin.Remove(OnCellTouchBegin);
            comp.OnTouchBegin.Add(OnCellTouchBegin);
        }

        TreeNodeRender?.Invoke(node, comp);
    }

    internal void AfterInserted(GTreeNode node)
    {
        if (node.Cell == null)
        {
            CreateCell(node);
        }

        var cell = node.Cell;
        if (cell == null)
        {
            return;
        }

        var index = GetInsertIndexForNode(node);
        AddChildAt(cell, index);
        TreeNodeRender?.Invoke(node, cell);

        if (node.IsFolder && node.Expanded)
        {
            CheckChildren(node, index);
        }
    }

    internal void AfterRemoved(GTreeNode node)
    {
        RemoveNode(node);
    }

    internal void AfterExpanded(GTreeNode node)
    {
        if (node == _rootNode)
        {
            CheckChildren(_rootNode, -1);
            return;
        }

        TreeNodeWillExpand?.Invoke(node, true);
        var cell = node.Cell;
        if (cell == null)
        {
            return;
        }

        TreeNodeRender?.Invoke(node, cell);
        var controller = cell.GetController("expanded");
        if (controller != null)
        {
            controller.SelectedIndex = 1;
        }

        if (cell.Parent != null)
        {
            CheckChildren(node, GetChildIndex(cell));
        }
    }

    internal void AfterCollapsed(GTreeNode node)
    {
        if (node == _rootNode)
        {
            CheckChildren(_rootNode, -1);
            return;
        }

        TreeNodeWillExpand?.Invoke(node, false);
        var cell = node.Cell;
        if (cell == null)
        {
            return;
        }

        TreeNodeRender?.Invoke(node, cell);
        var controller = cell.GetController("expanded");
        if (controller != null)
        {
            controller.SelectedIndex = 0;
        }

        if (cell.Parent != null)
        {
            HideFolderNode(node);
        }
    }

    internal void AfterMoved(GTreeNode node)
    {
        var cell = node.Cell;
        if (cell == null || cell.Parent == null)
        {
            return;
        }

        var startIndex = GetChildIndex(cell);
        var endIndex = node.IsFolder ? GetFolderEndIndex(startIndex, node.Level) : startIndex + 1;
        var insertIndex = GetInsertIndexForNode(node);
        var count = endIndex - startIndex;

        if (insertIndex < startIndex)
        {
            for (var i = 0; i < count; i++)
            {
                var obj = GetChildAt(startIndex + i);
                SetChildIndex(obj, insertIndex + i);
            }
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var obj = GetChildAt(startIndex);
                SetChildIndex(obj, insertIndex);
            }
        }
    }

    private int GetInsertIndexForNode(GTreeNode node)
    {
        var prevNode = node.GetPrevSibling() ?? node.Parent;
        var insertIndex = 0;
        if (prevNode?.Cell != null)
        {
            insertIndex = GetChildIndex(prevNode.Cell) + 1;
        }

        var myLevel = node.Level;
        var count = NumChildren;
        for (var i = insertIndex; i < count; i++)
        {
            var testNode = GetChildAt(i)._treeNode;
            if (testNode == null || testNode.Level <= myLevel)
            {
                break;
            }

            insertIndex++;
        }

        return insertIndex;
    }

    private int CheckChildren(GTreeNode folderNode, int index)
    {
        var count = folderNode.NumChildren;
        for (var i = 0; i < count; i++)
        {
            index++;
            var node = folderNode.GetChildAt(i);
            if (node == null)
            {
                continue;
            }

            if (node.Cell == null)
            {
                CreateCell(node);
            }

            var cell = node.Cell;
            if (cell == null)
            {
                continue;
            }

            if (cell.Parent == null)
            {
                AddChildAt(cell, index);
            }
            else
            {
                SetChildIndex(cell, index);
            }

            if (node.IsFolder && node.Expanded)
            {
                index = CheckChildren(node, index);
            }
        }

        return index;
    }

    private void HideFolderNode(GTreeNode folderNode)
    {
        var count = folderNode.NumChildren;
        for (var i = 0; i < count; i++)
        {
            var node = folderNode.GetChildAt(i);
            if (node == null)
            {
                continue;
            }

            if (node.Cell?.Parent != null)
            {
                RemoveChild(node.Cell);
            }

            if (node.IsFolder && node.Expanded)
            {
                HideFolderNode(node);
            }
        }
    }

    private void RemoveNode(GTreeNode node)
    {
        var cell = node.Cell;
        if (cell != null)
        {
            cell.OnTouchBegin.Remove(OnCellTouchBegin);
            var expandedController = cell.GetController("expanded");
            expandedController?.RemoveEventListener("onChange", OnExpandedStateChanged);

            if (cell.Parent != null)
            {
                RemoveChild(cell);
            }

            cell._treeNode = null;
            cell.Dispose();
            node._cell = null;
        }

        if (!node.IsFolder)
        {
            return;
        }

        var count = node.NumChildren;
        for (var i = 0; i < count; i++)
        {
            var child = node.GetChildAt(i);
            if (child != null)
            {
                RemoveNode(child);
            }
        }
    }

    private int GetFolderEndIndex(int startIndex, int level)
    {
        var count = NumChildren;
        for (var i = startIndex + 1; i < count; i++)
        {
            var node = GetChildAt(i)._treeNode;
            if (node == null || node.Level <= level)
            {
                return i;
            }
        }

        return count;
    }

    private void OnCellTouchBegin(EventContext context)
    {
        if (context.Sender is GObject sender && sender._treeNode != null)
        {
            _expandedStatusInEvt = sender._treeNode.Expanded;
        }
    }

    private void OnExpandedStateChanged(EventContext context)
    {
        if (context.Sender is not Controller controller)
        {
            return;
        }

        var node = controller.Parent?._treeNode;
        if (node == null)
        {
            return;
        }

        node.Expanded = controller.SelectedIndex == 1;
    }

    private void OnClickItemInternal(EventContext context)
    {
        if (_clickToExpand == 0)
        {
            return;
        }

        if (context.Data is not GObject item)
        {
            return;
        }

        var node = item._treeNode;
        if (node == null || !node.IsFolder)
        {
            return;
        }

        if (_expandedStatusInEvt != node.Expanded)
        {
            return;
        }

        node.Expanded = !node.Expanded;
    }
}

public class GTreeNode
{
    public object? Data { get; set; }
    public GTreeNode? Parent { get; private set; }
    public GTree? Tree { get; private set; }
    
    private List<GTreeNode>? _children;
    private bool _expanded;
    private int _level;
    internal GComponent? _cell;
    internal string? _resUrl;

    public GTreeNode(bool hasChild, string? resUrl = null)
    {
        if (hasChild) _children = new List<GTreeNode>();
        _resUrl = resUrl;
    }

    public GComponent? Cell => _cell;
    public int Level => _level;
    public bool IsFolder => _children != null;
    public string? Text => _cell?.Text;
    public string? Icon => _cell?.Icon;
    
    public bool Expanded
    {
        get => _expanded;
        set
        {
            if (_children == null) return;
            if (_expanded == value) return;
            _expanded = value;
            if (Tree != null)
            {
                if (value)
                {
                    Tree.AfterExpanded(this);
                }
                else
                {
                    Tree.AfterCollapsed(this);
                }
            }
        }
    }

    public IReadOnlyList<GTreeNode> Children => _children ?? (IReadOnlyList<GTreeNode>)Array.Empty<GTreeNode>();
    public int NumChildren => _children?.Count ?? 0;

    public GTreeNode AddChild(GTreeNode child) => AddChildAt(child, _children?.Count ?? 0);

    public GTreeNode AddChildAt(GTreeNode child, int index)
    {
        if (_children == null) throw new InvalidOperationException("Not a folder node");
        var clampedIndex = Math.Clamp(index, 0, _children.Count);
        if (child.Parent == this)
        {
            var oldIndex = _children.IndexOf(child);
            if (oldIndex == clampedIndex)
            {
                return child;
            }

            _children.RemoveAt(oldIndex);
            if (clampedIndex > _children.Count)
            {
                clampedIndex = _children.Count;
            }
            _children.Insert(clampedIndex, child);
            Tree?.AfterMoved(child);
            return child;
        }

        if (child.Parent != null) child.Parent.RemoveChild(child);
        
        child.Parent = this;
        child._level = _level + 1;
        child.SetTree(Tree);
        
        _children.Insert(clampedIndex, child);
        Tree?.AfterInserted(child);
        return child;
    }

    public void RemoveChild(GTreeNode child)
    {
        if (_children == null || !_children.Contains(child)) return;
        var tree = Tree;
        _children.Remove(child);
        child.Parent = null;
        tree?.AfterRemoved(child);
        child.SetTree(null);
    }

    public void RemoveChildren(int beginIndex = 0, int endIndex = -1)
    {
        if (_children == null) return;
        var tree = Tree;
        if (endIndex < 0) endIndex = _children.Count;
        for (int i = endIndex - 1; i >= beginIndex; i--)
        {
            var child = _children[i];
            _children.RemoveAt(i);
            child.Parent = null;
            tree?.AfterRemoved(child);
            child.SetTree(null);
        }
    }

    public GTreeNode? GetChildAt(int index) => _children != null && index >= 0 && index < _children.Count ? _children[index] : null;
    public GTreeNode? GetPrevSibling() => Parent?._children?.ElementAtOrDefault((Parent._children.IndexOf(this)) - 1);
    public GTreeNode? GetNextSibling() => Parent?._children?.ElementAtOrDefault((Parent._children.IndexOf(this)) + 1);

    internal void SetTree(GTree? tree)
    {
        Tree = tree;
        if (_children != null)
            foreach (var child in _children)
                child.SetTree(tree);
    }
}

public class Window : GComponent
{
    private GComponent? _contentPane;
    private GComponent? _frame;
    private GObject? _closeButton;
    private GObject? _dragArea;
    private bool _modal;
    private bool _inited;
    private bool _isStageShown;

    public Window() { Name = "Window"; }

    public GComponent? ContentPane
    {
        get => _contentPane;
        set
        {
            if (_contentPane != value)
            {
                if (_contentPane != null) RemoveChild(_contentPane);
                _contentPane = value;
                if (_contentPane != null)
                {
                    Name = "Window - " + _contentPane.Name;
                    AddChild(_contentPane);
                    SetSize(_contentPane.Width, _contentPane.Height);
                    _frame = _contentPane.GetChild("frame") as GComponent;
                    if (_frame != null)
                    {
                        CloseButton = _frame.GetChild("closeButton");
                        DragArea = _frame.GetChild("dragArea");
                    }
                }
                else { _frame = null; Name = "Window"; }
            }
        }
    }

    public GComponent? Frame => _frame;

    public GObject? CloseButton
    {
        get => _closeButton;
        set
        {
            if (_closeButton != null) _closeButton.OnClick.Remove(OnCloseButtonClick);
            _closeButton = value;
            if (_closeButton != null) _closeButton.OnClick.Add(OnCloseButtonClick);
        }
    }

    public GObject? DragArea
    {
        get => _dragArea;
        set
        {
            if (_dragArea != null) _dragArea.Draggable = false;
            _dragArea = value;
            if (_dragArea != null) _dragArea.Draggable = true;
        }
    }

    public bool Modal { get => _modal; set => _modal = value; }
    public bool IsShowing => Parent != null || _isStageShown;

    public void Show()
    {
        UIRuntime.AddToRoot(this);
        _isStageShown = true;
        DoShow();
    }
    public void Hide() { if (IsShowing) DoHideAnimation(); }
    public void HideImmediately()
    {
        UIRuntime.RemoveFromRoot(this, dispose: false);
        _isStageShown = false;
        OnHide();
    }

    public void Center()
    {
        SetXY((UIRuntime.RootWidth - Width) / 2, (UIRuntime.RootHeight - Height) / 2);
    }

    public void BringToFront()
    {
        if (!IsShowing)
        {
            return;
        }

        UIRuntime.RemoveFromRoot(this, dispose: false);
        UIRuntime.AddToRoot(this);
    }

    internal void DoShow() { if (!_inited) Init(); else DoShowAnimation(); }

    public void Init() { if (_inited) return; _inited = true; OnInit(); if (IsShowing) DoShowAnimation(); }

    protected virtual void OnInit() { }
    protected virtual void OnShown() { }
    protected virtual void OnHide() { }
    protected virtual void DoShowAnimation() { OnShown(); }
    protected virtual void DoHideAnimation() { HideImmediately(); }

    private void OnCloseButtonClick(EventContext ctx) => Hide();

    public override void Dispose() { _inited = false; base.Dispose(); }
}

public class PopupMenu
{
    private const string DefaultPopupMenuUrl = "ui://Basics/PopupMenu";
    private const int PopupMenuMinItemFontSize = 14;
    private GComponent? _contentPane;
    private GList? _list;
    private GObject? _popupTarget;
    private bool _isShown;
    private readonly List<PopupMenuItem> _items = new();
    private readonly List<PopupMenuItem> _visibleItems = new();
    private readonly string? _resourceURL;
    private float _listPaddingWidth;
    private float _listPaddingHeight;

    public GComponent? ContentPane => _contentPane;
    public int ItemCount => _items.Count;
    public bool IsShown => _isShown;

    public PopupMenu(string? resourceURL = null)
    {
        _resourceURL = string.IsNullOrWhiteSpace(resourceURL) ? DefaultPopupMenuUrl : resourceURL;
        EnsureContentPaneCreated();
    }

    private void EnsureContentPaneCreated()
    {
        if (_contentPane != null)
        {
            return;
        }

        _contentPane = CreateContentPane(_resourceURL);
        if (_contentPane == null && !string.Equals(_resourceURL, DefaultPopupMenuUrl, StringComparison.OrdinalIgnoreCase))
        {
            _contentPane = CreateContentPane(DefaultPopupMenuUrl);
        }

        if (_contentPane == null)
        {
            Game.Logger.LogWarning("[FGUI][PopupMenu] content pane create failed url={Url}", _resourceURL ?? "<null>");
            return;
        }

        _contentPane.Visible = false;
        _list = _contentPane.GetChild("list") as GList;
        _list ??= _contentPane.NumChildren > 1 ? _contentPane.GetChildAt(1) as GList : null;
        if (_list == null)
        {
            Game.Logger.LogWarning("[FGUI][PopupMenu] missing list child pane={Pane}", _contentPane.Name);
            return;
        }

//         Game.Logger.LogInformation(
//             "[FGUI][POPUP][MENU] pane ready pane={Pane} list={List} listSize={W}x{H} paneSize={PW}x{PH}",
//             _contentPane.Name ?? "<unnamed>",
//             _list.Name ?? "<unnamed>",
//             _list.Width,
//             _list.Height,
//             _contentPane.Width,
//             _contentPane.Height);

        _listPaddingWidth = Math.Max(0f, _contentPane.Width - _list.Width);
        _listPaddingHeight = Math.Max(0f, _contentPane.Height - _list.Height);
        _list.AutoResizeItem = true;

        _list.OnClickItem.Remove(HandleClickItem);
        _list.OnClickItem.Add(HandleClickItem);
        _list.ItemRenderer = RenderItem;
    }

    private static GComponent? CreateContentPane(string? resourceURL)
    {
        if (string.IsNullOrWhiteSpace(resourceURL))
        {
            return null;
        }

        return UIRuntime.CreateObject(resourceURL) as GComponent
            ?? UIPackage.CreateObjectFromURL(resourceURL) as GComponent;
    }

    public PopupMenuItem AddItem(string caption, Action? callback = null)
    {
        var item = new PopupMenuItem { Caption = caption, Callback = callback };
        _items.Add(item);
        RefreshList();
        return item;
    }

    public PopupMenuItem AddItemAt(string caption, int index, Action? callback = null)
    {
        var item = new PopupMenuItem { Caption = caption, Callback = callback };
        _items.Insert(index, item);
        RefreshList();
        return item;
    }

    public void AddSeparator()
    {
        var item = new PopupMenuItem { IsSeparator = true };
        _items.Add(item);
        RefreshList();
    }

    public PopupMenuItem? GetItemAt(int index)
    {
        if (index < 0 || index >= _items.Count) return null;
        return _items[index];
    }

    public void SetItemText(string name, string caption)
    {
        var item = _items.FirstOrDefault(i => i.Name == name);
        if (item != null) item.Caption = caption;
        RefreshList();
    }

    public void SetItemVisible(string name, bool visible)
    {
        var item = _items.FirstOrDefault(i => i.Name == name);
        if (item != null) item.Visible = visible;
        RefreshList();
    }

    public void SetItemGrayed(string name, bool grayed)
    {
        var item = _items.FirstOrDefault(i => i.Name == name);
        if (item != null) item.Grayed = grayed;
        RefreshList();
    }

    public void SetItemCheckable(string name, bool checkable)
    {
        var item = _items.FirstOrDefault(i => i.Name == name);
        if (item != null) item.Checkable = checkable;
        RefreshList();
    }

    public void SetItemChecked(string name, bool check)
    {
        var item = _items.FirstOrDefault(i => i.Name == name);
        if (item != null) item.Checked = check;
        RefreshList();
    }

    public bool IsItemChecked(string name)
    {
        var item = _items.FirstOrDefault(i => i.Name == name);
        return item?.Checked ?? false;
    }

    public bool RemoveItem(string name)
    {
        var item = _items.FirstOrDefault(i => i.Name == name);
        if (item != null)
        {
            _items.Remove(item);
            RefreshList();
            return true;
        }
        return false;
    }

    public void ClearItems()
    {
        _items.Clear();
        if (_list != null) _list.RemoveChildren();
    }

    private void RefreshList()
    {
        EnsureContentPaneCreated();
        if (_list == null) return;

        _visibleItems.Clear();
        foreach (var item in _items)
        {
            if (item.Visible)
            {
                _visibleItems.Add(item);
            }
        }

        _list.ItemRenderer = RenderItem;
        _list.NumItems = _visibleItems.Count;
        _list.ResizeToFit(_visibleItems.Count);

        if (_contentPane != null)
        {
            var paneWidth = _list.Width + _listPaddingWidth;
            var paneHeight = _list.Height + _listPaddingHeight;
            _contentPane.SetSize(paneWidth, paneHeight, true);
        }

        EnsureTouchBindingRecursive(_list);
    }

    private void RenderItem(int index, GObject itemObject)
    {
        if (index < 0 || index >= _visibleItems.Count)
        {
            return;
        }

        var item = _visibleItems[index];
        itemObject.Text = item.Caption;
        itemObject.Grayed = item.Grayed;
        EnsurePopupItemTextReadable(itemObject);

        if (itemObject is GButton button)
        {
            button.Selected = item.Checked;

            var checkedController = button.GetController("checkedController");
            if (checkedController != null)
            {
                checkedController.SelectedIndex = item.Checkable
                    ? (item.Checked ? 2 : 1)
                    : 0;
            }
        }
    }

    private static void EnsurePopupItemTextReadable(GObject itemObject)
    {
        switch (itemObject)
        {
            case GButton button:
            {
                if (button.TitleFontSize > 0 && button.TitleFontSize < PopupMenuMinItemFontSize)
                {
                    button.TitleFontSize = PopupMenuMinItemFontSize;
                }
                break;
            }
            case GLabel label:
            {
                if (label.TitleFontSize > 0 && label.TitleFontSize < PopupMenuMinItemFontSize)
                {
                    label.TitleFontSize = PopupMenuMinItemFontSize;
                }
                break;
            }
            case GTextField textField:
            {
                if (textField.FontSize > 0 && textField.FontSize < PopupMenuMinItemFontSize)
                {
                    textField.FontSize = PopupMenuMinItemFontSize;
                }
                break;
            }
        }
    }

    private void HandleClickItem(EventContext context)
    {
        if (_list == null || context.Data is not GObject clickedObject)
        {
//             Game.Logger.LogWarning(
//                 "[FGUI][POPUP][TRACE][ITEM] click ignored hasList={HasList} dataType={DataType}",
//                 _list != null,
//                 context.Data?.GetType().Name ?? "<null>");
            return;
        }

        var index = ResolveVisibleItemIndex(clickedObject);
//         Game.Logger.LogWarning(
//             "[FGUI][POPUP][TRACE][ITEM] clicked={Clicked} clickedType={ClickedType} resolvedIndex={Index} visibleCount={Count} listChildren={Children}",
//             clickedObject.Name ?? "<unnamed>",
//             clickedObject.GetType().Name,
//             index,
//             _visibleItems.Count,
//             _list.NumChildren);
        if (index < 0 || index >= _visibleItems.Count)
        {
            // Defensive close: if click routed from a nested node we failed to resolve,
            // close popup to avoid leaving an invisible input blocker on top.
//             Game.Logger.LogWarning("[FGUI][POPUP][TRACE][ITEM] invalid index => hide");
            Hide();
            return;
        }

        var item = _visibleItems[index];
        if (item.Grayed || item.IsSeparator)
        {
//             Game.Logger.LogWarning(
//                 "[FGUI][POPUP][TRACE][ITEM] blocked index={Index} caption={Caption} grayed={Grayed} separator={Separator}",
//                 index,
//                 item.Caption,
//                 item.Grayed,
//                 item.IsSeparator);
            return;
        }

        if (item.Checkable)
        {
            item.Checked = !item.Checked;
        }

//         Game.Logger.LogWarning(
//             "[FGUI][POPUP][TRACE][ITEM] invoke index={Index} caption={Caption} checkable={Checkable} checked={Checked}",
//             index,
//             item.Caption,
//             item.Checkable,
//             item.Checked);
        Hide();
        item.Callback?.Invoke();
    }

    private int ResolveVisibleItemIndex(GObject clickedObject)
    {
        if (_list == null)
        {
            return -1;
        }

        GObject? cursor = clickedObject;
        while (cursor != null && cursor.Parent != _list)
        {
            cursor = cursor.Parent;
        }

        return cursor == null ? -1 : _list.GetChildIndex(cursor);
    }

    public void Show(GObject? target = null, PopupDirection direction = PopupDirection.Auto)
    {
        EnsureContentPaneCreated();
        if (_contentPane == null)
        {
//             Game.Logger.LogWarning("[FGUI][POPUP][TRACE][SHOW] aborted contentPane=null");
            _isShown = false;
            return;
        }
        _popupTarget = target ?? _popupTarget;
        RefreshList();

        var anchor = target ?? _popupTarget;
        var host = ResolvePopupHost(anchor);
//         Game.Logger.LogWarning(
//             "[FGUI][POPUP][TRACE][SHOW] begin anchor={Anchor} host={Host} dir={Dir} paneParent={PaneParent} paneVisible={PaneVisible} paneFinal={PaneFinal} paneNative={PaneNative}",
//             anchor?.Name ?? "<null>",
//             host?.Name ?? "<null>",
//             direction,
//             _contentPane.Parent?.Name ?? "<none>",
//             _contentPane.Visible,
//             _contentPane.FinalVisible,
//             _contentPane.NativeObject != null);
        if (host != null && anchor != null)
        {
            if (_contentPane.Parent != host)
            {
                _contentPane.RemoveFromParent();
                host.AddChild(_contentPane);
            }
            else
            {
                host.SetChildIndex(_contentPane, host.NumChildren - 1);
            }

            var anchorPos = ResolvePositionRelativeToHost(anchor, host);
            var x = anchorPos.X;
            var y = anchorPos.Y;
            if (direction == PopupDirection.Down || direction == PopupDirection.Auto)
            {
                y = anchorPos.Y + anchor.Height;
                if (y + _contentPane.Height > host.Height)
                    y = anchorPos.Y - _contentPane.Height;
            }
            else if (direction == PopupDirection.Up)
            {
                y = anchorPos.Y - _contentPane.Height;
                if (y < 0f)
                    y = anchorPos.Y + anchor.Height;
            }

            var maxX = Math.Max(0f, host.Width - _contentPane.Width);
            var maxY = Math.Max(0f, host.Height - _contentPane.Height);
            _contentPane.SetXY(Math.Clamp(x, 0f, maxX), Math.Clamp(y, 0f, maxY));
            _contentPane.Touchable = true;
            _contentPane.Visible = true;
//             Game.Logger.LogInformation(
//                 "[FGUI][POPUP][MENU] show anchor={Anchor} host={Host} hostSize={HostW}x{HostH} paneSize={PaneW}x{PaneH} items={Items} xy={X},{Y} final={Final} alpha={Alpha} paneNative={PaneNative} hostNative={HostNative}",
//                 anchor.Name ?? "<unnamed>",
//                 host.Name ?? "<unnamed>",
//                 host.Width,
//                 host.Height,
//                 _contentPane.Width,
//                 _contentPane.Height,
//                 _visibleItems.Count,
//                 _contentPane.X,
//                 _contentPane.Y,
//                 _contentPane.FinalVisible,
//                 _contentPane.Alpha,
//                 _contentPane.NativeObject != null,
//                 host.NativeObject != null);
            _isShown = true;
//             Game.Logger.LogWarning(
//                 "[FGUI][POPUP][TRACE][SHOW] end parent={Parent} visible={Visible} final={Final} touchable={Touchable} native={Native}",
//                 _contentPane.Parent?.Name ?? "<none>",
//                 _contentPane.Visible,
//                 _contentPane.FinalVisible,
//                 _contentPane.Touchable,
//                 _contentPane.NativeObject != null);
            return;
        }

        Game.Logger.LogWarning(
            "[FGUI][POPUP][MENU] show aborted anchor={Anchor} host={Host} pane={Pane} items={Items}",
            anchor?.Name ?? "<null>",
            host?.Name ?? "<null>",
            _contentPane.Name ?? "<unnamed>",
            _visibleItems.Count);
        _contentPane.Visible = false;
        _contentPane.RemoveFromParent();
        _isShown = false;
    }

    public void Hide()
    {
        _isShown = false;
        if (_contentPane != null)
        {
//             Game.Logger.LogWarning(
//                 "[FGUI][POPUP][TRACE][HIDE] begin parent={Parent} visible={Visible} final={Final} touchable={Touchable} native={Native}",
//                 _contentPane.Parent?.Name ?? "<none>",
//                 _contentPane.Visible,
//                 _contentPane.FinalVisible,
//                 _contentPane.Touchable,
//                 _contentPane.NativeObject != null);
            ReleasePointerRecursive(_contentPane);
            _contentPane.Touchable = false;
            _contentPane.Visible = false;
            _contentPane.RemoveFromParent();
            if (_contentPane.NativeObject != null)
            {
                Render.SCERenderContext.Instance.RemoveFromParent(_contentPane);
            }

//             Game.Logger.LogWarning(
//                 "[FGUI][POPUP][TRACE][HIDE] end parent={Parent} visible={Visible} final={Final} touchable={Touchable} native={Native}",
//                 _contentPane.Parent?.Name ?? "<none>",
//                 _contentPane.Visible,
//                 _contentPane.FinalVisible,
//                 _contentPane.Touchable,
//                 _contentPane.NativeObject != null);
        }
    }

    private static void ReleasePointerRecursive(GObject node)
    {
        if (node.NativeObject != null)
        {
            Render.SCERenderContext.Instance.Adapter?.ReleasePointer(node.NativeObject);
        }

        if (node is not GComponent component)
        {
            return;
        }

        for (var i = 0; i < component.NumChildren; i++)
        {
            ReleasePointerRecursive(component.GetChildAt(i));
        }
    }

    public void Dispose()
    {
        if (_list != null)
        {
            _list.OnClickItem.Remove(HandleClickItem);
        }

        _contentPane?.Dispose();
        _contentPane = null;
        _list = null;
        _items.Clear();
        _visibleItems.Clear();
    }

    private static GComponent? ResolvePopupHost(GObject? target)
    {
        if (target == null)
        {
            return null;
        }

        GComponent? host = null;
        GObject? cursor = target.Parent;
        while (cursor != null)
        {
            if (cursor is GComponent component)
            {
                host = component;
            }

            cursor = cursor.Parent;
        }

        return host;
    }

    private static PointF ResolvePositionRelativeToHost(GObject target, GComponent host)
    {
        var x = target.X;
        var y = target.Y;
        GObject? cursor = target.Parent;
        while (cursor != null && cursor != host)
        {
            x += cursor.X;
            y += cursor.Y;
            cursor = cursor.Parent;
        }

        return cursor == host ? new PointF(x, y) : new PointF(target.X, target.Y);
    }

    private static void EnsureTouchBindingRecursive(GObject? node)
    {
        if (node == null)
        {
            return;
        }

        Render.SCERenderContext.Instance.EnsureTouchBinding(node);
        if (node is not GComponent component)
        {
            return;
        }

        for (var i = 0; i < component.NumChildren; i++)
        {
            EnsureTouchBindingRecursive(component.GetChildAt(i));
        }
    }
}

public class PopupMenuItem
{
    public string Name { get; set; } = "";
    public string Caption { get; set; } = "";
    public Action? Callback { get; set; }
    public bool Visible { get; set; } = true;
    public bool Grayed { get; set; }
    public bool Checkable { get; set; }
    public bool Checked { get; set; }
    public bool IsSeparator { get; set; }
}

/// <summary>
/// Object pool for GObject recycling
/// </summary>
public class GObjectPool
{
    public delegate void InitCallback(GObject obj);
    public InitCallback? OnInit;
    
    private readonly Dictionary<string, Queue<GObject>> _pool = new();
    
    public int Count => _pool.Count;
    
    public GObject? GetObject(string url)
    {
        url = UIPackage.NormalizeURL(url);
        if (string.IsNullOrEmpty(url)) return null;
        
        if (_pool.TryGetValue(url, out var queue) && queue.Count > 0)
        {
            return queue.Dequeue();
        }
        
        var obj = UIPackage.CreateObjectFromURL(url);
        if (obj != null)
        {
            OnInit?.Invoke(obj);
        }
        return obj;
    }
    
    public void ReturnObject(GObject obj)
    {
        string? url = obj.ResourceUrl;
        if (string.IsNullOrEmpty(url)) return;
        
        obj.RemoveFromParent();
        
        if (!_pool.TryGetValue(url, out var queue))
        {
            queue = new Queue<GObject>();
            _pool[url] = queue;
        }
        queue.Enqueue(obj);
    }
    
    public void Clear()
    {
        foreach (var kv in _pool)
        {
            foreach (var obj in kv.Value)
                obj.Dispose();
        }
        _pool.Clear();
    }
}

#endif


