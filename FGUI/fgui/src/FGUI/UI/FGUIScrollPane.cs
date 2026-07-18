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
    private const float PULL_RATIO = 0.5f;
    private const float SCROLL_THRESHOLD = 5f;

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
    /// <summary>过界(橡皮筋)最大可拖距离占视口尺寸的比例。跟手拖到此极限就不再继续，松手弹回。默认 0.3。</summary>
    private const float MAX_OVERSCROLL_RATIO = 0.5f;

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

        float newX = _xPos - dx;
        float newY = _yPos - dy;

        // Apply bounce effect
        if (_bouncebackEffect)
        {
            float maxX = Math.Max(0, _contentWidth - _viewWidth);
            float maxY = Math.Max(0, _contentHeight - _viewHeight);
            // 过界极限：跟手拖到 视口尺寸×比例 就封顶，不再继续下拉。
            float limitX = _viewWidth * MAX_OVERSCROLL_RATIO;
            float limitY = _viewHeight * MAX_OVERSCROLL_RATIO;

            if (newX < 0) newX = Math.Max(newX * PULL_RATIO, -limitX);
            else if (newX > maxX) newX = Math.Min(maxX + (newX - maxX) * PULL_RATIO, maxX + limitX);

            if (newY < 0) newY = Math.Max(newY * PULL_RATIO, -limitY);
            else if (newY > maxY) newY = Math.Min(maxY + (newY - maxY) * PULL_RATIO, maxY + limitY);
        }
        else
        {
            newX = ClampX(newX);
            newY = ClampY(newY);
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
        Game.Logger.LogInformation(
            "[FGUI][Scroll] OnTouchEnd velX={VX} velY={VY} 阈值={Th} inertiaDisabled={Dis} 距离系数={Fac} 时长上限={Dur}",
            _velocityX, _velocityY, INERTIA_VELOCITY_THRESHOLD, _inertiaDisabled,
            INERTIA_DISTANCE_FACTOR, SCROLL_ANIM_MAX_DURATION);

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
        }
    }

    public void Setup(ByteBuffer buffer)
    {
        _scrollType = (ScrollType)buffer.ReadByte();
        int scrollBarFlags = buffer.ReadInt();
        _scrollBarMargin = buffer.ReadFloat();
        _scrollBarDisplayType = (ScrollBarDisplayType)(scrollBarFlags & 0x03);

        if (buffer.ReadBool()) _scrollSpeed = buffer.ReadFloat();
        if (buffer.ReadBool()) buffer.ReadS(); // vScrollBar resource
        if (buffer.ReadBool()) buffer.ReadS(); // hScrollBar resource

        // Align with FairyGUI Unity flag semantics:
        // bit1=snapToItem, bit3=pageMode, bit4/5=touchEffect override, bit6/7=bounceback override, bit8=inertiaDisabled.
        _snapToItem = (scrollBarFlags & 2) != 0;
        _pageMode = (scrollBarFlags & 8) != 0;

        if ((scrollBarFlags & 16) != 0)
            _touchEffect = true;
        else if ((scrollBarFlags & 32) != 0)
            _touchEffect = false;

        if ((scrollBarFlags & 64) != 0)
            _bouncebackEffect = true;
        else if ((scrollBarFlags & 128) != 0)
            _bouncebackEffect = false;

        _inertiaDisabled = (scrollBarFlags & 256) != 0;

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


