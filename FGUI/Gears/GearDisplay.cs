#if CLIENT
using System.Drawing;
using FairyGUI;
using FairyGUI.Utils;

namespace FairyGUI.Gears;

public class GearDisplay : GearBase
{
    public string[]? Pages { get; set; }
    private int _visible;
    private uint _displayLockToken = 1;

    public GearDisplay(GObject owner) : base(owner) { }

    protected override void AddStatus(string? pageId, ByteBuffer buffer) { }
    protected override void Init() { Pages = null; }

    public override void Apply()
    {
        bool match = MatchesControllerPage(Pages, _controller);
        int oldVisible = _visible;
        if (match)
            _visible = 1;
        else
            _visible = 0;

        if (oldVisible != _visible)
        {
            _displayLockToken++;
            if (_displayLockToken == 0)
                _displayLockToken = 1;
        }

        if (string.Equals(_owner.Name, "btns", StringComparison.Ordinal))
            Game.Logger.LogInformation(
                "[FGUI][GearDisplay][btns] selectedIndex={SelectedIndex} selectedPage={SelectedPage} selectedPageId={SelectedPageId} pages=[{Pages}] visible={Visible}",
                _controller?.SelectedIndex ?? -1,
                _controller?.SelectedPage,
                _controller?.SelectedPageId,
                string.Join(",", Pages ?? Array.Empty<string>()),
                _visible);
        
        if (oldVisible != _visible)
            Game.Logger.LogInformation(
                "[FGUI] GearDisplay.Apply: {Owner}, controller={Controller}, selectedPage={SelectedPage}, selectedPageId={SelectedPageId}, pages=[{Pages}], visible={Visible}",
                _owner.Name,
                _controller?.Name,
                _controller?.SelectedPage,
                _controller?.SelectedPageId,
                string.Join(",", Pages ?? Array.Empty<string>()),
                _visible);
    }

    public override void UpdateState() { }

    public uint AddLock()
    {
        _visible++;
        if (string.Equals(_owner.Name, "btns", StringComparison.Ordinal))
            Game.Logger.LogInformation(
                "[FGUI][GearDisplay][btns][LOCK+] token={Token} visible={Visible}",
                _displayLockToken,
                _visible);
        return _displayLockToken;
    }
    public void ReleaseLock(uint token)
    {
        var released = false;
        if (token == _displayLockToken)
        {
            _visible--;
            released = true;
        }

        if (string.Equals(_owner.Name, "btns", StringComparison.Ordinal))
            Game.Logger.LogInformation(
                "[FGUI][GearDisplay][btns][LOCK-] token={Token} currentToken={CurrentToken} released={Released} visible={Visible}",
                token,
                _displayLockToken,
                released,
                _visible);
    }
    public bool Connected => _controller == null || _visible > 0;

    internal static bool MatchesControllerPage(string[]? pages, Controller? controller)
    {
        if (pages == null || pages.Length == 0 || controller == null)
        {
            return true;
        }

        var selectedPageId = controller.SelectedPageId;
        var selectedPage = controller.SelectedPage;
        var selectedIndexToken = controller.SelectedIndex >= 0
            ? controller.SelectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        for (var i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            if (string.Equals(page, selectedPageId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(page, selectedPage, StringComparison.OrdinalIgnoreCase)
                || string.Equals(page, selectedIndexToken, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public class GearDisplay2 : GearBase
{
    public string[]? Pages { get; set; }
    public int Condition { get; set; }
    private int _visible;

    public GearDisplay2(GObject owner) : base(owner) { }

    protected override void AddStatus(string? pageId, ByteBuffer buffer) { }
    protected override void Init() { Pages = null; }

    public override void Apply()
    {
        if (GearDisplay.MatchesControllerPage(Pages, _controller))
            _visible = 1;
        else
            _visible = 0;
    }

    public override void UpdateState() { }

    public bool Evaluate(bool connected)
    {
        bool v = GearDisplay.MatchesControllerPage(Pages, _controller);
        return Condition == 0 ? (connected && v) : (connected || v);
    }
}

public class GearXY : GearBase
{
    public bool PositionsInPercent { get; set; }
    private readonly Dictionary<string, (float x, float y)> _storage = new();
    private readonly Dictionary<string, (float px, float py)> _extStorage = new();
    private (float x, float y) _default;
    private (float px, float py) _defaultExt;
    private bool _hasDefaultExt;
    private (float x, float y) _tweenTarget;
    private bool _hasTweenTarget;

    public GearXY(GObject owner) : base(owner) { }

    protected override void AddStatus(string? pageId, ByteBuffer buffer)
    {
        var value = (buffer.ReadInt(), buffer.ReadInt());
        if (pageId == null) _default = value;
        else _storage[pageId] = value;
    }

    public void AddExtStatus(string? pageId, ByteBuffer buffer)
    {
        var value = (buffer.ReadFloat(), buffer.ReadFloat());
        if (pageId == null)
        {
            _defaultExt = value;
            _hasDefaultExt = true;
        }
        else
        {
            _extStorage[pageId] = value;
        }
    }

    protected override void Init()
    {
        _default = (_owner.X, _owner.Y);
        _storage.Clear();
        _extStorage.Clear();
        _hasDefaultExt = false;
        _hasTweenTarget = false;

        if (_owner.Parent != null && _owner.Parent.Width != 0 && _owner.Parent.Height != 0)
        {
            _defaultExt = (_owner.X / _owner.Parent.Width, _owner.Y / _owner.Parent.Height);
            _hasDefaultExt = true;
        }
    }

    public override void Apply()
    {
        var endPos = ResolveTargetPosition();
        if (CanTween())
        {
            ApplyWithTween(endPos.x, endPos.y);
            return;
        }

        ApplyImmediate(endPos.x, endPos.y);
    }

    public override void UpdateState()
    {
        if (string.IsNullOrWhiteSpace(_controller?.SelectedPageId))
        {
            return;
        }

        var pageId = _controller.SelectedPageId;
        _storage[pageId] = (_owner.X, _owner.Y);
        if (PositionsInPercent && _owner.Parent != null && _owner.Parent.Width != 0 && _owner.Parent.Height != 0)
        {
            _extStorage[pageId] = (_owner.X / _owner.Parent.Width, _owner.Y / _owner.Parent.Height);
        }
    }

    public override void UpdateFromRelations(float dx, float dy)
    {
        if (_controller == null || PositionsInPercent)
        {
            return;
        }

        foreach (var key in _storage.Keys.ToList())
        {
            var value = _storage[key];
            _storage[key] = (value.x + dx, value.y + dy);
        }

        _default = (_default.x + dx, _default.y + dy);
        UpdateState();
    }

    private (float x, float y) ResolveTargetPosition()
    {
        var pageId = _controller?.SelectedPageId;
        (float x, float y) storedPos = default;
        var hasPage = !string.IsNullOrWhiteSpace(pageId) && _storage.TryGetValue(pageId!, out storedPos);
        var pos = hasPage ? storedPos : _default;
        if (!PositionsInPercent || _owner.Parent == null)
        {
            return pos;
        }

        if (hasPage && !string.IsNullOrWhiteSpace(pageId) && _extStorage.TryGetValue(pageId!, out var ext))
        {
            return (ext.px * _owner.Parent.Width, ext.py * _owner.Parent.Height);
        }

        if (_hasDefaultExt)
        {
            return (_defaultExt.px * _owner.Parent.Width, _defaultExt.py * _owner.Parent.Height);
        }

        return pos;
    }

    private bool CanTween()
    {
        return _tweenConfig != null
            && _tweenConfig.Tween
            && !DisableAllTweenEffect
            && !_owner.UnderConstruct;
    }

    private void ApplyWithTween(float endX, float endY)
    {
        var config = _tweenConfig!;
        if (config.Tweener != null)
        {
            if (_hasTweenTarget && NearlyEqual(_tweenTarget.x, endX) && NearlyEqual(_tweenTarget.y, endY))
            {
                return;
            }

            config.Tweener.Kill(true);
            config.Tweener = null;
            _hasTweenTarget = false;
            if (config.DisplayLockToken != 0)
            {
                _owner.ReleaseDisplayLock(config.DisplayLockToken);
                config.DisplayLockToken = 0;
            }
        }

        var originX = _owner.X;
        var originY = _owner.Y;
        if (NearlyEqual(originX, endX) && NearlyEqual(originY, endY))
        {
            ApplyImmediate(endX, endY);
            return;
        }

        if (_controller != null && _owner.CheckGearController(0, _controller))
        {
            config.DisplayLockToken = _owner.AddDisplayLock();
        }

        _tweenTarget = (endX, endY);
        _hasTweenTarget = true;
        config.Tweener = GTween.To(new PointF(originX, originY), new PointF(endX, endY), config.Duration)
            .SetDelay(config.Delay)
            .SetEase(config.EaseType)
            .SetTarget(this)
            .OnUpdate(t =>
            {
                _owner.GearLocked = true;
                _owner.SetXY(t.Value.X, t.Value.Y);
                _owner.GearLocked = false;
            })
            .OnComplete(_ => OnTweenComplete());
    }

    private void OnTweenComplete()
    {
        if (_tweenConfig == null)
        {
            return;
        }

        _tweenConfig.Tweener = null;
        _hasTweenTarget = false;
        if (_tweenConfig.DisplayLockToken != 0)
        {
            _owner.ReleaseDisplayLock(_tweenConfig.DisplayLockToken);
            _tweenConfig.DisplayLockToken = 0;
        }

        _owner.DispatchEvent("onGearStop", this);
    }

    private void ApplyImmediate(float x, float y)
    {
        _owner.GearLocked = true;
        _owner.SetXY(x, y);
        _owner.GearLocked = false;
    }

    private static bool NearlyEqual(float a, float b) => MathF.Abs(a - b) <= 0.001f;
}

public class GearSize : GearBase
{
    private readonly Dictionary<string, (float w, float h, float sx, float sy)> _storage = new();
    private (float w, float h, float sx, float sy) _default;
    private (float w, float h, float sx, float sy) _tweenTarget;
    private bool _hasTweenTarget;

    public GearSize(GObject owner) : base(owner) { }

    protected override void AddStatus(string? pageId, ByteBuffer buffer)
    {
        var value = (buffer.ReadInt(), buffer.ReadInt(), buffer.ReadFloat(), buffer.ReadFloat());
        if (pageId == null) _default = value;
        else _storage[pageId] = value;
    }

    protected override void Init()
    {
        _default = (_owner.Width, _owner.Height, _owner.ScaleX, _owner.ScaleY);
        _storage.Clear();
        _hasTweenTarget = false;
    }

    public override void Apply()
    {
        var size = ResolveTargetValue();
        if (CanTween())
        {
            ApplyWithTween(size);
            return;
        }

        ApplyImmediate(size);
    }

    public override void UpdateState()
    {
        if (!string.IsNullOrWhiteSpace(_controller?.SelectedPageId))
        {
            _storage[_controller.SelectedPageId] = (_owner.Width, _owner.Height, _owner.ScaleX, _owner.ScaleY);
        }
    }

    public override void UpdateFromRelations(float dx, float dy)
    {
        if (_controller == null)
        {
            return;
        }

        foreach (var key in _storage.Keys.ToList())
        {
            var value = _storage[key];
            _storage[key] = (value.w + dx, value.h + dy, value.sx, value.sy);
        }

        _default = (_default.w + dx, _default.h + dy, _default.sx, _default.sy);
        UpdateState();
    }

    private (float w, float h, float sx, float sy) ResolveTargetValue()
    {
        if (!string.IsNullOrWhiteSpace(_controller?.SelectedPageId)
            && _storage.TryGetValue(_controller.SelectedPageId, out var size))
        {
            return size;
        }

        return _default;
    }

    private bool CanTween()
    {
        return _tweenConfig != null
            && _tweenConfig.Tween
            && !DisableAllTweenEffect
            && !_owner.UnderConstruct;
    }

    private void ApplyWithTween((float w, float h, float sx, float sy) target)
    {
        var config = _tweenConfig!;
        if (config.Tweener != null)
        {
            if (_hasTweenTarget
                && NearlyEqual(_tweenTarget.w, target.w)
                && NearlyEqual(_tweenTarget.h, target.h)
                && NearlyEqual(_tweenTarget.sx, target.sx)
                && NearlyEqual(_tweenTarget.sy, target.sy))
            {
                return;
            }

            config.Tweener.Kill(true);
            config.Tweener = null;
            _hasTweenTarget = false;
            if (config.DisplayLockToken != 0)
            {
                _owner.ReleaseDisplayLock(config.DisplayLockToken);
                config.DisplayLockToken = 0;
            }
        }

        var start = (_owner.Width, _owner.Height, _owner.ScaleX, _owner.ScaleY);
        if (NearlyEqual(start.Item1, target.w)
            && NearlyEqual(start.Item2, target.h)
            && NearlyEqual(start.Item3, target.sx)
            && NearlyEqual(start.Item4, target.sy))
        {
            ApplyImmediate(target);
            return;
        }

        if (_controller != null && _owner.CheckGearController(0, _controller))
        {
            config.DisplayLockToken = _owner.AddDisplayLock();
        }

        _tweenTarget = target;
        _hasTweenTarget = true;
        config.Tweener = GTween.To(0f, 1f, config.Duration)
            .SetDelay(config.Delay)
            .SetEase(config.EaseType)
            .SetTarget(this)
            .OnUpdate(t =>
            {
                var p = t.Value.X;
                var w = Lerp(start.Item1, target.w, p);
                var h = Lerp(start.Item2, target.h, p);
                var sx = Lerp(start.Item3, target.sx, p);
                var sy = Lerp(start.Item4, target.sy, p);
                _owner.GearLocked = true;
                _owner.SetSize(w, h, ShouldIgnorePivot());
                _owner.SetScale(sx, sy);
                _owner.GearLocked = false;
            })
            .OnComplete(_ => OnTweenComplete());
    }

    private void OnTweenComplete()
    {
        if (_tweenConfig == null)
        {
            return;
        }

        _tweenConfig.Tweener = null;
        _hasTweenTarget = false;
        if (_tweenConfig.DisplayLockToken != 0)
        {
            _owner.ReleaseDisplayLock(_tweenConfig.DisplayLockToken);
            _tweenConfig.DisplayLockToken = 0;
        }

        _owner.DispatchEvent("onGearStop", this);
    }

    private void ApplyImmediate((float w, float h, float sx, float sy) size)
    {
        _owner.GearLocked = true;
        _owner.SetSize(size.w, size.h, ShouldIgnorePivot());
        _owner.SetScale(size.sx, size.sy);
        _owner.GearLocked = false;
    }

    private bool ShouldIgnorePivot()
    {
        return _controller != null && _owner.CheckGearController(1, _controller);
    }

    private static float Lerp(float from, float to, float t) => from + (to - from) * t;
    private static bool NearlyEqual(float a, float b) => MathF.Abs(a - b) <= 0.001f;
}

public class GearLook : GearBase
{
    private readonly Dictionary<string, (float alpha, float rotation, bool grayed, bool touchable)> _storage = new();
    private (float alpha, float rotation, bool grayed, bool touchable) _default;
    private (float alpha, float rotation) _tweenTarget;
    private bool _hasTweenTarget;

    public GearLook(GObject owner) : base(owner) { }

    protected override void AddStatus(string? pageId, ByteBuffer buffer)
    {
        var value = (buffer.ReadFloat(), buffer.ReadFloat(), buffer.ReadBool(), buffer.ReadBool());
        if (pageId == null) _default = value;
        else _storage[pageId] = value;
    }

    protected override void Init()
    {
        _default = (_owner.Alpha, _owner.Rotation, _owner.Grayed, _owner.Touchable);
        _storage.Clear();
        _hasTweenTarget = false;
    }

    public override void Apply()
    {
        var look = ResolveTargetValue();
        if (CanTween())
        {
            ApplyWithTween(look);
            return;
        }

        ApplyImmediate(look);
    }

    public override void UpdateState()
    {
        if (!string.IsNullOrWhiteSpace(_controller?.SelectedPageId))
        {
            _storage[_controller.SelectedPageId] = (_owner.Alpha, _owner.Rotation, _owner.Grayed, _owner.Touchable);
        }
    }

    private (float alpha, float rotation, bool grayed, bool touchable) ResolveTargetValue()
    {
        if (!string.IsNullOrWhiteSpace(_controller?.SelectedPageId)
            && _storage.TryGetValue(_controller.SelectedPageId, out var look))
        {
            return look;
        }

        return _default;
    }

    private bool CanTween()
    {
        return _tweenConfig != null
            && _tweenConfig.Tween
            && !DisableAllTweenEffect
            && !_owner.UnderConstruct;
    }

    private void ApplyWithTween((float alpha, float rotation, bool grayed, bool touchable) look)
    {
        _owner.GearLocked = true;
        _owner.Grayed = look.grayed;
        _owner.Touchable = look.touchable;
        _owner.GearLocked = false;

        var config = _tweenConfig!;
        if (config.Tweener != null)
        {
            if (_hasTweenTarget
                && NearlyEqual(_tweenTarget.alpha, look.alpha)
                && NearlyEqual(_tweenTarget.rotation, look.rotation))
            {
                return;
            }

            config.Tweener.Kill(true);
            config.Tweener = null;
            _hasTweenTarget = false;
            if (config.DisplayLockToken != 0)
            {
                _owner.ReleaseDisplayLock(config.DisplayLockToken);
                config.DisplayLockToken = 0;
            }
        }

        var startAlpha = _owner.Alpha;
        var startRotation = _owner.Rotation;
        if (NearlyEqual(startAlpha, look.alpha) && NearlyEqual(startRotation, look.rotation))
        {
            ApplyImmediate(look);
            return;
        }

        if (_controller != null && _owner.CheckGearController(0, _controller))
        {
            config.DisplayLockToken = _owner.AddDisplayLock();
        }

        _tweenTarget = (look.alpha, look.rotation);
        _hasTweenTarget = true;
        config.Tweener = GTween.To(new PointF(startAlpha, startRotation), new PointF(look.alpha, look.rotation), config.Duration)
            .SetDelay(config.Delay)
            .SetEase(config.EaseType)
            .SetTarget(this)
            .OnUpdate(t =>
            {
                _owner.GearLocked = true;
                _owner.Alpha = t.Value.X;
                _owner.Rotation = t.Value.Y;
                _owner.GearLocked = false;
            })
            .OnComplete(_ => OnTweenComplete());
    }

    private void OnTweenComplete()
    {
        if (_tweenConfig == null)
        {
            return;
        }

        _tweenConfig.Tweener = null;
        _hasTweenTarget = false;
        if (_tweenConfig.DisplayLockToken != 0)
        {
            _owner.ReleaseDisplayLock(_tweenConfig.DisplayLockToken);
            _tweenConfig.DisplayLockToken = 0;
        }

        _owner.DispatchEvent("onGearStop", this);
    }

    private void ApplyImmediate((float alpha, float rotation, bool grayed, bool touchable) look)
    {
        _owner.GearLocked = true;
        _owner.Alpha = look.alpha;
        _owner.Rotation = look.rotation;
        _owner.Grayed = look.grayed;
        _owner.Touchable = look.touchable;
        _owner.GearLocked = false;
    }

    private static bool NearlyEqual(float a, float b) => MathF.Abs(a - b) <= 0.001f;
}

public class GearColor : GearBase
{
    private readonly Dictionary<string, System.Drawing.Color> _storage = new();
    private System.Drawing.Color _default = System.Drawing.Color.White;

    public GearColor(GObject owner) : base(owner) { }

    protected override void AddStatus(string? pageId, ByteBuffer buffer)
    {
        var color = buffer.ReadColor();
        if (pageId == null) _default = color;
        else _storage[pageId] = color;
    }

    protected override void Init()
    {
        if (_owner is IColorGear cg)
            _default = cg.Color;
        _storage.Clear();
    }

    public override void Apply()
    {
        if (_owner is IColorGear cg)
        {
            if (_controller?.SelectedPageId != null && _storage.TryGetValue(_controller.SelectedPageId, out var color))
                cg.Color = color;
            else
                cg.Color = _default;
        }
    }

    public override void UpdateState()
    {
        if (_controller?.SelectedPageId != null && _owner is IColorGear cg)
            _storage[_controller.SelectedPageId] = cg.Color;
    }
}

public class GearText : GearBase
{
    private readonly Dictionary<string, string> _storage = new();
    private string _default = "";

    public GearText(GObject owner) : base(owner) { }

    protected override void AddStatus(string? pageId, ByteBuffer buffer)
    {
        var text = buffer.ReadS() ?? "";
        if (pageId == null) _default = text;
        else _storage[pageId] = text;
    }

    protected override void Init()
    {
        _default = _owner.Text ?? "";
        _storage.Clear();
    }

    public override void Apply()
    {
        if (_controller?.SelectedPageId != null && _storage.TryGetValue(_controller.SelectedPageId, out var text))
            _owner.Text = text;
        else
            _owner.Text = _default;
    }

    public override void UpdateState()
    {
        if (_controller?.SelectedPageId != null)
            _storage[_controller.SelectedPageId] = _owner.Text ?? "";
    }
}

public class GearIcon : GearBase
{
    private readonly Dictionary<string, string> _storage = new();
    private string _default = "";

    public GearIcon(GObject owner) : base(owner) { }

    protected override void AddStatus(string? pageId, ByteBuffer buffer)
    {
        var icon = buffer.ReadS() ?? "";
        if (pageId == null) _default = icon;
        else _storage[pageId] = icon;
    }

    protected override void Init()
    {
        _default = _owner.Icon ?? "";
        _storage.Clear();
    }

    public override void Apply()
    {
        if (_controller?.SelectedPageId != null && _storage.TryGetValue(_controller.SelectedPageId, out var icon))
            _owner.Icon = icon;
        else
            _owner.Icon = _default;
    }

    public override void UpdateState()
    {
        if (_controller?.SelectedPageId != null)
            _storage[_controller.SelectedPageId] = _owner.Icon ?? "";
    }
}

public class GearAnimation : GearBase
{
    private readonly Dictionary<string, (bool playing, int frame)> _storage = new();
    private (bool playing, int frame) _default;

    public GearAnimation(GObject owner) : base(owner) { }

    protected override void AddStatus(string? pageId, ByteBuffer buffer)
    {
        var value = (buffer.ReadBool(), buffer.ReadInt());
        if (pageId == null) _default = value;
        else _storage[pageId] = value;
    }

    public void AddExtStatus(string? pageId, ByteBuffer buffer)
    {
        // Extension data for animation gear (version >= 6)
    }

    protected override void Init()
    {
        if (_owner is GMovieClip mc)
            _default = (mc.Playing, mc.Frame);
        _storage.Clear();
    }

    public override void Apply()
    {
        if (_owner is GMovieClip mc)
        {
            if (_controller?.SelectedPageId != null && _storage.TryGetValue(_controller.SelectedPageId, out var anim))
            {
                mc.Playing = anim.playing;
                mc.Frame = anim.frame;
            }
            else
            {
                mc.Playing = _default.playing;
                mc.Frame = _default.frame;
            }
        }
    }

    public override void UpdateState()
    {
        if (_controller?.SelectedPageId != null && _owner is GMovieClip mc)
            _storage[_controller.SelectedPageId] = (mc.Playing, mc.Frame);
    }
}

public class GearFontSize : GearBase
{
    private readonly Dictionary<string, int> _storage = new();
    private int _default;

    public GearFontSize(GObject owner) : base(owner) { }

    protected override void AddStatus(string? pageId, ByteBuffer buffer)
    {
        var size = buffer.ReadInt();
        if (pageId == null) _default = size;
        else _storage[pageId] = size;
    }

    protected override void Init()
    {
        if (_owner is GTextField tf)
            _default = tf.FontSize;
        _storage.Clear();
    }

    public override void Apply()
    {
        if (_owner is GTextField tf)
        {
            if (_controller?.SelectedPageId != null && _storage.TryGetValue(_controller.SelectedPageId, out var size))
                tf.FontSize = size;
            else
                tf.FontSize = _default;
        }
    }

    public override void UpdateState()
    {
        if (_controller?.SelectedPageId != null && _owner is GTextField tf)
            _storage[_controller.SelectedPageId] = tf.FontSize;
    }
}
#endif


