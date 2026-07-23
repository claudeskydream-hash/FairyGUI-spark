#if CLIENT
using System.Drawing;
using FairyGUI;
using FairyGUI.Utils;

namespace FairyGUI;

public class GImage : GObject, IColorGear
{
    private const float MinRenderableFillSize = 1f;
    private Color _color = Color.White;
    private string? _icon;
    private FlipType _flip = FlipType.None;
    private FillMethod _fillMethod = FillMethod.None;
    private int _fillOrigin;
    private float _fillAmount = 1;
    private bool _fillClockwise = true;
    private FillMethod _lastUnsupportedFillMethod = FillMethod.None;
    private bool _fillNativeUpgradeAttempted;
    private bool _fillDiagLogged;

    public Color Color { get => _color; set { _color = value; UpdateDisplay(); } }
    public override string? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            ApplyIcon(value);
        }
    }
    public FlipType Flip { get => _flip; set { _flip = value; UpdateDisplay(); } }
    public FillMethod FillMethod { get => _fillMethod; set { _fillMethod = value; UpdateDisplay(); } }
    public int FillOrigin { get => _fillOrigin; set { _fillOrigin = value; UpdateDisplay(); } }
    public float FillAmount { get => _fillAmount; set { _fillAmount = Math.Clamp(value, 0, 1); UpdateDisplay(); } }
    public bool FillClockwise { get => _fillClockwise; set { _fillClockwise = value; UpdateDisplay(); } }

    public override void ConstructFromResource()
    {
        // 确保精灵/图集资源被加载（填充 PackageItem.Sprite）。
        // 设计期图片经 CreateObject→GetItemAsset 已加载，但 GLoader 直接 new GImage 会绕过，
        // 导致 Sprite 一直为 null、渲染时走不到图集路径而画不出（单图也是各自独占一页图集）。
        PackageItem?.Owner?.GetItemAsset(PackageItem);

        SourceWidth = PackageItem?.Width ?? 0;
        SourceHeight = PackageItem?.Height ?? 0;
        InitWidth = SourceWidth;
        InitHeight = SourceHeight;
        if (_width == 0 && _height == 0)
            SetSize(SourceWidth, SourceHeight);
    }

    public override void Setup_BeforeAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_BeforeAdd(buffer, beginPos);
        buffer.Seek(beginPos, 5);
        if (buffer.ReadBool()) _color = buffer.ReadColor();
        _flip = (FlipType)buffer.ReadByte();
        _fillMethod = (FillMethod)buffer.ReadByte();
        if (_fillMethod != FillMethod.None)
        {
            _fillOrigin = buffer.ReadShort();
            _fillClockwise = buffer.ReadBool();
            _fillAmount = buffer.ReadFloat();
        }
    }

    protected virtual void UpdateDisplay()
    {
        // 确保原生控件已创建
        if (NativeObject == null)
            Render.SCERenderContext.Instance.CreateNativeControl(this);

        if (NativeObject == null) return;
        
        var adapter = Render.SCERenderContext.Instance.Adapter;
        if (adapter == null) return;
        UpgradeNativeControlForFillIfNeeded(adapter);

        // 应用颜色着色（Tint）- 这是图片叠加颜色，不是背景色
        // 使用专门的SetTintColor方法，如果SCE不支持会fallback
        if (_color != Color.White)
        {
            adapter.SetTintColor(NativeObject, _color);
        }
        else
        {
            // 重置为白色（无着色）
            adapter.SetTintColor(NativeObject, Color.White);
        }

        var renderX = _x;
        var renderY = _y;
        if (PivotAsAnchor && (_pivotX != 0f || _pivotY != 0f))
        {
            renderX -= _width * _pivotX;
            renderY -= _height * _pivotY;
        }

        var renderWidth = MathF.Max(0f, _width);
        var renderHeight = MathF.Max(0f, _height);
        var fillAmount = Math.Clamp(_fillAmount, 0f, 1f);
        var nativeFillApplied = adapter.TrySetImageFill(NativeObject, _fillMethod, _fillOrigin, _fillClockwise, fillAmount);
        if (!_fillDiagLogged && _fillMethod != FillMethod.None)
        {
            _fillDiagLogged = true;
            Game.Logger.LogWarning(
                "[FGUI][FILL][DIAG] image={Name} pkg={Pkg}/{Item} type={NativeType} fill={FillMethod} origin={Origin} clockwise={Clockwise} amount={Amount:F2} applied={Applied}",
                Name,
                PackageItem?.Owner?.Name ?? "<none>",
                PackageItem?.Name ?? "<none>",
                NativeObject.GetType().Name,
                _fillMethod,
                _fillOrigin,
                _fillClockwise,
                fillAmount,
                nativeFillApplied);
        }
        if (_fillMethod != FillMethod.None && fillAmount < 1.0f && !nativeFillApplied)
        {
            if (_fillMethod == FillMethod.Horizontal)
            {
                renderWidth = _width * fillAmount;
                if (_width > 0f && renderWidth <= 0f)
                    renderWidth = MathF.Min(MinRenderableFillSize, _width);
                renderWidth = Math.Clamp(renderWidth, 0f, Math.Max(0f, _width));

                if (_fillOrigin == (int)OriginHorizontal.Right)
                    renderX += _width - renderWidth;
            }
            else if (_fillMethod == FillMethod.Vertical)
            {
                renderHeight = _height * fillAmount;
                if (_height > 0f && renderHeight <= 0f)
                    renderHeight = MathF.Min(MinRenderableFillSize, _height);
                renderHeight = Math.Clamp(renderHeight, 0f, Math.Max(0f, _height));

                if (_fillOrigin == (int)OriginVertical.Bottom)
                    renderY += _height - renderHeight;
            }
            else
            {
                if (_lastUnsupportedFillMethod != _fillMethod)
                {
                    _lastUnsupportedFillMethod = _fillMethod;
                    Game.Logger.LogWarning("[FGUI] Image '{Name}' FillMethod {FillMethod} not supported in SCE", Name, _fillMethod);
                }
            }
        }
        else
        {
            _lastUnsupportedFillMethod = FillMethod.None;
        }

        var scaleFactor = UIRuntime.ContentScaleFactor;
        if (scaleFactor <= 0f)
            scaleFactor = 1f;
        adapter.SetPosition(NativeObject, renderX * scaleFactor, renderY * scaleFactor);
        adapter.SetSize(NativeObject, renderWidth * scaleFactor, renderHeight * scaleFactor);

        // Flip（翻转）- 使用Scale负值模拟
        float flipScaleX = (_flip == FlipType.Horizontal || _flip == FlipType.Both) ? -1 : 1;
        float flipScaleY = (_flip == FlipType.Vertical || _flip == FlipType.Both) ? -1 : 1;

        // 组合用户设置的缩放和翻转
        adapter.SetScale(NativeObject, flipScaleX * _scaleX, flipScaleY * _scaleY);
    }

    private void UpgradeNativeControlForFillIfNeeded(Render.ISCEAdapter adapter)
    {
        if (_fillMethod == FillMethod.None || NativeObject == null || _fillNativeUpgradeAttempted)
        {
            return;
        }

        var probeAmount = Math.Clamp(_fillAmount, 0f, 1f);
        if (adapter.TrySetImageFill(NativeObject, _fillMethod, _fillOrigin, _fillClockwise, probeAmount))
        {
            return;
        }

        // Fill is configured but current native control can't apply it (often created too early as Panel).
        _fillNativeUpgradeAttempted = true;
        Render.SCERenderContext.Instance.DisposeNative(this);
        Render.SCERenderContext.Instance.CreateNativeControl(this);
        Parent?.ChildStateChanged(this);
    }

    internal void ApplyNativeVisualState()
    {
        UpdateDisplay();
    }

    private void ApplyIcon(string? rawIcon)
    {
        if (string.IsNullOrWhiteSpace(rawIcon))
            return;

        var icon = rawIcon.Trim();
        if (!icon.StartsWith(UIPackage.URL_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            var owner = PackageItem?.Owner;
            if (!string.IsNullOrWhiteSpace(owner?.Name))
            {
                var candidate = owner.GetItemByName(icon) ?? owner.GetItem(icon);
                if (candidate == null)
                {
                    candidate = owner.GetItems().FirstOrDefault(x =>
                        !string.IsNullOrWhiteSpace(x.Name) &&
                        (x.Name.Equals(icon, StringComparison.OrdinalIgnoreCase) ||
                         x.Name.EndsWith("/" + icon, StringComparison.OrdinalIgnoreCase) ||
                         x.Name.EndsWith(icon, StringComparison.OrdinalIgnoreCase)));
                }

                if (!string.IsNullOrWhiteSpace(candidate?.Id) && !string.IsNullOrWhiteSpace(owner.Id))
                {
                    icon = UIPackage.URL_PREFIX + owner.Id + candidate.Id;
                }
                else
                {
                    Game.Logger.LogWarning("[FGUI][Icon] image icon unresolved raw={Raw} owner={Owner}", icon, owner.Name);
                }
            }
        }

        if (icon.StartsWith(UIPackage.URL_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            var item = UIPackage.GetItemByURL(icon);
            if (item != null)
            {
                PackageItem = item;
                SourceWidth = item.Width;
                SourceHeight = item.Height;
                if (_width <= 0 || _height <= 0)
                    SetSize(SourceWidth, SourceHeight);
                item.Owner?.GetItemAsset(item);
                if (NativeObject == null)
                    Render.SCERenderContext.Instance.CreateNativeControl(this);
                Render.SCERenderContext.Instance.ApplyProperties(this);
                return;
            }
        }

        if (NativeObject == null)
            Render.SCERenderContext.Instance.CreateNativeControl(this);
        if (NativeObject != null)
            Render.SCERenderContext.Instance.Adapter?.SetBackgroundImage(NativeObject, icon.TrimStart('/'));
    }
    
    public void UpdateGear(int index)
    {
        // Gear4 是颜色相关
        if (index == 4)
        {
            UpdateDisplay();
        }
    }
}

public class GMovieClip : GImage
{
    private float _interval;
    private bool _swing;
    private float _repeatDelay;
    private bool _playing = true;
    private bool _loop = true;
    private int _frame;
    private int _frameCount;
    private float _frameElapsed;
    private bool _reversed;
    private int _repeatedCount;

    public bool Playing { get => _playing; set => _playing = value; }
    public bool Loop { get => _loop; set => _loop = value; }
    public int Frame
    {
        get => _frame;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(_frameCount - 1, 0));
            if (_frame == clamped)
            {
                return;
            }

            _frame = clamped;
            _frameElapsed = 0f;
            if (NativeObject != null)
            {
                Render.SCERenderContext.Instance.ApplyProperties(this);
            }
        }
    }
    public int FrameCount => _frameCount;
    public float Interval { get => _interval; set => _interval = value; }
    public bool Swing { get => _swing; set => _swing = value; }
    public float RepeatDelay { get => _repeatDelay; set => _repeatDelay = value; }

    public override void ConstructFromResource()
    {
        base.ConstructFromResource();
        var item = PackageItem;
        if (item == null)
        {
            return;
        }

        item.Owner?.GetItemAsset(item);
        _interval = item.Interval > 0 ? item.Interval : 0.1f;
        _swing = item.Swing;
        _repeatDelay = item.RepeatDelay;
        _frameCount = item.MovieClipFrames?.Count ?? 0;
        _frame = Math.Clamp(_frame, 0, Math.Max(_frameCount - 1, 0));
        _frameElapsed = 0f;
        _reversed = false;
        _repeatedCount = 0;
    }

    internal bool Advance(float deltaSeconds)
    {
        if (!_playing || _frameCount <= 0 || deltaSeconds <= 0f)
        {
            return false;
        }

        _frameElapsed += deltaSeconds;
        var changed = false;
        var guard = Math.Max(4, _frameCount * 2 + 2);
        while (guard-- > 0)
        {
            var frameDuration = GetCurrentFrameDuration();
            if (_frameElapsed < frameDuration)
            {
                break;
            }

            _frameElapsed -= frameDuration;
            StepForwardOneFrame();
            changed = true;
        }

        if (_frameElapsed > Math.Max(0.001f, _interval))
        {
            _frameElapsed = Math.Max(0.001f, _interval);
        }

        return changed;
    }

    internal string? GetCurrentFrameSpriteId()
    {
        var frames = PackageItem?.MovieClipFrames;
        if (frames == null || _frame < 0 || _frame >= frames.Count)
        {
            return null;
        }

        return frames[_frame].SpriteId;
    }

    private float GetCurrentFrameDuration()
    {
        var duration = Math.Max(0.001f, _interval);
        var frames = PackageItem?.MovieClipFrames;
        if (frames != null && _frame >= 0 && _frame < frames.Count)
        {
            duration += Math.Max(0f, frames[_frame].AddDelay);
        }

        if (_frame == 0 && _repeatedCount > 0)
        {
            duration += Math.Max(0f, _repeatDelay);
        }

        return duration;
    }

    private void StepForwardOneFrame()
    {
        if (_frameCount <= 1)
        {
            _frame = 0;
            if (!_loop)
            {
                _playing = false;
                return;
            }

            _repeatedCount++;
            return;
        }

        if (_swing)
        {
            if (_reversed)
            {
                _frame--;
                if (_frame <= 0)
                {
                    _frame = 0;
                    _repeatedCount++;
                    if (!_loop)
                    {
                        _playing = false;
                        return;
                    }

                    _reversed = false;
                }
            }
            else
            {
                _frame++;
                if (_frame > _frameCount - 1)
                {
                    if (!_loop)
                    {
                        _frame = _frameCount - 1;
                        _playing = false;
                        return;
                    }

                    _frame = Math.Max(0, _frameCount - 2);
                    _repeatedCount++;
                    _reversed = true;
                }
            }

            return;
        }

        _frame++;
        if (_frame > _frameCount - 1)
        {
            if (!_loop)
            {
                _frame = _frameCount - 1;
                _playing = false;
                return;
            }

            _frame = 0;
            _repeatedCount++;
        }
    }
}

public enum GraphType { Empty, Rect, Ellipse, Polygon, RegularPolygon }

public class GGraph : GObject, IColorGear
{
    private GraphType _type;
    private Color _fillColor = Color.Transparent;
    private Color _lineColor = Color.Black;
    private int _lineSize = 1;
    private float[] _cornerRadius = new float[4];
    private PointF[]? _polygonPoints;
    
    public GraphType Type => _type;
    public Color FillColor { get => _fillColor; set { _fillColor = value; UpdateGraphDisplay(); } }
    public Color LineColor { get => _lineColor; set { _lineColor = value; UpdateGraphDisplay(); } }
    public int LineSize { get => _lineSize; set { _lineSize = value; UpdateGraphDisplay(); } }
    public Color Color { get => _fillColor; set => FillColor = value; }

    public override void Setup_BeforeAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_BeforeAdd(buffer, beginPos);
        buffer.Seek(beginPos, 5);

        int type = buffer.ReadByte();
        _type = (GraphType)type;
        
        if (type != 0)
        {
            _lineSize = buffer.ReadInt();
            _lineColor = buffer.ReadColor();
            _fillColor = buffer.ReadColor();
            bool roundedRect = buffer.ReadBool();
            if (roundedRect)
            {
                for (int i = 0; i < 4; i++)
                    _cornerRadius[i] = buffer.ReadFloat();
            }

            if (type == 3) // Polygon
            {
                int cnt = buffer.ReadShort() / 2;
                _polygonPoints = new PointF[cnt];
                for (int i = 0; i < cnt; i++)
                    _polygonPoints[i] = new PointF(buffer.ReadFloat(), buffer.ReadFloat());
            }
            else if (type == 4) // RegularPolygon
            {
                buffer.ReadShort(); // sides
                buffer.ReadFloat(); // startAngle
                int cnt = buffer.ReadShort();
                for (int i = 0; i < cnt; i++)
                    buffer.ReadFloat(); // distances
            }
            
            // Apply the shape drawing after parsing - this triggers display updates
            if (_width > 0 && _height > 0)
            {
//                 Game.Logger.LogInformation($"[FGUI] Graph '{Name}' Setup_BeforeAdd: type={_type}, size=({_width}x{_height}), fillColor={_fillColor}");
            }
        }
    }
    
    public void DrawRect(float width, float height, int lineSize, Color lineColor, Color fillColor)
    {
        _type = GraphType.Rect;
        SetSize(width, height);
        _lineSize = lineSize;
        _lineColor = lineColor;
        _fillColor = fillColor;
        UpdateGraphDisplay();
    }
    
    public void DrawEllipse(float width, float height, Color fillColor)
    {
        _type = GraphType.Ellipse;
        SetSize(width, height);
        _fillColor = fillColor;
        UpdateGraphDisplay();
    }
    
    public void Clear()
    {
        _type = GraphType.Empty;
        _fillColor = Color.Transparent;
        UpdateGraphDisplay();
    }
    
    private void UpdateGraphDisplay()
    {
        if (NativeObject == null)
            Render.SCERenderContext.Instance.CreateNativeControl(this);
        if (NativeObject != null)
        {
            var adapter = Render.SCERenderContext.Instance.Adapter;
            if (adapter == null)
            {
                return;
            }

            if (_type == GraphType.Ellipse)
            {
                adapter.SetCanvasEllipse(NativeObject, _fillColor);
                return;
            }

            adapter.ClearCanvasRenderState(NativeObject);
            adapter.SetBackgroundColor(NativeObject, _fillColor);
            if (_type == GraphType.Rect && _cornerRadius[0] > 0)
                adapter.SetCornerRadius(NativeObject, _cornerRadius[0]);
            else
                adapter.SetCornerRadius(NativeObject, 0);
        }
    }
}
#endif

