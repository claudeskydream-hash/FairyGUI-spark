#if CLIENT
using System.Drawing;
using FairyGUI;
using FairyGUI.Utils;

namespace FairyGUI;

public class GLabel : GComponent, IColorGear
{
    protected GObject? _titleObject;
    protected GObject? _iconObject;
    private string? _title;
    private string? _icon;

    public override string? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            if (_iconObject != null)
                _iconObject.Icon = ResolveIconUrl(value);
        }
    }

    public string? Title
    {
        get => _title;
        set { _title = value; if (_titleObject != null) _titleObject.Text = value; }
    }

    public override string? Text
    {
        get => Title;
        set => Title = value;
    }

    public bool Editable
    {
        get => (_titleObject as GTextInput)?.Editable ?? false;
        set { if (_titleObject is GTextInput input) input.Editable = value; }
    }

    public Color TitleColor
    {
        get => GetTextField()?.Color ?? Color.Black;
        set { var tf = GetTextField(); if (tf != null) tf.Color = value; }
    }

    public int TitleFontSize
    {
        get => GetTextField()?.FontSize ?? 0;
        set { var tf = GetTextField(); if (tf != null) tf.FontSize = value; }
    }

    public Color Color
    {
        get => TitleColor;
        set => TitleColor = value;
    }

    public GTextField? GetTextField()
    {
        if (_titleObject is GTextField tf) return tf;
        if (_titleObject is GLabel label) return label.GetTextField();
        if (_titleObject is GButton btn) return btn.GetTextField();
        return null;
    }

    protected override void ConstructExtension(ByteBuffer buffer)
    {
        _titleObject = GetChild("title");
        _iconObject = GetChild("icon");
        
        // Apply title/icon that was set in Setup_AfterAdd to the child objects
        if (_titleObject != null && !string.IsNullOrEmpty(_title))
            _titleObject.Text = _title;
        if (_iconObject != null && !string.IsNullOrEmpty(_icon))
            _iconObject.Icon = ResolveIconUrl(_icon);
    }

    public override void Setup_AfterAdd(ByteBuffer buffer, int beginPos)
    {
        base.Setup_AfterAdd(buffer, beginPos);

        if (!buffer.Seek(beginPos, 6))
            return;

        if ((ObjectType)buffer.ReadByte() != PackageItem?.ObjectType)
            return;

        string? str = buffer.ReadS();
        if (str != null) Title = str;

        str = buffer.ReadS();
        if (str != null) Icon = str;

        if (buffer.ReadBool())
            TitleColor = buffer.ReadColor();

        int iv = buffer.ReadInt();
        if (iv != 0) TitleFontSize = iv;

        if (buffer.ReadBool())
        {
            var input = GetTextField() as GTextInput;
            if (input != null)
            {
                str = buffer.ReadS();
                if (str != null) input.PromptText = str;
                str = buffer.ReadS();
                if (str != null) input.Restrict = str;
                iv = buffer.ReadInt();
                if (iv != 0) input.MaxLength = iv;
                buffer.ReadInt(); // keyboardType
                if (buffer.ReadBool()) input.Password = true;
            }
            else
                buffer.Skip(13);
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
            Game.Logger.LogWarning("[FGUI][Icon] label icon unresolved raw={Raw} owner={Owner}", iconKey, owner.Name);
            return rawIcon;
        }

        return UIPackage.URL_PREFIX + owner.Id + item.Id;
    }
}
#endif

