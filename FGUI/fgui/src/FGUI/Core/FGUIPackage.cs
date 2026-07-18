#if CLIENT
using System.Drawing;
using FairyGUI.Utils;

namespace FairyGUI;

public class UIPackage
{
    public const string URL_PREFIX = "ui://";
    public const uint FGUI_MAGIC = 0x46475549;

    public string? Id { get; private set; }
    public string? Name { get; private set; }
    public string? AssetPath => _assetPath;
    public int BranchIndex { get; internal set; } = -1;

    private readonly List<PackageItem> _items = new();
    private readonly Dictionary<string, PackageItem> _itemsById = new();
    private readonly Dictionary<string, PackageItem> _itemsByName = new();
    private readonly Dictionary<string, AtlasSprite> _sprites = new();
    private Dictionary<string, string>[]? _dependencies;
    private string?[]? _branches;

    private static readonly Dictionary<string, UIPackage> _packageInstById = new();
    private static readonly Dictionary<string, UIPackage> _packageInstByName = new();
    private static readonly List<UIPackage> _packageList = new();
    private static string? _branch;

    public delegate byte[]? LoadResourceFunc(string name, string extension);
    private LoadResourceFunc? _loadFunc;
    private string? _assetPath;

    public static string? Branch
    {
        get => _branch;
        set
        {
            _branch = value;
            bool empty = string.IsNullOrEmpty(_branch);
            foreach (var pkg in _packageInstById.Values)
            {
                if (empty) pkg.BranchIndex = -1;
                else if (pkg._branches != null)
                    pkg.BranchIndex = Array.IndexOf(pkg._branches, value);
            }
        }
    }

    public static UIPackage? GetById(string id) =>
        _packageInstById.TryGetValue(id, out var pkg) ? pkg : null;

    public static UIPackage? GetByName(string name) =>
        _packageInstByName.TryGetValue(name, out var pkg) ? pkg : null;

    public static GObject? CreateObject(string packageName, string componentName) =>
        GetByName(packageName)?.CreateObject(componentName);

    public static UIPackage? AddPackage(byte[] descData, string assetNamePrefix, LoadResourceFunc loadFunc)
    {
        ByteBuffer buffer = new ByteBuffer(descData);
        UIPackage pkg = new UIPackage { _loadFunc = loadFunc, _assetPath = assetNamePrefix };
        if (!pkg.LoadPackage(buffer, assetNamePrefix)) return null;
        if (pkg.Id != null) _packageInstById[pkg.Id] = pkg;
        if (pkg.Name != null) _packageInstByName[pkg.Name] = pkg;
        _packageList.Add(pkg);
        return pkg;
    }

    public static UIPackage? AddPackage(string filePath, LoadResourceFunc loadFunc)
    {
        string assetPath = Path.GetDirectoryName(filePath) ?? "";
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        byte[]? descData = loadFunc(Path.Combine(assetPath, fileName + "_fui"), ".bytes");
        if (descData == null)
        {
            descData = loadFunc(filePath, "");
            if (descData == null)
                throw new Exception($"FGUI: Cannot load ui package from '{filePath}'");
        }
        return AddPackage(descData, Path.Combine(assetPath, fileName), loadFunc);
    }

    public static void RemovePackage(string packageIdOrName)
    {
        UIPackage? pkg = null;
        if (!_packageInstById.TryGetValue(packageIdOrName, out pkg))
            if (!_packageInstByName.TryGetValue(packageIdOrName, out pkg))
                throw new Exception($"FGUI: '{packageIdOrName}' is not a valid package id or name.");
        if (pkg.Id != null) _packageInstById.Remove(pkg.Id);
        if (pkg.Name != null) _packageInstByName.Remove(pkg.Name);
        _packageList.Remove(pkg);
    }

    public static void RemoveAllPackages()
    {
        _packageList.Clear();
        _packageInstById.Clear();
        _packageInstByName.Clear();
    }

    public static List<UIPackage> GetPackages() => _packageList;

    public static string? GetItemURL(string pkgName, string resName)
    {
        var pkg = GetByName(pkgName);
        if (pkg == null) return null;
        if (!pkg._itemsByName.TryGetValue(resName, out var pi)) return null;
        return URL_PREFIX + pkg.Id + pi.Id;
    }

    public static PackageItem? GetItemByURL(string? url)
    {
        if (url == null) return null;
        int pos1 = url.IndexOf("//");
        if (pos1 == -1) return null;
        int pos2 = url.IndexOf('/', pos1 + 2);
        if (pos2 == -1)
        {
            if (url.Length > 13)
            {
                string pkgId = url.Substring(5, 8);
                var pkg = GetById(pkgId);
                if (pkg != null)
                {
                    string srcId = url.Substring(13);
                    return pkg.GetItem(srcId);
                }
            }
        }
        else
        {
            string pkgName = url.Substring(pos1 + 2, pos2 - pos1 - 2);
            var pkg = GetByName(pkgName);
            if (pkg != null)
            {
                string srcName = url.Substring(pos2 + 1);
                return pkg.GetItemByName(srcName);
            }
        }
        return null;
    }

    private bool LoadPackage(ByteBuffer buffer, string assetNamePrefix)
    {
        Game.Logger.LogInformation($"[FGUI] LoadPackage: buffer.Length={buffer.Length}");
        
        if (buffer.ReadUint() != FGUI_MAGIC)
            throw new Exception($"FGUI: old package format found in '{assetNamePrefix}'");

        buffer.Version = buffer.ReadInt();
        bool ver2 = buffer.Version >= 2;
        buffer.ReadBool();
        Id = buffer.ReadString();
        Name = buffer.ReadString();
        
        Game.Logger.LogInformation($"[FGUI] Package: {Name}, ID: {Id}, Version: {buffer.Version}");

        if (Id != null && _packageInstById.TryGetValue(Id, out var existingPkg))
        {
            if (Name != existingPkg.Name)
                Game.Logger.LogWarning("FGUI: Package conflicts, '{Name}' and '{ExistingName}'", Name, existingPkg.Name);
            return false;
        }

        buffer.Skip(20);
        int indexTablePos = buffer.Position;

        buffer.Seek(indexTablePos, 4);
        int cnt = buffer.ReadInt();
        string[] stringTable = new string[cnt];
        for (int i = 0; i < cnt; i++)
            stringTable[i] = buffer.ReadString();
        buffer.StringTable = stringTable;

        if (buffer.Seek(indexTablePos, 5))
        {
            cnt = buffer.ReadInt();
            for (int i = 0; i < cnt; i++)
            {
                int index = buffer.ReadUshort();
                int len = buffer.ReadInt();
                stringTable[index] = buffer.ReadString(len);
            }
        }

        buffer.Seek(indexTablePos, 0);
        cnt = buffer.ReadShort();
        _dependencies = new Dictionary<string, string>[cnt];
        for (int i = 0; i < cnt; i++)
        {
            var kv = new Dictionary<string, string>();
            var depId = buffer.ReadS();
            var depName = buffer.ReadS();
            if (depId != null) kv["id"] = depId;
            if (depName != null) kv["name"] = depName;
            _dependencies[i] = kv;
        }

        bool branchIncluded = false;
        if (ver2)
        {
            cnt = buffer.ReadShort();
            if (cnt > 0)
            {
                _branches = buffer.ReadSArray(cnt);
                if (!string.IsNullOrEmpty(_branch))
                    BranchIndex = Array.IndexOf(_branches!, _branch);
                branchIncluded = true;
            }
        }

        buffer.Seek(indexTablePos, 1);
        string assetPath;
        if (assetNamePrefix.Length > 0)
        {
            assetPath = Path.GetDirectoryName(assetNamePrefix) ?? "";
            if (assetPath.Length > 0) assetPath += "/";
            assetNamePrefix = Path.GetFileName(assetNamePrefix) + "_";
        }
        else assetPath = string.Empty;

        cnt = buffer.ReadShort();
        for (int i = 0; i < cnt; i++)
        {
            int nextPos = buffer.ReadInt();
            nextPos += buffer.Position;

            PackageItem pi = new PackageItem
            {
                Owner = this,
                Type = (PackageItemType)buffer.ReadByte(),
                Id = buffer.ReadS(),
                Name = buffer.ReadS()
            };
            buffer.ReadS();
            pi.File = buffer.ReadS();
            pi.Exported = buffer.ReadBool();
            pi.Width = buffer.ReadInt();
            pi.Height = buffer.ReadInt();

            switch (pi.Type)
            {
                case PackageItemType.Image:
                    pi.ObjectType = ObjectType.Image;
                    int scaleOption = buffer.ReadByte();
                    if (scaleOption == 1)
                    {
                        pi.Scale9Grid = new RectangleF(buffer.ReadInt(), buffer.ReadInt(), buffer.ReadInt(), buffer.ReadInt());
                        pi.TileGridIndice = buffer.ReadInt();
                    }
                    else if (scaleOption == 2) pi.ScaleByTile = true;
                    buffer.ReadBool();
                    break;
                case PackageItemType.MovieClip:
                    buffer.ReadBool();
                    pi.ObjectType = ObjectType.MovieClip;
                    pi.RawData = buffer.ReadBuffer();
                    break;
                case PackageItemType.Font:
                    pi.RawData = buffer.ReadBuffer();
                    break;
                case PackageItemType.Component:
                    int extension = buffer.ReadByte();
                    pi.ObjectType = extension > 0 ? (ObjectType)extension : ObjectType.Component;
                    pi.RawData = buffer.ReadBuffer();
                    Game.Logger.LogInformation($"[FGUI] Component: {pi.Name}, RawData.Length={pi.RawData?.Length ?? 0}, StringTable.Length={pi.RawData?.StringTable?.Length ?? 0}");
                    break;
                case PackageItemType.Atlas:
                case PackageItemType.Sound:
                case PackageItemType.Misc:
                    pi.File = assetNamePrefix + pi.File;
                    break;
                case PackageItemType.Spine:
                case PackageItemType.DragonBones:
                    pi.File = assetPath + pi.File;
                    buffer.ReadFloat();
                    buffer.ReadFloat();
                    break;
            }

            if (ver2)
            {
                string? str = buffer.ReadS();
                if (str != null) pi.Name = str + "/" + pi.Name;
                int branchCnt = buffer.ReadByte();
                if (branchCnt > 0)
                {
                    if (branchIncluded) pi.Branches = buffer.ReadSArray(branchCnt);
                    else
                    {
                        var branchId = buffer.ReadS();
                        if (branchId != null) _itemsById[branchId] = pi;
                    }
                }
                int highResCnt = buffer.ReadByte();
                if (highResCnt > 0) pi.HighResolution = buffer.ReadSArray(highResCnt);
            }

            _items.Add(pi);
            if (pi.Id != null) _itemsById[pi.Id] = pi;
            if (pi.Name != null) _itemsByName[pi.Name] = pi;
            buffer.Position = nextPos;
        }

        buffer.Seek(indexTablePos, 2);
        cnt = buffer.ReadShort();
        for (int i = 0; i < cnt; i++)
        {
            int nextPos = buffer.ReadUshort();
            nextPos += buffer.Position;
            string? itemId = buffer.ReadS();
            string? atlasId = buffer.ReadS();
            if (atlasId != null && _itemsById.TryGetValue(atlasId, out var atlasItem))
            {
                AtlasSprite sprite = new AtlasSprite { Atlas = atlasItem };
                sprite.Rect = new RectangleF(buffer.ReadInt(), buffer.ReadInt(), buffer.ReadInt(), buffer.ReadInt());
                sprite.Rotated = buffer.ReadBool();
                if (ver2 && buffer.ReadBool())
                {
                    sprite.Offset = new PointF(buffer.ReadInt(), buffer.ReadInt());
                    sprite.OriginalSize = new SizeF(buffer.ReadInt(), buffer.ReadInt());
                }
                else if (sprite.Rotated) sprite.OriginalSize = new SizeF(sprite.Rect.Height, sprite.Rect.Width);
                else sprite.OriginalSize = new SizeF(sprite.Rect.Width, sprite.Rect.Height);
                if (itemId != null) _sprites[itemId] = sprite;
            }
            buffer.Position = nextPos;
        }
        return true;
    }

    public PackageItem? GetItem(string itemId) =>
        _itemsById.TryGetValue(itemId, out var pi) ? pi : null;

    public PackageItem? GetItemByName(string itemName) =>
        _itemsByName.TryGetValue(itemName, out var pi) ? pi : null;

    public List<PackageItem> GetItems() => _items;

    public bool TryGetDesignResolution(out float width, out float height, string? preferredComponentName = null)
    {
        width = 0f;
        height = 0f;

        PackageItem? candidate = null;
        _ = preferredComponentName;

        if (candidate == null)
        {
            foreach (var item in _items)
            {
                if (item.Type != PackageItemType.Component || string.IsNullOrWhiteSpace(item.Name))
                {
                    continue;
                }

                if (item.Name.Equals("Main", StringComparison.OrdinalIgnoreCase) ||
                    item.Name.EndsWith("/Main", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = item;
                    break;
                }
            }
        }

        if (candidate == null)
        {
            var maxArea = -1;
            foreach (var item in _items)
            {
                if (item.Type != PackageItemType.Component || item.Width <= 0 || item.Height <= 0)
                {
                    continue;
                }

                var area = item.Width * item.Height;
                if (area > maxArea)
                {
                    maxArea = area;
                    candidate = item;
                }
            }
        }

        if (candidate == null || candidate.Width <= 0 || candidate.Height <= 0)
        {
            return false;
        }

        width = candidate.Width;
        height = candidate.Height;
        return true;
    }

    public AtlasSprite? GetSprite(string itemId) =>
        _sprites.TryGetValue(itemId, out var sprite) ? sprite : null;

    public object? GetItemAsset(PackageItem item)
    {
        switch (item.Type)
        {
            case PackageItemType.Image:
                if (item.Sprite == null) LoadImage(item);
                return item.Sprite;
            case PackageItemType.MovieClip:
                if (item.MovieClipFrames == null) LoadMovieClip(item);
                return item.MovieClipFrames;
            case PackageItemType.Atlas:
                if (item.TextureData == null) LoadAtlas(item);
                return item.TextureData;
            case PackageItemType.Component:
                return item.RawData;
            default:
                return null;
        }
    }

    private void LoadAtlas(PackageItem item)
    {
        // In SCE, we don't need to load atlas data - just mark it as loaded
        // The actual image will be loaded by SCE using path like "image/ui/xxx.png"
        if (item.File == null) return;
        item.TextureData = new byte[0]; // Mark as loaded (non-null)
        Game.Logger.LogInformation($"[FGUI] Atlas marked as loaded: {item.File}");
    }

    private void LoadImage(PackageItem item)
    {
        if (item.Id == null) return;
        if (_sprites.TryGetValue(item.Id, out var sprite))
        {
            item.Sprite = sprite;
            if (sprite.Atlas != null) GetItemAsset(sprite.Atlas);
        }
    }

    private void LoadMovieClip(PackageItem item)
    {
        if (item.RawData == null)
        {
            item.MovieClipFrames = [];
            return;
        }

        var buffer = item.RawData;
        buffer.Seek(0, 0);
        item.Interval = buffer.ReadInt() / 1000f;
        item.Swing = buffer.ReadBool();
        item.RepeatDelay = buffer.ReadInt() / 1000f;

        if (!buffer.Seek(0, 1))
        {
            item.MovieClipFrames = [];
            return;
        }

        var frameCount = (int)buffer.ReadShort();
        var frames = new List<MovieClipFrameData>(Math.Max(frameCount, 0));

        for (var i = 0; i < frameCount; i++)
        {
            var nextPos = (int)buffer.ReadUshort();
            nextPos += buffer.Position;

            var frame = new MovieClipFrameData
            {
                Rect = new RectangleF(
                    buffer.ReadInt(),
                    buffer.ReadInt(),
                    buffer.ReadInt(),
                    buffer.ReadInt()),
                AddDelay = buffer.ReadInt() / 1000f,
                SpriteId = buffer.ReadS(),
            };
            frames.Add(frame);

            buffer.Position = nextPos;
        }

        item.MovieClipFrames = frames;
    }

    public GObject? CreateObject(string resName)
    {
        if (!_itemsByName.TryGetValue(resName, out var pi))
        {
            Game.Logger.LogWarning("FGUI: resource not found - {ResName} in {Name}", resName, Name);
            return null;
        }
        return CreateObject(pi);
    }

    public GObject? CreateObject(PackageItem item)
    {
        GetItemAsset(item);
        GObject? obj = UIObjectFactory.NewObject(item);
        if (obj == null) return null;
        obj.PackageItem = item;
        obj.ResourceUrl = URL_PREFIX + Id + item.Id;
        obj.ConstructFromResource();
        return obj;
    }
    
    public static GObject? CreateObjectFromURL(string url)
    {
        var pi = GetItemByURL(url);
        if (pi?.Owner == null) return null;
        return pi.Owner.CreateObject(pi);
    }
    
    public static string NormalizeURL(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        if (url.StartsWith(URL_PREFIX)) return url;
        return url;
    }
}
#endif

