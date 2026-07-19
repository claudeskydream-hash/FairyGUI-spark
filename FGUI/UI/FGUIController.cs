#if CLIENT
using FairyGUI;
using FairyGUI.Utils;

namespace FairyGUI;

public class Controller : EventDispatcher
{
    public string Name { get; set; } = "";
    public GComponent? Parent { get; internal set; }

    private int _selectedIndex = -1;
    private int _previousIndex = -1;
    private readonly List<string> _pageIds = new();
    private readonly List<string> _pageNames = new();
    private readonly List<ControllerAction> _actions = new();
    private bool _changing;
    private bool _autoRadioGroupDepth;

    public int PageCount => _pageIds.Count;
    public bool Changing => _changing;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex != value)
            {
                if (value >= _pageIds.Count) throw new ArgumentOutOfRangeException(nameof(value));
                _changing = true;
                _previousIndex = _selectedIndex;
                _selectedIndex = value;
                Game.Logger.LogInformation($"[FGUI] Controller '{Name}' changed: {_previousIndex} -> {_selectedIndex}, pageId={SelectedPageId}");
                Parent?.ApplyController(this);
                DispatchEvent("onChange", null);
                _changing = false;
            }
        }
    }

    public int PreviousIndex => _previousIndex;
    public string SelectedPage
    {
        get => _selectedIndex >= 0 && _selectedIndex < _pageNames.Count ? _pageNames[_selectedIndex] : "";
        set
        {
            int index = GetPageIndexByName(value);
            if (index >= 0)
            {
                SelectedIndex = index;
            }
        }
    }
    public string PreviousPage => _previousIndex >= 0 && _previousIndex < _pageNames.Count ? _pageNames[_previousIndex] : "";
    public string SelectedPageId
    {
        get => _selectedIndex >= 0 && _selectedIndex < _pageIds.Count ? _pageIds[_selectedIndex] : "";
        set
        {
            int index = ResolvePageIndexByToken(value);
            if (index >= 0)
            {
                SelectedIndex = index;
            }
        }
    }
    internal string OppositePageId
    {
        set
        {
            var index = GetPageIndexById(value);
            if (index > 0)
            {
                SelectedIndex = 0;
            }
            else if (_pageIds.Count > 1)
            {
                SelectedIndex = 1;
            }
        }
    }
    public string? PreviousPageId => _previousIndex >= 0 && _previousIndex < _pageIds.Count ? _pageIds[_previousIndex] : null;

    public string GetPageName(int index) => index >= 0 && index < _pageNames.Count ? _pageNames[index] : "";
    public string GetPageId(int index) => index >= 0 && index < _pageIds.Count ? _pageIds[index] : "";
    public int GetPageIndexById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return -1;
        }

        for (var i = 0; i < _pageIds.Count; i++)
        {
            if (string.Equals(_pageIds[i], id, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public int GetPageIndexByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return -1;
        }

        for (var i = 0; i < _pageNames.Count; i++)
        {
            if (string.Equals(_pageNames[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public int ResolvePageIndexByToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return -1;
        }

        var byId = GetPageIndexById(token);
        if (byId >= 0)
        {
            return byId;
        }

        var byName = GetPageIndexByName(token);
        if (byName >= 0)
        {
            return byName;
        }

        if (int.TryParse(token, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var byIndex)
            && byIndex >= 0
            && byIndex < _pageIds.Count)
        {
            return byIndex;
        }

        return -1;
    }

    public bool SetSelectedPageByToken(string? token)
    {
        var index = ResolvePageIndexByToken(token);
        if (index < 0)
        {
            return false;
        }

        SelectedIndex = index;
        return true;
    }

    public bool IsCurrentPageToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var indexToken = _selectedIndex >= 0
            ? _selectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

        return string.Equals(token, SelectedPageId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, SelectedPage, StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, indexToken, StringComparison.OrdinalIgnoreCase);
    }
    public void AddPage(string name = "") => AddPageAt(name, _pageIds.Count);

    public void AddPageAt(string name, int index)
    {
        string id = $"p{_pageIds.Count}";
        if (index < 0 || index > _pageIds.Count) index = _pageIds.Count;
        _pageIds.Insert(index, id);
        _pageNames.Insert(index, name);
    }

    public void RemovePage(string name) { int index = _pageNames.IndexOf(name); if (index >= 0) RemovePageAt(index); }
    public void RemovePageAt(int index) { _pageIds.RemoveAt(index); _pageNames.RemoveAt(index); if (_selectedIndex >= _pageIds.Count) SelectedIndex = _pageIds.Count - 1; }
    public void ClearPages() { _pageIds.Clear(); _pageNames.Clear(); _selectedIndex = -1; }
    public bool HasPage(string name) => GetPageIndexByName(name) >= 0;

    public void RunActions()
    {
        if (_actions.Count == 0)
            return;

        var prevPageId = PreviousPageId;
        var curPageId = SelectedPageId;
        for (var i = 0; i < _actions.Count; i++)
        {
            _actions[i].Run(this, prevPageId, curPageId);
        }
    }

    public void Setup(ByteBuffer buffer)
    {
        int beginPos = buffer.Position;
        
        // Block 0: Name and autoRadioGroupDepth
        if (!buffer.Seek(beginPos, 0)) return;
        Name = buffer.ReadS() ?? "";
        _autoRadioGroupDepth = buffer.ReadBool();
        
        // Block 1: Pages
        if (!buffer.Seek(beginPos, 1)) return;
        int pageCount = buffer.ReadShort();
        for (int i = 0; i < pageCount; i++)
        {
            _pageIds.Add(buffer.ReadS() ?? "");
            _pageNames.Add(buffer.ReadS() ?? "");
        }
        
        // Determine home page index
        int homePageIndex = 0;
        if (buffer.Version >= 2)
        {
            int homePageType = buffer.ReadByte();
            switch (homePageType)
            {
                case 1: // Specific index
                    homePageIndex = buffer.ReadShort();
                    break;
                case 2: // By branch
                    if (!string.IsNullOrWhiteSpace(UIPackage.Branch))
                    {
                        homePageIndex = _pageNames.IndexOf(UIPackage.Branch!);
                        if (homePageIndex < 0) homePageIndex = 0;
                    }
                    break;
                case 3: // By variable
                    buffer.ReadS(); // Variable key (not supported yet in SCE runtime)
                    break;
            }
        }
        
        // Block 2: Actions
        _actions.Clear();
        if (buffer.Seek(beginPos, 2))
        {
            int actionCount = buffer.ReadShort();
            for (int i = 0; i < actionCount; i++)
            {
                int nextPos = buffer.ReadUshort() + buffer.Position;
                var actionType = (ControllerAction.ActionType)buffer.ReadByte();
                var action = ControllerAction.CreateAction(actionType);
                if (action != null)
                {
                    action.Setup(buffer);
                    _actions.Add(action);
                }
                buffer.Position = nextPos;
            }
        }
        
        if (Parent != null && _pageIds.Count > 0)
            _selectedIndex = homePageIndex;
    }
}
#endif

