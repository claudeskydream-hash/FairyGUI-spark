#if CLIENT
using System.Drawing;
using System.Linq;
using System.Text;
using FairyGUI;
using FairyGUI.Utils;

namespace FairyGUI;

public class GTextField : GObject
{
    // 静态标志：记录哪些警告已经显示过（避免重复警告）
    private static bool _warnedLeading = false;
    
    protected string _text = "";
    protected bool _ubbEnabled;
    protected AutoSizeType _autoSize = AutoSizeType.Both;
    protected bool _singleLine;
    protected string _font = "";
    protected int _fontSize = 12;
    protected Color _color = Color.Black;
    protected AlignType _align = AlignType.Left;
    protected VertAlignType _verticalAlign = VertAlignType.Top;
    protected int _leading;
    protected int _letterSpacing;
    protected bool _bold;
    protected bool _italic;
    protected bool _underline;
    protected Color? _strokeColor;
    protected int _strokeSize;
    protected Color? _shadowColor;
    protected PointF _shadowOffset;
    protected Dictionary<string, string>? _templateVars;

    public override string? Text { get => _text; set { _text = value ?? ""; UpdateDisplay(); } }
    public Dictionary<string, string>? TemplateVars
    {
        get => _templateVars;
        set
        {
            if (_templateVars == null && value == null)
                return;

            _templateVars = value;
            FlushVars();
        }
    }
    public bool UBBEnabled { get => _ubbEnabled; set => _ubbEnabled = value; }
    public AutoSizeType AutoSize { get => _autoSize; set { _autoSize = value; UpdateDisplay(); } }
    public bool SingleLine { get => _singleLine; set { _singleLine = value; UpdateDisplay(); } }
    public string Font { get => _font; set { _font = value; UpdateDisplay(); } }
    public int FontSize { get => _fontSize; set { _fontSize = value; UpdateDisplay(); } }
    public Color Color { get => _color; set { _color = value; UpdateDisplay(); } }
    public AlignType Align { get => _align; set { _align = value; UpdateDisplay(); } }
    public VertAlignType VerticalAlign { get => _verticalAlign; set { _verticalAlign = value; UpdateDisplay(); } }
    public bool Bold { get => _bold; set { _bold = value; UpdateDisplay(); } }
    public bool Italic { get => _italic; set { _italic = value; UpdateDisplay(); } }

    public GTextField SetVar(string name, string value)
    {
        if (_templateVars == null)
            _templateVars = new Dictionary<string, string>();
        _templateVars[name] = value;
        return this;
    }

    public void FlushVars()
    {
        UpdateDisplay();
    }

    private string ParseTemplate(string template)
    {
        if (_templateVars == null || _templateVars.Count == 0)
            return template;

        int pos1 = 0, pos2 = 0;
        int pos3;
        string tag;
        string value;
        StringBuilder buffer = new StringBuilder();

        while ((pos2 = template.IndexOf('{', pos1)) != -1)
        {
            if (pos2 > 0 && template[pos2 - 1] == '\\')
            {
                buffer.Append(template, pos1, pos2 - pos1 - 1);
                buffer.Append('{');
                pos1 = pos2 + 1;
                continue;
            }

            buffer.Append(template, pos1, pos2 - pos1);
            pos1 = pos2;
            pos2 = template.IndexOf('}', pos1);
            if (pos2 == -1)
                break;

            if (pos2 == pos1 + 1)
            {
                buffer.Append(template, pos1, 2);
                pos1 = pos2 + 1;
                continue;
            }

            tag = template.Substring(pos1 + 1, pos2 - pos1 - 1);
            pos3 = tag.IndexOf('=');
            if (pos3 != -1)
            {
                if (!_templateVars.TryGetValue(tag.Substring(0, pos3), out value))
                    value = tag.Substring(pos3 + 1);
            }
            else
            {
                if (!_templateVars.TryGetValue(tag, out value))
                    value = "";
            }
            buffer.Append(value);
            pos1 = pos2 + 1;
        }
        if (pos1 < template.Length)
            buffer.Append(template, pos1, template.Length - pos1);

        return buffer.ToString();
    }

    public override void ConstructFromResource()
    {
        // Size may already be set by Setup_BeforeAdd
        // For AutoSize text, Width/Height might be 0 - that's OK, SCE Label will auto-size
        if (PackageItem != null && _width == 0 && _height == 0)
        {
            SourceWidth = PackageItem.Width;
            SourceHeight = PackageItem.Height;
            InitWidth = SourceWidth;
            InitHeight = SourceHeight;
            SetSize(SourceWidth, SourceHeight);
        }
        CreateNativeControl();
    }

    protected virtual void CreateNativeControl()
    {
        // 创建原生Label控件
        if (NativeObject == null)
        {
            Render.SCERenderContext.Instance.CreateNativeControl(this);
            // 初始化显示属性
            UpdateDisplay();
        }
    }

    public override void Setup_BeforeAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_BeforeAdd(buffer, beginPos);
        buffer.Seek(beginPos, 5);
        var fontStr = buffer.ReadS();
        if (fontStr != null) _font = fontStr;
        _fontSize = buffer.ReadShort();
        _color = buffer.ReadColor();
        _align = (AlignType)buffer.ReadByte();
        _verticalAlign = (VertAlignType)buffer.ReadByte();
        _leading = buffer.ReadShort();
        _letterSpacing = buffer.ReadShort();
        _ubbEnabled = buffer.ReadBool();
        _autoSize = (AutoSizeType)buffer.ReadByte();
        _underline = buffer.ReadBool();
        _italic = buffer.ReadBool();
        _bold = buffer.ReadBool();
        _singleLine = buffer.ReadBool();
        if (buffer.ReadBool()) { _strokeColor = buffer.ReadColor(); _strokeSize = (int)buffer.ReadFloat(); }
        if (buffer.ReadBool()) { _shadowColor = buffer.ReadColor(); _shadowOffset = new PointF(buffer.ReadFloat(), buffer.ReadFloat()); }
        if (buffer.ReadBool()) buffer.ReadS();
    }

    public override void Setup_AfterAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_AfterAdd(buffer, beginPos);
        if (!buffer.Seek(beginPos, 6))
        {
            Game.Logger.LogWarning($"[FGUI] TextField '{Name}' Setup_AfterAdd: block 6 not found");
            return;
        }
        string? str = buffer.ReadS();
        if (str != null) 
        {
            Text = str;
            Game.Logger.LogInformation($"[FGUI] TextField '{Name}' Setup_AfterAdd: text='{str}'");
        }
    }

    protected virtual void UpdateDisplay()
    {
        // 确保原生控件已创建
        if (NativeObject == null)
        {
            CreateNativeControl();
            return;
        }

        var adapter = Render.SCERenderContext.Instance.Adapter;
        if (adapter == null) return;

        // 处理模板变量
        string displayText = _text;
        if (_templateVars != null)
        {
            displayText = ParseTemplate(displayText);
        }
        
        // 处理 UBB 标签：转成引擎内联标记([u]→<u>、[color=..]→<color=..> 等)交给引擎渲染，
        // 不再剥成纯文本。引擎不支持的标记会原样显示，届时在 UBBParser 里把对应 handler 改回剥离。
        if (_ubbEnabled && !string.IsNullOrEmpty(displayText))
        {
            displayText = UBBParser.Instance.Parse(displayText);
        }

        // 下划线（主方案）：引擎 Label 文本渲染支持内联标记 <u>，包一层即得原生下划线（贴合文字宽度）。
        // 备用方案是覆盖 Canvas 手绘（见 adapter.SetTextUnderline），当前不启用。
        if (_underline && !string.IsNullOrEmpty(displayText))
            displayText = $"<u>{displayText}</u>";

        // 设置文本内容
        adapter.SetText(NativeObject, displayText);

        // 设置文本颜色
        adapter.SetTextColor(NativeObject, _color);

        // 设置字体大小
        adapter.SetFontSize(NativeObject, _fontSize);

        // 设置字体 family（FGUI 存的是字体名/资源引用，映射成引擎 family 后套用）
        var fontFamily = FGUIFontMap.Resolve(_font);
        if (fontFamily != null)
        {
            adapter.SetFontName(NativeObject, fontFamily);
        }

        // 设置粗体和斜体
        adapter.SetBold(NativeObject, _bold);
        adapter.SetItalic(NativeObject, _italic);

        // 设置对齐方式
        var textAlign = _align switch
        {
            AlignType.Left => Render.TextAlign.Left,
            AlignType.Center => Render.TextAlign.Center,
            AlignType.Right => Render.TextAlign.Right,
            _ => Render.TextAlign.Left
        };
        adapter.SetTextAlign(NativeObject, textAlign);

        // 设置垂直对齐
        var vertAlign = _verticalAlign switch
        {
            VertAlignType.Top => Render.TextVerticalAlign.Top,
            VertAlignType.Middle => Render.TextVerticalAlign.Middle,
            VertAlignType.Bottom => Render.TextVerticalAlign.Bottom,
            _ => Render.TextVerticalAlign.Top
        };
        adapter.SetTextVerticalAlign(NativeObject, vertAlign);

        // 描边：引擎原生支持，直接套用（无描边时显式清零，避免控件复用残留）
        if (_strokeColor.HasValue)
            adapter.SetTextStroke(NativeObject, _strokeColor.Value, _strokeSize);
        else
            adapter.SetTextStroke(NativeObject, System.Drawing.Color.Transparent, 0);

        // 阴影：引擎原生支持，直接套用（无阴影时清零）
        if (_shadowColor.HasValue)
            adapter.SetTextShadow(NativeObject, _shadowColor.Value, _shadowOffset);
        else
            adapter.SetTextShadow(NativeObject, System.Drawing.Color.Transparent, default);

        // 下划线备用方案（覆盖 Canvas 手绘）：当前用 <u> 内联标记，故此处传 false 关闭覆盖层。
        // 若哪天 <u> 的层级/位置不满足需求，把上面 <u> 包裹去掉、这里改回传 _underline 即可切换。
        adapter.SetTextUnderline(NativeObject, false, Width, Height);

        // 记录暂不支持的特性（每种警告只记录一次，避免日志冗余）
        if ((_leading != 0 || _letterSpacing != 0) && !_warnedLeading)
        {
            var preview = displayText.Length > 32 ? $"{displayText[..32]}..." : displayText;
            Game.Logger.LogWarning(
                "[FGUI] TextField leading/letterSpacing not supported in SCE (this warning will only show once). name={Name}, leading={Leading}, letterSpacing={LetterSpacing}, text='{Preview}'",
                Name,
                _leading,
                _letterSpacing,
                preview);
            _warnedLeading = true;
        }

        // TODO: 实现AutoSize逻辑
        // 目前依赖SCE Label的自动尺寸功能
        if (_autoSize != AutoSizeType.None)
        {
            Game.Logger.LogInformation($"[FGUI] TextField '{Name}' AutoSize={_autoSize} - relying on SCE Label auto-sizing");
        }
    }
}

public class GRichTextField : GTextField 
{
    public EventListener OnClickLink => GetOrCreateListener("onClickLink");
    
    private EventListener GetOrCreateListener(string type)
    {
        if (!_listeners.TryGetValue(type, out var listener))
        {
            listener = new EventListener();
            _listeners[type] = listener;
        }
        return listener;
    }
    
    private readonly Dictionary<string, EventListener> _listeners = new();
}

public class GTextInput : GTextField
{
    private string _promptText = "";
    private bool _password;
    private int _maxLength;
    private string _restrict = "";
    private bool _editable = true;
    
    private EventListener? _onChanged;
    private EventListener? _onSubmit;

    public string PromptText { get => _promptText; set { _promptText = value; UpdateInputDisplay(); } }
    public bool Password { get => _password; set { _password = value; UpdateInputDisplay(); } }
    public int MaxLength { get => _maxLength; set { _maxLength = value; UpdateInputDisplay(); } }
    public string Restrict { get => _restrict; set => _restrict = value; }
    public bool Editable { get => _editable; set { _editable = value; UpdateInputDisplay(); } }
    public override string? Text
    {
        get
        {
            SyncTextFromNativeInput();
            return base.Text;
        }
        set => base.Text = value;
    }
    
    /// <summary>
    /// 文本变化事件
    /// </summary>
    public EventListener OnChanged => _onChanged ??= new EventListener();
    
    /// <summary>
    /// 提交事件（按下回车）
    /// </summary>
    public EventListener OnSubmit => _onSubmit ??= new EventListener();

    public override void Setup_BeforeAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_BeforeAdd(buffer, beginPos);
        buffer.Seek(beginPos, 4);
        string? str = buffer.ReadS();
        if (str != null) _promptText = str;
        str = buffer.ReadS();
        if (str != null) _restrict = str;
        _maxLength = buffer.ReadInt();
        buffer.ReadInt();
        if (buffer.ReadBool()) _password = true;
    }
    
    protected override void CreateNativeControl()
    {
        // 输入框使用 Input 控件。必须走渲染上下文的统一创建路径,而不是自己 CreateInput():
        // 只有 SCERenderContext.CreateNativeControl 才会随后调用 ApplyProperties
        // (定位/尺寸/可见/BindTouchEvents/指针拦截)。若绕过它,输入框虽能显示,
        // 但指针事件默认穿透到父级,点击无法聚焦→无法输入。
        if (NativeObject == null)
        {
            Render.SCERenderContext.Instance.CreateNativeControl(this);
            var adapter = Render.SCERenderContext.Instance.Adapter;
            if (NativeObject != null && adapter != null)
            {
                adapter.OnInputTextChanged(NativeObject, text =>
                {
                    if (_text == text)
                    {
                        return;
                    }

                    _text = text;
                    _onChanged?.Call(new EventContext
                    {
                        Sender = this,
                        Type = "onChanged",
                        Data = text
                    });
                });
            }
            UpdateDisplay();
            UpdateInputDisplay();
        }
    }
    
    /// <summary>
    /// 更新输入框特有属性
    /// </summary>
    private void UpdateInputDisplay()
    {
        if (NativeObject == null) return;
        
        var adapter = Render.SCERenderContext.Instance.Adapter;
        if (adapter == null) return;
        
        // 设置占位符文本
        if (!string.IsNullOrEmpty(_promptText))
        {
            adapter.SetInputPlaceholder(NativeObject, _promptText);
        }
        
        // 设置密码模式
        adapter.SetInputPassword(NativeObject, _password);
        
        // 设置最大长度
        if (_maxLength > 0)
        {
            adapter.SetInputMaxLength(NativeObject, _maxLength);
        }
        
        // 设置是否可编辑
        adapter.SetInputEditable(NativeObject, _editable);
    }

    private void SyncTextFromNativeInput()
    {
        if (NativeObject == null)
        {
            return;
        }

        // Host input callbacks can be one step behind in some UI paths.
        // Pull native text directly so immediate click handlers read the latest value.
        var nativeText = TryGetNativeText(NativeObject);
        if (nativeText != null && _text != nativeText)
        {
            _text = nativeText;
        }
    }

    private static string? TryGetNativeText(object nativeObject)
    {
        var textProperty = nativeObject.GetType().GetProperty("Text");
        if (textProperty?.CanRead != true)
        {
            return null;
        }

        return textProperty.GetValue(nativeObject)?.ToString();
    }
}
#endif


