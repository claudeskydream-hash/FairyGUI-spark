#if CLIENT
using System.Drawing;
using FairyGUI.Utils;

namespace FairyGUI;

public class GButton : GComponent, IColorGear
{
    private const bool EnableButtonDiagLogs = false;

    public const string UP = "up";
    public const string DOWN = "down";
    public const string OVER = "over";
    public const string SELECTED_OVER = "selectedOver";
    public const string DISABLED = "disabled";
    public const string SELECTED_DISABLED = "selectedDisabled";

    public bool ChangeStateOnClick { get; set; } = true;

    protected GObject? _titleObject;
    protected GObject? _iconObject;
    protected Controller? _relatedController;
    protected string? _relatedPageId;
    protected Controller? _buttonController;

    private ButtonMode _mode;
    private bool _selected;
    private string _title = "";
    private string? _icon;
    private string? _selectedTitle;
    private string? _selectedIcon;
    private int _downEffect;
    private float _downEffectValue = 0.8f;
    private bool _downScaled;
    private bool _down;
    private bool _over;
    private bool _suppressDownEffectScale;
    private bool _suppressInteractiveStateTransition;
    private long _lastClickTick;

    private EventListener? _onChanged;

    public EventListener OnChanged => _onChanged ??= new EventListener();
    public bool SuppressDownEffectScale
    {
        get => _suppressDownEffectScale;
        set
        {
            if (_suppressDownEffectScale == value)
            {
                return;
            }

            _suppressDownEffectScale = value;
            if (_suppressDownEffectScale)
            {
                ResetDownScaleIfNeeded();
            }
        }
    }

    public bool SuppressInteractiveStateTransition
    {
        get => _suppressInteractiveStateTransition;
        set
        {
            if (_suppressInteractiveStateTransition == value)
            {
                return;
            }

            _suppressInteractiveStateTransition = value;
            if (_suppressInteractiveStateTransition)
            {
                _over = false;
                _down = false;
                SetState(UP);
            }
        }
    }

    public override string? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            var val = (_selected && _selectedIcon != null) ? _selectedIcon : _icon;
            if (_iconObject != null) _iconObject.Icon = ResolveIconUrl(val);
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            if (_titleObject != null)
                _titleObject.Text = (_selected && _selectedTitle != null) ? _selectedTitle : _title;
        }
    }

    public override string? Text
    {
        get => Title;
        set => Title = value ?? "";
    }

    public string? SelectedIcon
    {
        get => _selectedIcon;
        set
        {
            _selectedIcon = value;
            var val = (_selected && _selectedIcon != null) ? _selectedIcon : _icon;
            if (_iconObject != null) _iconObject.Icon = ResolveIconUrl(val);
        }
    }

    public string? SelectedTitle
    {
        get => _selectedTitle;
        set
        {
            _selectedTitle = value;
            if (_titleObject != null)
                _titleObject.Text = (_selected && _selectedTitle != null) ? _selectedTitle : _title;
        }
    }

    public Color TitleColor
    {
        get => GetTextField()?.Color ?? Color.Black;
        set { var tf = GetTextField(); if (tf != null) tf.Color = value; }
    }

    public Color Color
    {
        get => TitleColor;
        set => TitleColor = value;
    }

    public int TitleFontSize
    {
        get => GetTextField()?.FontSize ?? 0;
        set { var tf = GetTextField(); if (tf != null) tf.FontSize = value; }
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_mode == ButtonMode.Common) return;
            if (_selected != value)
            {
                _selected = value;
                SetCurrentState();
                if (_selectedTitle != null && _titleObject != null)
                    _titleObject.Text = _selected ? _selectedTitle : _title;
                if (_selectedIcon != null && _iconObject != null)
                    _iconObject.Icon = ResolveIconUrl(_selected ? _selectedIcon : _icon);
                if (_relatedController != null && Parent != null && Parent.BuildingDisplayList == 0)
                {
                    if (_selected)
                        _relatedController.SetSelectedPageByToken(_relatedPageId);
                    else if (_mode == ButtonMode.Check
                        && !string.IsNullOrWhiteSpace(_relatedPageId)
                        && _relatedController.IsCurrentPageToken(_relatedPageId))
                        _relatedController.OppositePageId = _relatedPageId;
                }
            }
        }
    }

    public ButtonMode Mode
    {
        get => _mode;
        set { if (_mode != value) { if (value == ButtonMode.Common) Selected = false; _mode = value; } }
    }

    public Controller? RelatedController
    {
        get => _relatedController;
        set { if (value != _relatedController) { _relatedController = value; _relatedPageId = null; } }
    }

    public string? RelatedPageId { get => _relatedPageId; set => _relatedPageId = value; }

    public GTextField? GetTextField()
    {
        if (_titleObject is GTextField tf) return tf;
        if (_titleObject is GLabel label) return label.GetTextField();
        if (_titleObject is GButton btn) return btn.GetTextField();
        return null;
    }

    protected void SetState(string val)
    {
        if (_suppressInteractiveStateTransition && !IsCloseButton())
        {
            if (val == OVER || val == DOWN || val == SELECTED_OVER)
            {
                val = UP;
            }
            else if (val == SELECTED_DISABLED)
            {
                val = DISABLED;
            }
        }

        if (_buttonController != null)
        {
            if (!TrySetControllerState(val) && _buttonController.PageCount > 0 && val == UP)
            {
                _buttonController.SelectedIndex = 0;
                if (EnableButtonDiagLogs)
                {
//                     Game.Logger.LogInformation(
//                         "[FGUI] Button fallback to page index 0: button={ButtonName}, controller={Controller}",
//                         Name, _buttonController.Name);
                }
            }
        }

        // CloseButton 在 SCE 里优先保证“按下缩小”触感，不依赖导出 downEffect 配置。
        if (IsCloseButton())
        {
            if (_suppressDownEffectScale)
            {
                ResetDownScaleIfNeeded();
            }
            else
            {
                ApplyDownScaleByState(val, ResolveDownScaleFactor());
            }
            return;
        }

        if (_downEffect == 1)
        {
            float v = _downEffectValue;
            if (val == DOWN || val == SELECTED_OVER || val == SELECTED_DISABLED)
            {
                foreach (var child in _children)
                    if (child is IColorGear cg && child is not GTextField)
                        cg.Color = Color.FromArgb(255, (int)(255 * v), (int)(255 * v), (int)(255 * v));
            }
            else
            {
                foreach (var child in _children)
                    if (child is IColorGear cg && child is not GTextField)
                        cg.Color = Color.White;
            }
        }
        else if (_downEffect == 2)
        {
            if (_suppressDownEffectScale)
            {
                ResetDownScaleIfNeeded();
            }
            else
            {
                ApplyDownScaleByState(val, ResolveDownScaleFactor());
            }
        }
    }

    protected void SetCurrentState()
    {
        if (Grayed && _buttonController != null && _buttonController.HasPage(DISABLED))
            SetState(_selected ? SELECTED_DISABLED : DISABLED);
        else
            SetState(_selected ? (_over ? SELECTED_OVER : DOWN) : (_over ? OVER : UP));
    }

    public override void HandleControllerChanged(Controller c)
    {
        base.HandleControllerChanged(c);
        if (_relatedController == c)
            Selected = !string.IsNullOrWhiteSpace(_relatedPageId) && c.IsCurrentPageToken(_relatedPageId);
    }

    protected override void HandleGrayedChanged()
    {
        if (_buttonController != null && _buttonController.HasPage(DISABLED))
            SetState(Grayed ? (_selected ? SELECTED_DISABLED : DISABLED) : (_selected ? DOWN : UP));
        else
            base.HandleGrayedChanged();
    }

    protected override void ConstructExtension(ByteBuffer buffer)
    {
        buffer.Seek(0, 6);

        _mode = (ButtonMode)buffer.ReadByte();
        buffer.ReadS(); // sound URL
        buffer.ReadFloat(); // soundVolumeScale
        _downEffect = buffer.ReadByte();
        _downEffectValue = buffer.ReadFloat();
        if (_downEffect == 2)
            SetPivot(0.5f, 0.5f, PivotAsAnchor);

        _buttonController = GetController("button") ?? FindButtonStateControllerFallback();
        _titleObject = GetChild("title");
        _iconObject = GetChild("icon");
        if (_titleObject != null)
        {
            _titleObject.Touchable = false;
        }
        if (_iconObject != null)
        {
            _iconObject.Touchable = false;
        }

        // Apply title/icon that was set in Setup_AfterAdd to the child objects
        if (_titleObject != null && !string.IsNullOrEmpty(_title))
            _titleObject.Text = _title;
        if (_iconObject != null && !string.IsNullOrEmpty(_icon))
            _iconObject.Icon = ResolveIconUrl(_icon);

        if (_mode == ButtonMode.Common)
            SetCurrentState();

        // Re-apply grayed after button controller is resolved.
        // Setup_BeforeAdd may set Grayed before ConstructExtension assigns _buttonController,
        // which can otherwise override disabled visual state during initial SetState.
        if (Grayed)
            HandleGrayedChanged();

        // 注册事件处理器
        RegisterEventHandlers();

        if (EnableButtonDiagLogs)
        {
//             Game.Logger.LogInformation(
//                 "[FGUI] Button ConstructExtension: name='{Name}', title='{Title}', titleObjText='{TitleObj}', mode={Mode}, buttonController={ControllerName}, pageCount={PageCount}, childCount={ChildCount}",
//                 Name, _title, _titleObject?.Text, _mode, _buttonController?.Name ?? "<none>", _buttonController?.PageCount ?? 0, NumChildren);
        }
    }

    private bool TrySetControllerState(string val)
    {
        if (_buttonController == null)
        {
            return false;
        }

        if (_buttonController.HasPage(val))
        {
            _buttonController.SelectedPage = val;
            return true;
        }

        var aliases = val switch
        {
            UP => new[] { "up", "normal", "default" },
            OVER => new[] { "over", "hover" },
            DOWN => new[] { "down", "pressed", "press", "selected" },
            DISABLED => new[] { "disabled", "gray", "grayed" },
            SELECTED_OVER => new[] { "selectedover", "selected_over", "selected-over", "over" },
            SELECTED_DISABLED => new[] { "selecteddisabled", "selected_disabled", "selected-disabled", "disabled" },
            _ => new[] { val }
        };

        foreach (var alias in aliases)
        {
            var pageIndex = _buttonController.GetPageIndexByName(alias);
            if (pageIndex < 0)
            {
                continue;
            }

            _buttonController.SelectedIndex = pageIndex;
            if (EnableButtonDiagLogs)
            {
//                 Game.Logger.LogInformation(
//                     "[FGUI] Button state alias mapped: button={ButtonName}, requested={Requested}, alias={Alias}, controller={Controller}, index={Index}",
//                     Name, val, alias, _buttonController.Name, pageIndex);
            }
            return true;
        }

        return false;
    }

    private Controller? FindButtonStateControllerFallback()
    {
        Controller? first = null;
        for (var i = 0; i < NumControllers; i++)
        {
            var controller = GetControllerAt(i);
            if (controller == null)
            {
                continue;
            }

            first ??= controller;
            if (!controller.HasPage(UP))
            {
                continue;
            }

            if (controller.HasPage(DOWN) || controller.HasPage(OVER) || controller.HasPage(DISABLED))
            {
                if (EnableButtonDiagLogs)
                {
//                     Game.Logger.LogInformation(
//                         "[FGUI] Button controller fallback selected: button={ButtonName}, controller={Controller}, pageCount={PageCount}",
//                         Name, controller.Name, controller.PageCount);
                }
                return controller;
            }
        }

        return first;
    }

    private bool IsCloseButton()
    {
        if (!string.IsNullOrWhiteSpace(Name) &&
            Name.Contains("close", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var itemName = PackageItem?.Name;
        return !string.IsNullOrWhiteSpace(itemName) &&
               itemName.Contains("close", StringComparison.OrdinalIgnoreCase);
    }

    private float ResolveDownScaleFactor()
    {
        var factor = _downEffectValue;
        if (!float.IsFinite(factor) || factor <= 0f)
        {
            factor = 0.8f;
        }

        if (IsCloseButton())
        {
            if (factor >= 1f)
            {
                factor = 1f / MathF.Max(1.0001f, factor);
            }

            // 统一 close button 的触感：按下为缩小，不允许放大。
            return Math.Clamp(factor, 0.5f, 0.95f);
        }

        return Math.Clamp(factor, 0.05f, 4f);
    }

    private void ApplyDownScaleByState(string val, float scaleFactor)
    {
        if (val == DOWN || val == SELECTED_OVER || val == SELECTED_DISABLED)
        {
            if (!_downScaled)
            {
                _downScaled = true;
                SetScale(ScaleX * scaleFactor, ScaleY * scaleFactor);
            }
        }
        else
        {
            if (_downScaled)
            {
                _downScaled = false;
                SetScale(ScaleX / scaleFactor, ScaleY / scaleFactor);
            }
        }
    }

    private void ResetDownScaleIfNeeded()
    {
        if (!_downScaled)
        {
            return;
        }

        _downScaled = false;
        var factor = ResolveDownScaleFactor();
        SetScale(ScaleX / factor, ScaleY / factor);
    }

    /// <summary>
    /// 注册按钮事件处理器
    /// </summary>
    private void RegisterEventHandlers()
    {
        // RollOver/RollOut 为 FairyGUI 语义
        AddEventListener("onRollOver", HandleRollOver);
        AddEventListener("onRollOut", HandleRollOut);

        // 触摸/点击事件
        AddEventListener("onTouchBegin", HandleTouchBegin);
        AddEventListener("onTouchEnd", HandleTouchEnd);
        AddEventListener("onClick", HandleClick);

        // 从舞台移除事件
        AddEventListener("onRemovedFromStage", HandleRemovedFromStage);
    }

    /// <summary>
    /// 鼠标进入事件处理
    /// </summary>
    private void HandleRollOver(EventContext context)
    {
        if (_buttonController == null || !_buttonController.HasPage(OVER))
            return;

        _over = true;
        if (_down)
            return;

        if (Grayed && _buttonController.HasPage(DISABLED))
            return;

        SetState(_selected ? SELECTED_OVER : OVER);
    }

    /// <summary>
    /// 鼠标离开事件处理
    /// </summary>
    private void HandleRollOut(EventContext context)
    {
        if (_buttonController == null || !_buttonController.HasPage(OVER))
            return;

        _over = false;
        if (_down)
            return;

        if (Grayed && _buttonController.HasPage(DISABLED))
            return;

        SetState(_selected ? DOWN : UP);
    }

    /// <summary>
    /// 触摸开始事件处理
    /// </summary>
    private void HandleTouchBegin(EventContext context)
    {
        if (_down)
            return;

        _down = true;
        StartDrag(); // Pointer capture equivalent

        if (_mode == ButtonMode.Common)
        {
            if (Grayed && _buttonController != null && _buttonController.HasPage(DISABLED))
                SetState(SELECTED_DISABLED);
            else
                SetState(DOWN);
        }

        // TODO: linkedPopup支持
        // if (linkedPopup != null) { ... }
    }

    /// <summary>
    /// 触摸结束事件处理
    /// </summary>
    private void HandleTouchEnd(EventContext context)
    {
        if (_down)
        {
            _down = false;
            if (_mode == ButtonMode.Common)
            {
                if (Grayed && _buttonController != null && _buttonController.HasPage(DISABLED))
                    SetState(DISABLED);
                else if (_over)
                    SetState(OVER);
                else
                    SetState(UP);
            }
            else
            {
                if (!_over
                    && _buttonController != null
                    && (_buttonController.SelectedPage == OVER || _buttonController.SelectedPage == SELECTED_OVER))
                {
                    SetCurrentState();
                }
            }
        }

        StopDrag();
    }

    /// <summary>
    /// 点击事件处理
    /// </summary>
    private void HandleClick(EventContext context)
    {
        // SCE pointer routing can emit duplicated click callbacks in the same physical tap.
        // Keep FairyGUI button semantics: one state toggle per real click.
        var nowTick = Environment.TickCount64;
        if (nowTick - _lastClickTick >= 0 && nowTick - _lastClickTick < 60)
        {
            return;
        }

        _lastClickTick = nowTick;

        // TODO: 音效播放
        // if (sound != null) { ... }

        if (_mode == ButtonMode.Check)
        {
            if (ChangeStateOnClick)
            {
                Selected = !_selected;
                _onChanged?.Call(context);
            }
        }
        else if (_mode == ButtonMode.Radio)
        {
            if (ChangeStateOnClick && !_selected)
            {
                Selected = true;
                _onChanged?.Call(context);
            }
        }
        else // Common mode
        {
            if (_relatedController != null && _relatedPageId != null)
                _relatedController.SetSelectedPageByToken(_relatedPageId);
        }
    }

    /// <summary>
    /// 从舞台移除事件处理
    /// </summary>
    private void HandleRemovedFromStage(EventContext context)
    {
        StopDrag();
        if (_over)
            HandleRollOut(context);
    }

    /// <summary>
    /// 模拟按钮点击（程序化触发）
    /// </summary>
    /// <param name="downEffect">是否显示按下效果</param>
    /// <param name="clickCall">是否触发点击回调</param>
    public void FireClick(bool downEffect = true, bool clickCall = true)
    {
        if (downEffect && _mode == ButtonMode.Common)
        {
            SetState(DOWN);
            // 延迟恢复状态
            GTween.DelayedCall(0.1f, () =>
            {
                SetState(_over ? OVER : UP);
            });
        }

        if (clickCall)
        {
            HandleClick(new EventContext());
        }
    }

    private string? ResolveIconUrl(string? rawIcon)
    {
        if (string.IsNullOrWhiteSpace(rawIcon))
            return rawIcon;

        if (rawIcon.StartsWith(UIPackage.URL_PREFIX, StringComparison.OrdinalIgnoreCase))
            return rawIcon;

        var owner = PackageItem?.Owner;
        if (owner?.Name == null)
            return rawIcon;

        var iconKey = rawIcon.Trim();
        var item = owner.GetItemByName(iconKey) ?? owner.GetItem(iconKey);
        if (item == null)
        {
            item = owner.GetItems().FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x.Name) &&
                (x.Name.Equals(iconKey, StringComparison.OrdinalIgnoreCase) ||
                 x.Name.EndsWith("/" + iconKey, StringComparison.OrdinalIgnoreCase) ||
                 x.Name.EndsWith(iconKey, StringComparison.OrdinalIgnoreCase)));
        }

        if (item == null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(owner.Id))
        {
            Game.Logger.LogWarning("[FGUI][Icon] button icon unresolved raw={Raw} owner={Owner}", iconKey, owner.Name);
            return rawIcon;
        }

        return UIPackage.URL_PREFIX + owner.Id + item.Id;
    }

    public override void Setup_AfterAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_AfterAdd(buffer, beginPos);

        if (!buffer.Seek(beginPos, 6))
            return;

        if ((ObjectType)buffer.ReadByte() != PackageItem?.ObjectType)
            return;

        string? str;

        str = buffer.ReadS();
        if (str != null) Title = str;

        str = buffer.ReadS();
        if (str != null) SelectedTitle = str;

        str = buffer.ReadS();
        if (str != null) Icon = str;

        str = buffer.ReadS();
        if (str != null) SelectedIcon = str;

        if (buffer.ReadBool())
            TitleColor = buffer.ReadColor();

        int iv = buffer.ReadInt();
        if (iv != 0) TitleFontSize = iv;

        iv = buffer.ReadShort();
        if (iv >= 0 && Parent != null)
            _relatedController = Parent.GetControllerAt(iv);

        _relatedPageId = buffer.ReadS();

        buffer.ReadS(); // sound override
        if (buffer.ReadBool())
            buffer.ReadFloat(); // soundVolumeScale override

        Selected = buffer.ReadBool();

        if (EnableButtonDiagLogs)
        {
//             Game.Logger.LogInformation($"[FGUI] Button Setup_AfterAdd: title='{_title}', selected={_selected}");
        }
    }
}

public interface IColorGear
{
    Color Color { get; set; }
}
#endif

