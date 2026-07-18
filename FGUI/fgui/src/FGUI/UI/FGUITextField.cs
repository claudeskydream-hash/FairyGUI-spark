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
    private static bool _warnedStroke = false;
    private static bool _warnedShadow = false;
    private static bool _warnedUnderline = false;
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
        if (buffer.ReadBool()) { _strokeColor = buffer.ReadColor(); buffer.ReadFloat(); }
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
        
        // 处理UBB标签
        if (_ubbEnabled && !string.IsNullOrEmpty(displayText))
        {
            // 解析UBB标签提取样式信息
            var elements = RichTextParser.Parse(displayText, _color, _fontSize);
            
            if (elements.Count > 0)
            {
                // 对于不支持富文本的控件，提取第一个元素的样式
                var first = elements[0];
                if (!first.Bold && elements.Any(e => e.Bold)) _bold = true;
                if (!first.Italic && elements.Any(e => e.Italic)) _italic = true;
                
                // 转换为纯文本显示
                displayText = UBBParser.ToPlainText(displayText);
            }
        }

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

        // 记录不支持的特性（每种警告只记录一次，避免日志冗余）
        if (_strokeColor.HasValue && !_warnedStroke)
        {
            Game.Logger.LogWarning("[FGUI] TextField stroke not supported in SCE (this warning will only show once)");
            _warnedStroke = true;
        }
        if (_shadowColor.HasValue && !_warnedShadow)
        {
            Game.Logger.LogWarning("[FGUI] TextField shadow not supported in SCE (this warning will only show once)");
            _warnedShadow = true;
        }
        if (_underline && !_warnedUnderline)
        {
            Game.Logger.LogWarning("[FGUI] TextField underline not supported in SCE (this warning will only show once)");
            _warnedUnderline = true;
        }
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
        // 输入框使用Input控件
        if (NativeObject == null)
        {
            var adapter = Render.SCERenderContext.Instance.Adapter;
            NativeObject = adapter?.CreateInput();
            if (NativeObject != null)
            {
                adapter?.OnInputTextChanged(NativeObject, text =>
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


