#if CLIENT
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;

namespace FairyGUI.Utils;

/// <summary>
/// UBB标签解析器 - 将UBB/HTML标签转换为纯文本和样式信息
/// </summary>
public class UBBParser
{
    private static UBBParser? _instance;
    public static UBBParser Instance => _instance ??= new UBBParser();

    private readonly Dictionary<string, Func<string, string, string>> _handlers = new();

    public int DefaultImgWidth { get; set; } = 0;
    public int DefaultImgHeight { get; set; } = 0;

    public UBBParser()
    {
        _handlers["url"] = (tagName, value) => "";
        _handlers["/url"] = (tagName, value) => "";
        _handlers["img"] = (tagName, value) => "";
        _handlers["b"] = (tagName, value) => "";
        _handlers["/b"] = (tagName, value) => "";
        _handlers["i"] = (tagName, value) => "";
        _handlers["/i"] = (tagName, value) => "";
        _handlers["u"] = (tagName, value) => "";
        _handlers["/u"] = (tagName, value) => "";
        _handlers["sup"] = (tagName, value) => "";
        _handlers["/sup"] = (tagName, value) => "";
        _handlers["sub"] = (tagName, value) => "";
        _handlers["/sub"] = (tagName, value) => "";
        _handlers["color"] = (tagName, value) => "";
        _handlers["/color"] = (tagName, value) => "";
        _handlers["font"] = (tagName, value) => "";
        _handlers["/font"] = (tagName, value) => "";
        _handlers["size"] = (tagName, value) => "";
        _handlers["/size"] = (tagName, value) => "";
    }

    public void SetHandler(string tagName, Func<string, string, string> handler)
    {
        _handlers[tagName.ToLower()] = handler;
    }

    public string Parse(string text)
    {
        return Parse(text, false);
    }

    public string Parse(string text, bool remove)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = ParseUBB(text, remove);
        text = ParseHTML(text, remove);
        return text;
    }

    private string ParseUBB(string text, bool remove)
    {
        var result = new StringBuilder();
        int lastPos = 0;
        int pos = 0;

        while (pos < text.Length)
        {
            int start = text.IndexOf('[', pos);
            if (start < 0)
            {
                result.Append(text.Substring(pos));
                break;
            }

            int end = text.IndexOf(']', start);
            if (end < 0)
            {
                result.Append(text.Substring(pos));
                break;
            }

            result.Append(text.Substring(pos, start - pos));

            string tag = text.Substring(start + 1, end - start - 1);
            string tagName;
            string tagValue = "";

            int eqPos = tag.IndexOf('=');
            if (eqPos > 0)
            {
                tagName = tag.Substring(0, eqPos).ToLower();
                tagValue = tag.Substring(eqPos + 1);
            }
            else
            {
                tagName = tag.ToLower();
            }

            if (remove)
            {
                // Just remove tags
            }
            else if (_handlers.TryGetValue(tagName, out var handler))
            {
                result.Append(handler(tagName, tagValue));
            }

            pos = end + 1;
        }

        return result.ToString();
    }

    private string ParseHTML(string text, bool remove)
    {
        // Parse HTML-like tags: <b>, </b>, <i>, </i>, <color=#RRGGBB>, <size=20>, <a href="...">, <img src="...">
        var result = new StringBuilder();
        int pos = 0;

        while (pos < text.Length)
        {
            int start = text.IndexOf('<', pos);
            if (start < 0)
            {
                result.Append(text.Substring(pos));
                break;
            }

            int end = text.IndexOf('>', start);
            if (end < 0)
            {
                result.Append(text.Substring(pos));
                break;
            }

            result.Append(text.Substring(pos, start - pos));

            if (remove)
            {
                // Just remove tags
            }
            // Keep the tag as is for now, SCE might handle some HTML

            pos = end + 1;
        }

        return result.ToString();
    }

    /// <summary>
    /// Convert UBB text to plain text (remove all tags)
    /// </summary>
    public static string ToPlainText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Remove UBB tags [xxx]
        text = Regex.Replace(text, @"\[/?[^\]]+\]", "");
        // Remove HTML tags <xxx>
        text = Regex.Replace(text, @"</?[^>]+>", "");

        return text;
    }

    /// <summary>
    /// Parse color from UBB format: #RRGGBB or color name
    /// </summary>
    public static Color ParseColor(string colorStr)
    {
        if (string.IsNullOrEmpty(colorStr))
            return Color.Black;

        colorStr = colorStr.Trim().ToUpper();

        if (colorStr.StartsWith("#"))
        {
            try
            {
                if (colorStr.Length == 7) // #RRGGBB
                {
                    int r = Convert.ToInt32(colorStr.Substring(1, 2), 16);
                    int g = Convert.ToInt32(colorStr.Substring(3, 2), 16);
                    int b = Convert.ToInt32(colorStr.Substring(5, 2), 16);
                    return Color.FromArgb(255, r, g, b);
                }
                else if (colorStr.Length == 9) // #AARRGGBB
                {
                    int a = Convert.ToInt32(colorStr.Substring(1, 2), 16);
                    int r = Convert.ToInt32(colorStr.Substring(3, 2), 16);
                    int g = Convert.ToInt32(colorStr.Substring(5, 2), 16);
                    int b = Convert.ToInt32(colorStr.Substring(7, 2), 16);
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch
            {
                return Color.Black;
            }
        }

        // Named colors
        return colorStr switch
        {
            "RED" => Color.Red,
            "GREEN" => Color.Green,
            "BLUE" => Color.Blue,
            "WHITE" => Color.White,
            "BLACK" => Color.Black,
            "YELLOW" => Color.Yellow,
            "CYAN" => Color.Cyan,
            "MAGENTA" => Color.Magenta,
            "GRAY" or "GREY" => Color.Gray,
            "ORANGE" => Color.Orange,
            "PURPLE" => Color.Purple,
            "PINK" => Color.Pink,
            _ => Color.Black
        };
    }
}

/// <summary>
/// Parsed text element with style information
/// </summary>
public class TextElement
{
    public string Text { get; set; } = "";
    public Color Color { get; set; } = Color.Black;
    public int FontSize { get; set; } = 12;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string? Link { get; set; }
    public string? ImageUrl { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
}

/// <summary>
/// Rich text parser that extracts styled elements
/// </summary>
public class RichTextParser
{
    public static List<TextElement> Parse(string text, Color defaultColor = default, int defaultFontSize = 12)
    {
        var elements = new List<TextElement>();
        if (string.IsNullOrEmpty(text))
            return elements;

        if (defaultColor == default)
            defaultColor = Color.Black;

        var colorStack = new Stack<Color>();
        colorStack.Push(defaultColor);

        var sizeStack = new Stack<int>();
        sizeStack.Push(defaultFontSize);

        bool bold = false;
        bool italic = false;
        bool underline = false;
        string? currentLink = null;

        var currentText = new StringBuilder();

        void FlushText()
        {
            if (currentText.Length > 0)
            {
                elements.Add(new TextElement
                {
                    Text = currentText.ToString(),
                    Color = colorStack.Peek(),
                    FontSize = sizeStack.Peek(),
                    Bold = bold,
                    Italic = italic,
                    Underline = underline,
                    Link = currentLink
                });
                currentText.Clear();
            }
        }

        int pos = 0;
        while (pos < text.Length)
        {
            // Look for UBB tag
            if (text[pos] == '[')
            {
                int end = text.IndexOf(']', pos);
                if (end > pos)
                {
                    FlushText();
                    string tag = text.Substring(pos + 1, end - pos - 1);
                    ProcessTag(tag, ref bold, ref italic, ref underline, ref currentLink,
                               colorStack, sizeStack, elements, defaultColor, defaultFontSize);
                    pos = end + 1;
                    continue;
                }
            }
            // Look for HTML tag
            else if (text[pos] == '<')
            {
                int end = text.IndexOf('>', pos);
                if (end > pos)
                {
                    FlushText();
                    string tag = text.Substring(pos + 1, end - pos - 1);
                    ProcessHtmlTag(tag, ref bold, ref italic, ref underline, ref currentLink,
                                   colorStack, sizeStack, elements, defaultColor, defaultFontSize);
                    pos = end + 1;
                    continue;
                }
            }

            currentText.Append(text[pos]);
            pos++;
        }

        FlushText();
        return elements;
    }

    private static void ProcessTag(string tag, ref bool bold, ref bool italic, ref bool underline,
                                   ref string? currentLink, Stack<Color> colorStack, Stack<int> sizeStack,
                                   List<TextElement> elements, Color defaultColor, int defaultFontSize)
    {
        string tagName;
        string tagValue = "";
        int eqPos = tag.IndexOf('=');
        if (eqPos > 0)
        {
            tagName = tag.Substring(0, eqPos).ToLower();
            tagValue = tag.Substring(eqPos + 1);
        }
        else
        {
            tagName = tag.ToLower();
        }

        switch (tagName)
        {
            case "b":
                bold = true;
                break;
            case "/b":
                bold = false;
                break;
            case "i":
                italic = true;
                break;
            case "/i":
                italic = false;
                break;
            case "u":
                underline = true;
                break;
            case "/u":
                underline = false;
                break;
            case "color":
                colorStack.Push(UBBParser.ParseColor(tagValue));
                break;
            case "/color":
                if (colorStack.Count > 1) colorStack.Pop();
                break;
            case "size":
                if (int.TryParse(tagValue, out int size))
                    sizeStack.Push(size);
                break;
            case "/size":
                if (sizeStack.Count > 1) sizeStack.Pop();
                break;
            case "url":
                currentLink = tagValue;
                break;
            case "/url":
                currentLink = null;
                break;
            case "img":
                elements.Add(new TextElement { ImageUrl = tagValue });
                break;
        }
    }

    private static void ProcessHtmlTag(string tag, ref bool bold, ref bool italic, ref bool underline,
                                       ref string? currentLink, Stack<Color> colorStack, Stack<int> sizeStack,
                                       List<TextElement> elements, Color defaultColor, int defaultFontSize)
    {
        tag = tag.Trim();
        if (tag.StartsWith("/"))
        {
            string endTag = tag.Substring(1).ToLower();
            switch (endTag)
            {
                case "b": bold = false; break;
                case "i": italic = false; break;
                case "u": underline = false; break;
                case "a": currentLink = null; break;
                case "color": if (colorStack.Count > 1) colorStack.Pop(); break;
                case "size": if (sizeStack.Count > 1) sizeStack.Pop(); break;
            }
            return;
        }

        // Parse attributes
        string tagName = tag;
        int spacePos = tag.IndexOf(' ');
        if (spacePos > 0)
            tagName = tag.Substring(0, spacePos);
        tagName = tagName.ToLower();

        switch (tagName)
        {
            case "b": bold = true; break;
            case "i": italic = true; break;
            case "u": underline = true; break;
            case "a":
                var hrefMatch = Regex.Match(tag, @"href=['""]?([^'"">\s]+)['""]?", RegexOptions.IgnoreCase);
                if (hrefMatch.Success)
                    currentLink = hrefMatch.Groups[1].Value;
                break;
            case "img":
                var srcMatch = Regex.Match(tag, @"src=['""]?([^'"">\s]+)['""]?", RegexOptions.IgnoreCase);
                if (srcMatch.Success)
                    elements.Add(new TextElement { ImageUrl = srcMatch.Groups[1].Value });
                break;
        }
    }
}
#endif

