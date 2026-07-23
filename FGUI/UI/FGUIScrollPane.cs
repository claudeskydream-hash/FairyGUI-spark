#if CLIENT
using System.Drawing;
using FairyGUI;
using FairyGUI.Render;
using FairyGUI.Utils;

namespace FairyGUI;

public class ScrollPane
{
    public GComponent? Owner { get; internal set; }

    private ScrollType _scrollType = ScrollType.Vertical;
    private float _scrollSpeed = 1;
    private bool _mouseWheelEnabled = true;
    private float _decelerationRate = 0.967f;
    private bool _touchEffect = true;
    private bool _bouncebackEffect = true;
    private bool _inertiaDisabled;
    private float _scrollBarMargin;
    private ScrollBarDisplayType _scrollBarDisplayType = ScrollBarDisplayType.Default;

    private float _viewWidth, _viewHeight;
    private float _contentWidth, _contentHeight;
    private float _xPos, _yPos;
    private float _velocityX, _velocityY;
    // 裁剪边缘羽化(FGUI clipSoftness)：靠近视口边缘 softness 像素内的内容按 Alpha 渐隐。
    // 原生无软裁剪能力，改由逐项调整子对象 Alpha 近似(见 GComponent.UpdateClipSoftness)。
    private float _clipSoftnessX, _clipSoftnessY;
    private bool _scrolling;
    private bool _dragging;
    private PointF _lastTouchPos;
    private float _lastTouchTime;
    private bool _isHolding;

    // Scrollbars
    private GObject? _hScrollBar;
    private GObject? _vScrollBar;
    private bool _hScrollBarVisible;
    private bool _vScrollBarVisible;

    // Tweening
    private GTweener? _tweener;
    private bool _tweening;

    // Pagination
    private bool _pageMode;
    private Controller? _pageController;
    private float _pageWidth, _pageHeight;
    private bool _snapToItem;

    // Pull to refresh
    private GComponent? _header;
    private GComponent? _footer;
    private int _headerLockedSize;
    private int _footerLockedSize;

    // Constants
    private const float SCROLL_THRESHOLD = 5f;

    // ===== 过界（橡皮筋）阻尼 =====
    /// <summary>
    /// 过界阻尼范围占视口尺寸的比例。往外拖越深阻力越大，位移会渐近于 视口尺寸×该比例（拖不动），
    /// 即柔性的最大可过界距离。往回拖始终 1:1 立即跟手，不受此影响。默认 0.5。
    /// </summary>
    private const float OVERSCROLL_DAMP_RANGE_RATIO = 0.5f;

    // ===== 惯性手感旋钮（想要更长惯性就调这里）=====
    /// <summary>惯性滑行距离系数：越大，松手后滑得越远。默认 0.3；当前用"明显长"预设 0.7（可调 0.6~1.0）。</summary>
    private const float INERTIA_DISTANCE_FACTOR = 0.3f;
    /// <summary>触发惯性所需的最小松手速度（低于此值不滑行）。默认 100。</summary>
    private const float INERTIA_VELOCITY_THRESHOLD = 100f;
    /// <summary>惯性动画时长换算：滑行距离 / 该值 = 秒数。越小，动画越慢越长。默认 500；当前预设 400。</summary>
    private const float INERTIA_DURATION_DIVISOR = 600f;
    /// <summary>惯性/回弹动画的最短、最长时长（秒）。想更长惯性把最大值加大；默认 0.1~0.5，当前预设上限 1.2。</summary>
    private const float SCROLL_ANIM_MIN_DURATION = 0.2f;
    private const float SCROLL_ANIM_MAX_DURATION = 0.8f;

    public ScrollType ScrollType => _scrollType;
    public float PosX { get => _xPos; set => SetPos(value, _yPos, false); }
    public float PosY { get => _yPos; set => SetPos(_xPos, value, false); }
    public float PercentX 
    { 
        get => _contentWidth > _viewWidth ? _xPos / (_contentWidth - _viewWidth) : 0; 
        set => SetPos(value * Math.Max(0, _contentWidth - _viewWidth), _yPos, false); 
    }
    public float PercentY 
    { 
        get => _contentHeight > _viewHeight ? _yPos / (_contentHeight - _viewHeight) : 0; 
        set => SetPos(_xPos, value * Math.Max(0, _contentHeight - _viewHeight), false); 
    }
    public float ContentWidth => _contentWidth;
    public float ContentHeight => _contentHeight;
    public float ViewWidth => _viewWidth;
    public float ViewHeight => _viewHeight;
    public bool IsScrolling => _scrolling;
    public bool IsDragging => _dragging;
    /// <summary>裁剪边缘羽化像素（X/Y）。0 表示不羽化。</summary>
    public float ClipSoftnessX => _clipSoftnessX;
    public float ClipSoftnessY => _clipSoftnessY;
    public void SetClipSoftness(float x, float y)
    {
        x = float.IsFinite(x) ? Math.Max(0f, x) : 0f;
        y = float.IsFinite(y) ? Math.Max(0f, y) : 0f;
        if (_clipSoftnessX == x && _clipSoftnessY == y)
            return;

        _clipSoftnessX = x;
        _clipSoftnessY = y;
        // 既要让初始静止列表立刻出现羽化，也要在编辑器配置/运行时关闭羽化时恢复项目的原始 Alpha。
        Owner?.UpdateClipSoftness();
    }
    /// <summary>是否正在跑惯性/回弹动画。此期间 FGUI 物理为唯一来源，必须忽略原生回读，否则回声会打断 tween 造成"打哆嗦"。</summary>
    public bool IsTweening => _tweening;
    public bool TouchEffect { get => _touchEffect; set => _touchEffect = value; }
    public bool BouncebackEffect { get => _bouncebackEffect; set => _bouncebackEffect = value; }
    public bool MouseWheelEnabled { get => _mouseWheelEnabled; set => _mouseWheelEnabled = value; }
    public float DecelerationRate { get => _decelerationRate; set => _decelerationRate = value; }
    public float ScrollSpeed { get => _scrollSpeed; set => _scrollSpeed = value; }
    
    public float ScrollingPosX => _xPos;
    public float ScrollingPosY => _yPos;

    /// <summary>FGUI 编辑器里配置的滚动条显示方式（Default/Visible/Auto/Hidden）。</summary>
    public ScrollBarDisplayType ScrollBarDisplay => _scrollBarDisplayType;

    // Pagination properties
    public bool PageMode
    {
        get => _pageMode;
        set
        {
            if (_pageMode == value)
                return;

            _pageMode = value;
            if (_pageMode)
            {
                _pageWidth = _viewWidth;
                _pageHeight = _viewHeight;
            }
        }
    }
    public Controller? PageController { get => _pageController; set => _pageController = value; }
    public bool SnapToItem { get => _snapToItem; set => _snapToItem = value; }

    public int CurrentPageX
    {
        get
        {
            if (!_pageMode || _pageWidth <= 0)
                return 0;

            int page = (int)Math.Floor(_xPos / _pageWidth);
            if (_xPos - page * _pageWidth > _pageWidth * 0.5f)
                page++;

            return page;
        }
        set
        {
            if (!_pageMode)
                return;

            Owner?.EnsureBoundsCorrect();

            if (_contentWidth > _viewWidth)
                SetPos(value * _pageWidth, _yPos, false);
        }
    }

    public int CurrentPageY
    {
        get
        {
            if (!_pageMode || _pageHeight <= 0)
                return 0;

            int page = (int)Math.Floor(_yPos / _pageHeight);
            if (_yPos - page * _pageHeight > _pageHeight * 0.5f)
                page++;

            return page;
        }
        set
        {
            if (!_pageMode)
                return;

            Owner?.EnsureBoundsCorrect();

            if (_contentHeight > _viewHeight)
                SetPos(_xPos, value * _pageHeight, false);
        }
    }

    // Pull to refresh properties
    public GComponent? Header { get => _header; set => _header = value; }
    public GComponent? Footer { get => _footer; set => _footer = value; }

    public EventListener OnPullDownRelease => GetOrCreateListener("onPullDownRelease");
    public EventListener OnPullUpRelease => GetOrCreateListener("onPullUpRelease");

    private readonly Dictionary<string, EventListener> _listeners = new();

    private EventListener GetOrCreateListener(string type)
    {
        if (!_listeners.TryGetValue(type, out var listener))
        {
            listener = new EventListener();
            _listeners[type] = listener;
        }
        return listener;
    }

    public void SetPos(float xv, float yv, bool animate = false)
    {
        if (_tweening)
        {
            KillTween();
        }

        xv = ClampX(xv);
        yv = ClampY(yv);

        if (animate && (_xPos != xv || _yPos != yv))
        {
            float startX = _xPos;
            float startY = _yPos;
            float endX = xv;
            float endY = yv;
            
            float dx = Math.Abs(endX - startX);
            float dy = Math.Abs(endY - startY);
            float duration = Math.Max(dx, dy) / INERTIA_DURATION_DIVISOR; // Speed based duration
            duration = Math.Clamp(duration, SCROLL_ANIM_MIN_DURATION, SCROLL_ANIM_MAX_DURATION);
            
            _tweening = true;
            _tweener = GTween.To(new PointF(startX, startY), new PointF(endX, endY), duration)
                .SetEase(EaseType.QuadOut)
                .OnUpdate(t =>
                {
                    _xPos = t.Value.X;
                    _yPos = t.Value.Y;
                    UpdateScrollPosition();
                })
                .OnComplete(t =>
                {
                    _tweening = false;
                    _tweener = null;
                    OnScrollEnd();
                });
        }
        else if (_xPos != xv || _yPos != yv)
        {
            _xPos = xv;
            _yPos = yv;
            UpdateScrollPosition();
        }
    }

    public void ScrollTop(bool animate = false) => SetPos(_xPos, 0, animate);
    public void ScrollBottom(bool animate = false) => SetPos(_xPos, Math.Max(0, _contentHeight - _viewHeight), animate);
    public void ScrollLeft(bool animate = false) => SetPos(0, _yPos, animate);
    public void ScrollRight(bool animate = false) => SetPos(Math.Max(0, _contentWidth - _viewWidth), _yPos, animate);

    private const float DEFAULT_SCROLL_STEP = 40f;

    /// <summary>
    /// 向上滚动
    /// </summary>
    public void ScrollUp(float ratio = 1, bool animate = false)
    {
        if (_pageMode)
            SetPos(_xPos, _yPos - _pageHeight * ratio, animate);
        else
            SetPos(_xPos, _yPos - DEFAULT_SCROLL_STEP * ratio, animate);
    }

    /// <summary>
    /// 向下滚动
    /// </summary>
    public void ScrollDown(float ratio = 1, bool animate = false)
    {
        if (_pageMode)
            SetPos(_xPos, _yPos + _pageHeight * ratio, animate);
        else
            SetPos(_xPos, _yPos + DEFAULT_SCROLL_STEP * ratio, animate);
    }

    /// <summary>
    /// 向左滚动
    /// </summary>
    public void ScrollLeftStep(float ratio = 1, bool animate = false)
    {
        if (_pageMode)
            SetPos(_xPos - _pageWidth * ratio, _yPos, animate);
        else
            SetPos(_xPos - DEFAULT_SCROLL_STEP * ratio, _yPos, animate);
    }

    /// <summary>
    /// 向右滚动
    /// </summary>
    public void ScrollRightStep(float ratio = 1, bool animate = false)
    {
        if (_pageMode)
            SetPos(_xPos + _pageWidth * ratio, _yPos, animate);
        else
            SetPos(_xPos + DEFAULT_SCROLL_STEP * ratio, _yPos, animate);
    }

    /// <summary>
    /// 设置当前X轴页码（分页模式）
    /// </summary>
    public void SetCurrentPageX(int value, bool animate = false)
    {
        if (!_pageMode)
            return;

        Owner?.EnsureBoundsCorrect();

        if (_contentWidth > _viewWidth)
            SetPos(value * _pageWidth, _yPos, animate);
    }

    /// <summary>
    /// 设置当前Y轴页码（分页模式）
    /// </summary>
    public void SetCurrentPageY(int value, bool animate = false)
    {
        if (!_pageMode)
            return;

        Owner?.EnsureBoundsCorrect();

        if (_contentHeight > _viewHeight)
            SetPos(_xPos, value * _pageHeight, animate);
    }

    /// <summary>
    /// 锁定Header显示
    /// </summary>
    public void LockHeader(int size)
    {
        if (_headerLockedSize == size)
            return;

        _headerLockedSize = size;

        if (_header != null)
        {
            _header.Visible = size > 0;
            if (size > 0)
                _header.Height = size;
        }
    }

    /// <summary>
    /// 锁定Footer显示
    /// </summary>
    public void LockFooter(int size)
    {
        if (_footerLockedSize == size)
            return;

        _footerLockedSize = size;

        if (_footer != null)
        {
            _footer.Visible = size > 0;
            if (size > 0)
                _footer.Height = size;
        }
    }

    /// <summary>
    /// 滚动到指定对象可见
    /// </summary>
    /// <param name="obj">目标对象（可以是任何舞台上的对象，不限于此容器的直接子对象）</param>
    public void ScrollToView(GObject obj)
    {
        ScrollToView(obj, false, false);
    }

    /// <summary>
    /// 滚动到指定对象可见
    /// </summary>
    /// <param name="obj">目标对象（可以是任何舞台上的对象，不限于此容器的直接子对象）</param>
    /// <param name="animate">是否使用动画</param>
    public void ScrollToView(GObject obj, bool animate)
    {
        ScrollToView(obj, animate, false);
    }

    /// <summary>
    /// 滚动到指定对象可见
    /// </summary>
    /// <param name="obj">目标对象（可以是任何舞台上的对象，不限于此容器的直接子对象）</param>
    /// <param name="animate">是否使用动画</param>
    /// <param name="setFirst">如果为true，滚动到顶部/左侧；如果为false，滚动到视图中的任意位置</param>
    public void ScrollToView(GObject obj, bool animate, bool setFirst)
    {
        if (obj == null || Owner == null)
            return;

        // 确保边界正确
        Owner.EnsureBoundsCorrect();

        // 获取对象的矩形区域
        RectangleF rect = new RectangleF(obj.X, obj.Y, obj.Width, obj.Height);

        // 如果对象不是Owner的直接子对象，需要转换坐标
        if (obj.Parent != null && obj.Parent != Owner)
        {
            // 转换到Owner的本地坐标系
            // 简化实现：累加父对象的位置
            var parent = obj.Parent;
            while (parent != null && parent != Owner)
            {
                rect.X += parent.X;
                rect.Y += parent.Y;
                parent = parent.Parent;
            }
        }

        ScrollToView(rect, animate, setFirst);
    }

    public void ScrollToView(RectangleF rect, bool animate = false, bool setFirst = false)
    {
        float targetX = _xPos;
        float targetY = _yPos;

        if (_scrollType == ScrollType.Vertical || _scrollType == ScrollType.Both)
        {
            if (setFirst || rect.Y < _yPos || rect.Height >= _viewHeight)
                targetY = rect.Y;
            else if (rect.Bottom > _yPos + _viewHeight)
                targetY = rect.Bottom - _viewHeight;
        }

        if (_scrollType == ScrollType.Horizontal || _scrollType == ScrollType.Both)
        {
            if (setFirst || rect.X < _xPos || rect.Width >= _viewWidth)
                targetX = rect.X;
            else if (rect.Right > _xPos + _viewWidth)
                targetX = rect.Right - _viewWidth;
        }

        SetPos(targetX, targetY, animate);
    }

    /// <summary>
    /// 检查子对象是否在视图中可见
    /// </summary>
    /// <param name="obj">对象必须是此容器的直接子对象</param>
    /// <returns>如果对象在视图中可见返回true</returns>
    public bool IsChildInView(GObject obj)
    {
        if (obj == null || Owner == null)
            return false;

        // 检查垂直方向
        if (_scrollType == ScrollType.Vertical || _scrollType == ScrollType.Both)
        {
            if (_contentHeight > _viewHeight)
            {
                // 对象相对于视图的位置
                float objTop = obj.Y;
                float objBottom = obj.Y + obj.Height;
                float viewTop = _yPos;
                float viewBottom = _yPos + _viewHeight;

                // 如果对象完全在视图外
                if (objBottom <= viewTop || objTop >= viewBottom)
                    return false;
            }
        }

        // 检查水平方向
        if (_scrollType == ScrollType.Horizontal || _scrollType == ScrollType.Both)
        {
            if (_contentWidth > _viewWidth)
            {
                // 对象相对于视图的位置
                float objLeft = obj.X;
                float objRight = obj.X + obj.Width;
                float viewLeft = _xPos;
                float viewRight = _xPos + _viewWidth;

                // 如果对象完全在视图外
                if (objRight <= viewLeft || objLeft >= viewRight)
                    return false;
            }
        }

        return true;
    }

    public void CancelDragging()
    {
        _dragging = false;
        _isHolding = false;
    }

    /// <summary>
    /// 逐帧、按方向的过界阻尼。
    /// <para>pos 为当前显示位置，delta 为本帧要施加的位移(界内 1:1 意义下的增量)，[0,max] 为界内范围，dim 为视口尺寸。</para>
    /// <list type="bullet">
    /// <item>界内、以及往回拖(减小越界深度)：1:1 直接跟手，保证反向立刻响应。</item>
    /// <item>往外拖(加深越界)：位移按阻尼系数缩放，越界越深系数越小，渐近于 <c>dim·OVERSCROLL_DAMP_RANGE_RATIO</c>，
    /// 即位移速度趋近于 0、拖不动，形成柔性上限。</item>
    /// </list>
    /// </summary>
    private static float ResistedMove(float pos, float delta, float max, float dim)
    {
        if (delta == 0f || dim <= 0f)
            return pos + delta;

        float range = dim * OVERSCROLL_DAMP_RANGE_RATIO;
        if (range <= 0f)
            return pos;

        // 移动回内容区的这一段永远 1:1；若单帧移动跨过另一侧边界，仅对真正越界的剩余量施加阻尼。
        // 这也修复了旧实现从边界第一帧就能无阻力跳出一大截的问题。
        if (delta > 0f)
        {
            if (pos < 0f)
            {
                float backToEdge = -pos;
                if (delta <= backToEdge)
                    return pos + delta;
                delta -= backToEdge;
                pos = 0f;
            }

            if (pos < max)
            {
                float toFarEdge = max - pos;
                if (delta <= toFarEdge)
                    return pos + delta;
                delta -= toFarEdge;
                pos = max;
            }

            return max + ApplyOutwardResistance(pos - max, delta, range);
        }

        float magnitude = -delta;
        if (pos > max)
        {
            float backToEdge = pos - max;
            if (magnitude <= backToEdge)
                return pos + delta;
            magnitude -= backToEdge;
            pos = max;
        }

        if (pos > 0f)
        {
            float toNearEdge = pos;
            if (magnitude <= toNearEdge)
                return pos + delta;
            magnitude -= toNearEdge;
            pos = 0f;
        }

        return -ApplyOutwardResistance(-pos, magnitude, range);
    }

    /// <summary>积分形式的阻尼：导数随越界深度线性衰减，因而首帧跨界也会受阻、越拖越难拖。</summary>
    private static float ApplyOutwardResistance(float overshoot, float outwardDelta, float range)
    {
        if (overshoot >= range)
            return overshoot;

        // d(overshoot)/d(drag) = 1 - overshoot/range 的解析解；接近 range 时速度自然趋近于 0。
        return range - (range - Math.Max(0f, overshoot)) * MathF.Exp(-outwardDelta / range);
    }

    private float ClampX(float x)
    {
        float max = _contentWidth - _viewWidth;
        if (max <= 0) return 0;
        return Math.Clamp(x, 0, max);
    }

    private float ClampY(float y)
    {
        float max = _contentHeight - _viewHeight;
        if (max <= 0) max = 0;

        float min = 0;
        if (_headerLockedSize > 0) min = -_headerLockedSize;
        if (_footerLockedSize > 0) max += _footerLockedSize;

        return Math.Clamp(y, min, max);
    }

    private void UpdateScrollPosition()
    {
        Owner?.DispatchEvent("onScroll", null);
        UpdateScrollBars();

        // 裁剪边缘羽化：滚动位置一变，就按各子对象到视口边缘的距离刷新其 Alpha。
        if ((_clipSoftnessX > 0 || _clipSoftnessY > 0) && Owner != null)
        {
            Owner.UpdateClipSoftness();
        }

        // Update page controller if in page mode
        if (_pageMode)
            UpdatePageController();

        // 把 FGUI 算出的滚动位置同步到原生面板：
        // 拖动、松手后的惯性/回弹 tween 都经过这里，从而让 FGUI 物理统一驱动可视滚动，
        // 内外列表手感一致（原生不再各自产生惯性）。
        if (Owner != null)
        {
            SCERenderContext.Instance.SyncScrollPaneToNative(Owner);
        }
    }

    /// <summary>
    /// 更新页面控制器
    /// </summary>
    private void UpdatePageController()
    {
        if (_pageController != null && !_pageController.Changing)
        {
            int index;
            if (_scrollType == ScrollType.Horizontal)
                index = CurrentPageX;
            else
                index = CurrentPageY;

            if (index < _pageController.PageCount)
            {
                var c = _pageController;
                _pageController = null; // 防止HandleControllerChanged的调用
                c.SelectedIndex = index;
                _pageController = c;
            }
        }
    }

    private void UpdateScrollBars()
    {
        if (_hScrollBar != null)
        {
            _hScrollBar.Visible = _contentWidth > _viewWidth;
            // Update scrollbar position/size
        }
        if (_vScrollBar != null)
        {
            _vScrollBar.Visible = _contentHeight > _viewHeight;
            // Update scrollbar position/size
        }
    }

    private void OnScrollEnd()
    {
        _scrolling = false;
        Owner?.DispatchEvent("onScrollEnd", null);
    }

    private void KillTween()
    {
        if (_tweener != null)
        {
            _tweener.Kill();
            _tweener = null;
        }
        _tweening = false;
    }

    // Touch/Mouse handling
    public void OnTouchBegin(float x, float y)
    {
        if (!_touchEffect) return;
        
        KillTween();
        _dragging = true;
        _isHolding = true;
        _lastTouchPos = new PointF(x, y);
        _lastTouchTime = GetTime();
        _velocityX = 0;
        _velocityY = 0;
    }

    public void OnTouchMove(float x, float y)
    {
        if (!_dragging) return;

        float dx = x - _lastTouchPos.X;
        float dy = y - _lastTouchPos.Y;

        // 只响应本列表能滚动的轴：竖向列表忽略横向位移，横向列表忽略纵向位移。
        // 否则竖向列表在纵向拖动时，手指的横向分量会让 X 轴产生过界(maxX=0 时任何 dx 都算过界)，
        // 表现为不该有的"左右回弹"。
        if (_scrollType == ScrollType.Vertical) dx = 0;
        else if (_scrollType == ScrollType.Horizontal) dy = 0;

        float now = GetTime();
        float dt = now - _lastTouchTime;
        if (dt > 0)
        {
            _velocityX = dx / dt * 0.5f + _velocityX * 0.5f;
            _velocityY = dy / dt * 0.5f + _velocityY * 0.5f;
        }
        
        _lastTouchPos = new PointF(x, y);
        _lastTouchTime = now;

        float maxX = Math.Max(0, _contentWidth - _viewWidth);
        float maxY = Math.Max(0, _contentHeight - _viewHeight);

        float newX, newY;
        if (_bouncebackEffect)
        {
            // 逐帧、按方向的阻尼：往外拖(加深越界)有阻力，越深越大、到极限时速度为 0 拖不动；
            // 往回拖(减小越界)立即 1:1 跟手，避免深度越界时“往回拖拖不动”。
            newX = ResistedMove(_xPos, -dx, maxX, _viewWidth);
            newY = ResistedMove(_yPos, -dy, maxY, _viewHeight);
        }
        else
        {
            newX = ClampX(_xPos - dx);
            newY = ClampY(_yPos - dy);
        }

        _xPos = newX;
        _yPos = newY;
        _scrolling = true;
        UpdateScrollPosition();
    }

    public void OnTouchEnd()
    {
        if (!_dragging) return;
        _dragging = false;
        _isHolding = false;

        // 诊断：松手时的速度/惯性开关，用于定位"惯性很小/没变化"
//         Game.Logger.LogInformation(
//             "[FGUI][Scroll] OnTouchEnd velX={VX} velY={VY} 阈值={Th} inertiaDisabled={Dis} 距离系数={Fac} 时长上限={Dur}",
//             _velocityX, _velocityY, INERTIA_VELOCITY_THRESHOLD, _inertiaDisabled,
//             INERTIA_DISTANCE_FACTOR, SCROLL_ANIM_MAX_DURATION);

        // Check for pull to refresh
        // 注意：事件触发不依赖于 _header/_footer 是否存在，用户可能只注册事件
        if (_scrollType == ScrollType.Vertical || _scrollType == ScrollType.Both)
        {
            float max = _contentHeight - _viewHeight;
            if (max <= 0) max = 0;

            if (_yPos < -SCROLL_THRESHOLD)
            {
                // 下拉刷新事件
                _listeners.TryGetValue("onPullDownRelease", out var listener);
                if (listener != null && !listener.IsEmpty)
                    Owner?.DispatchEvent("onPullDownRelease", null);
            }
            else if (_yPos > max + SCROLL_THRESHOLD)
            {
                // 上拉加载事件
                _listeners.TryGetValue("onPullUpRelease", out var listener);
                if (listener != null && !listener.IsEmpty)
                    Owner?.DispatchEvent("onPullUpRelease", null);
            }
        }
        
        // 水平方向的下拉刷新
        if (_scrollType == ScrollType.Horizontal || _scrollType == ScrollType.Both)
        {
            float max = _contentWidth - _viewWidth;
            if (max <= 0) max = 0;

            if (_xPos < -SCROLL_THRESHOLD)
            {
                _listeners.TryGetValue("onPullDownRelease", out var listener);
                if (listener != null && !listener.IsEmpty)
                    Owner?.DispatchEvent("onPullDownRelease", null);
            }
            else if (_xPos > max + SCROLL_THRESHOLD)
            {
                _listeners.TryGetValue("onPullUpRelease", out var listener);
                if (listener != null && !listener.IsEmpty)
                    Owner?.DispatchEvent("onPullUpRelease", null);
            }
        }

        // Apply inertia
        if (!_inertiaDisabled && (Math.Abs(_velocityX) > INERTIA_VELOCITY_THRESHOLD || Math.Abs(_velocityY) > INERTIA_VELOCITY_THRESHOLD))
        {
            float targetX = _xPos - _velocityX * INERTIA_DISTANCE_FACTOR;
            float targetY = _yPos - _velocityY * INERTIA_DISTANCE_FACTOR;

            // Snap to page if in page mode
            if (_pageMode || _snapToItem)
            {
                if (_scrollType == ScrollType.Horizontal && _pageWidth > 0)
                {
                    int page = (int)Math.Round(targetX / _pageWidth);
                    targetX = page * _pageWidth;
                }
                else if (_scrollType == ScrollType.Vertical && _pageHeight > 0)
                {
                    int page = (int)Math.Round(targetY / _pageHeight);
                    targetY = page * _pageHeight;
                }
            }

            SetPos(targetX, targetY, true);
        }
        else
        {
            // Snap to page if in page mode
            if (_pageMode || _snapToItem)
            {
                float targetX = _xPos;
                float targetY = _yPos;

                if (_scrollType == ScrollType.Horizontal && _pageWidth > 0)
                {
                    int page = (int)Math.Round(_xPos / _pageWidth);
                    targetX = page * _pageWidth;
                }
                else if (_scrollType == ScrollType.Vertical && _pageHeight > 0)
                {
                    int page = (int)Math.Round(_yPos / _pageHeight);
                    targetY = page * _pageHeight;
                }

                if (targetX != _xPos || targetY != _yPos)
                {
                    SetPos(targetX, targetY, true);
                    return;
                }
            }

            // Bounceback if needed
            float clampedX = ClampX(_xPos);
            float clampedY = ClampY(_yPos);
            
            // Adjust for locked header/footer
            float max = _contentHeight - _viewHeight;
            if (max <= 0) max = 0;
            
            if (_headerLockedSize > 0 && clampedY > -_headerLockedSize && clampedY < 0.1f)
                clampedY = -_headerLockedSize;
                
            if (_footerLockedSize > 0 && clampedY < max + _footerLockedSize && clampedY > max - 0.1f)
                clampedY = max + _footerLockedSize;

            if (_bouncebackEffect && (clampedX != _xPos || clampedY != _yPos))
            {
                SetPos(clampedX, clampedY, true);
            }
            else
            {
                OnScrollEnd();
            }
        }
    }

    public void OnMouseWheel(float delta)
    {
        if (!_mouseWheelEnabled) return;
        
        if (_scrollType == ScrollType.Vertical || _scrollType == ScrollType.Both)
        {
            SetPos(_xPos, _yPos - delta * _scrollSpeed * 40, false);
        }
        else
        {
            SetPos(_xPos - delta * _scrollSpeed * 40, _yPos, false);
        }
    }

    // 时间基准：用相对启动时刻的小数值，避免 float 存不下 Unix 秒数(约 17.7 亿)的亚秒精度，
    // 否则 dt 恒为 0、速度永远不累积、松手没有惯性。
    private static readonly long _timeBaseMs = Environment.TickCount64;
    private static float GetTime() => (Environment.TickCount64 - _timeBaseMs) / 1000f;

    public void SetContentSize(float width, float height)
    {
        if (_contentWidth != width || _contentHeight != height)
        {
            var oldOverflowX = Math.Max(0f, _contentWidth - _viewWidth);
            var oldOverflowY = Math.Max(0f, _contentHeight - _viewHeight);
            _contentWidth = width;
            _contentHeight = height;
            SetPos(_xPos, _yPos, false);

            // 内容尺寸变化(如首次绑定数据、项数更新)后即使滚动位置没变，也刷新一次羽化，
            // 让静止列表一开始就带软边(否则要等第一次滚动才生效)。
            if ((_clipSoftnessX > 0 || _clipSoftnessY > 0) && Owner != null)
            {
                Owner.UpdateClipSoftness();
            }

            // Content overflow state can change after data binding (e.g. virtual list NumItems update).
            // Re-apply native scrollability only when scrollable-state flips, otherwise it can cause
            // feedback loops in virtual list scroll syncing.
            if (Owner?.NativeObject != null)
            {
                var newOverflowX = Math.Max(0f, _contentWidth - _viewWidth);
                var newOverflowY = Math.Max(0f, _contentHeight - _viewHeight);
                const float epsilon = 0.01f;
                var wasScrollable = oldOverflowX > epsilon || oldOverflowY > epsilon;
                var nowScrollable = newOverflowX > epsilon || newOverflowY > epsilon;
                if (wasScrollable != nowScrollable)
                {
                    SCERenderContext.Instance.UpdateSize(Owner);
                }
            }
        }
    }

    public void SetViewSize(float width, float height)
    {
        if (_viewWidth != width || _viewHeight != height)
        {
            _viewWidth = width;
            _viewHeight = height;
            if (_pageMode)
            {
                _pageWidth = width;
                _pageHeight = height;
            }
            SetPos(_xPos, _yPos, false);

            // 视口尺寸变化即使没有改变滚动位置，也会改变子项到软裁剪边缘的距离。
            if ((_clipSoftnessX > 0 || _clipSoftnessY > 0) && Owner != null)
            {
                Owner.UpdateClipSoftness();
            }
        }
    }

    public void Setup(ByteBuffer buffer)
    {
        // 严格对齐 FairyGUI 官方二进制格式（HelpProject/FairyGUI-unity/ScrollPane.cs）：
        //   scrollType:byte, scrollBarDisplay:byte(独立!), flags:int,
        //   scrollBarMargin: bool(+int×4), 然后依次 4 个 ReadS(竖/横滚动条、header、footer 资源)。
        // 旧实现把 scrollBarDisplay 那一字节漏读、并把 display 从 flags&3 里瞎猜，导致 scrollBar="hidden" 不生效、
        // 且后续字节整体错位。此块由调用方 Seek 到滚动块并在返回后恢复 Position，故本方法内错位不影响其余解析。
        _scrollType = (ScrollType)buffer.ReadByte();
        var scrollBarDisplay = (ScrollBarDisplayType)buffer.ReadByte();
        int flags = buffer.ReadInt();

        if (buffer.ReadBool())
        {
            // scrollBarMargin: top/bottom/left/right（移植层仅留 top 作近似，其余按序消费以对齐）。
            _scrollBarMargin = buffer.ReadInt();
            buffer.ReadInt();
            buffer.ReadInt();
            buffer.ReadInt();
        }

        // 竖/横滚动条资源 + header/footer 资源：移植层暂不使用，但必须按序读取以对齐缓冲。
        buffer.ReadS();
        buffer.ReadS();
        buffer.ReadS();
        buffer.ReadS();

        // flags 位语义（官方）：1=displayOnLeft,2=snapToItem,4=displayInDemand,8=pageMode,
        // 16/32=touchEffect 开/关,64/128=bounceback 开/关,256=inertiaDisabled。
        _snapToItem = (flags & 2) != 0;
        _pageMode = (flags & 8) != 0;

        if ((flags & 16) != 0)
            _touchEffect = true;
        else if ((flags & 32) != 0)
            _touchEffect = false;

        if ((flags & 64) != 0)
            _bouncebackEffect = true;
        else if ((flags & 128) != 0)
            _bouncebackEffect = false;

        _inertiaDisabled = (flags & 256) != 0;

        // Default：官方在移动端解析为 Auto；本移植面向触屏，统一按 Auto 处理（非 Hidden，即显示）。
        if (scrollBarDisplay == ScrollBarDisplayType.Default)
            scrollBarDisplay = ScrollBarDisplayType.Auto;
        _scrollBarDisplayType = scrollBarDisplay;

        if (Owner != null)
        {
            _viewWidth = Owner.Width;
            _viewHeight = Owner.Height;

            // Initialize page size
            if (_pageMode)
            {
                _pageWidth = _viewWidth;
                _pageHeight = _viewHeight;
            }
        }
    }

    public void Dispose()
    {
        KillTween();
        Owner = null;
    }
}
#endif


