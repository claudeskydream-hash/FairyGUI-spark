#if CLIENT
using System.Drawing;
using FairyGUI.Utils;

namespace FairyGUI;

public class AtlasSprite
{
    public PackageItem? Atlas { get; set; }
    public RectangleF Rect { get; set; }
    public PointF Offset { get; set; }
    public SizeF OriginalSize { get; set; }
    public bool Rotated { get; set; }
}

public class MovieClipFrameData
{
    public RectangleF Rect { get; set; }
    public float AddDelay { get; set; }
    public string? SpriteId { get; set; }
}

public class PackageItem
{
    public UIPackage? Owner { get; set; }
    public PackageItemType Type { get; set; }
    public ObjectType ObjectType { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? File { get; set; }
    public bool Exported { get; set; }
    public ByteBuffer? RawData { get; set; }
    public string?[]? Branches { get; set; }
    public string?[]? HighResolution { get; set; }
    public RectangleF? Scale9Grid { get; set; }
    public bool ScaleByTile { get; set; }
    public int TileGridIndice { get; set; }
    public float Interval { get; set; }
    public float RepeatDelay { get; set; }
    public bool Swing { get; set; }
    public List<MovieClipFrameData>? MovieClipFrames { get; set; }
    public byte[]? TextureData { get; set; }
    public AtlasSprite? Sprite { get; set; }

    public object? Load() => Owner?.GetItemAsset(this);

    public PackageItem GetBranch()
    {
        if (Branches != null && Owner != null && Owner.BranchIndex != -1)
        {
            string? itemId = Branches[Owner.BranchIndex];
            if (itemId != null)
            {
                var item = Owner.GetItem(itemId);
                if (item != null) return item;
            }
        }
        return this;
    }
}
#endif

