#if CLIENT
namespace FairyGUI.Render;

/// <summary>
/// 图集里的图改用<b>原生控件</b>画（控件 Image 属性填引擎原生图集里的逻辑名），不再一张图一个 Canvas。
///
/// <b>为什么</b>：每个 Canvas 各占一块 NanoVG 帧缓冲，引擎同时能持有的约 1000 块，界面一多就满，
/// 新建的图画不出来、客户端也不报错（只在 logs/game/editor-*.log 里报 Failed to create NanoVG framebuffer，2026-10-17）。
/// 原生控件画图集子图不占帧缓冲。
///
/// <b>前提</b>：图集已经登记成引擎原生图集 —— 项目 <c>ui/atlas/atlas.json</c> 登记每张图集页，
/// <c>ui/atlas/fgui/*.json</c> 写每张子图的逻辑名与位置，由 <c>tools/FGUIAtlasConfig/gen_fgui_atlas_config.py</c>
/// 按 FGUI 发布的描述文件生成。逻辑名 = <see cref="LogicalPath"/>，两边必须一致。
///
/// <b>原型阶段</b>：默认关，由项目在 FGUI 初始化前打开 <see cref="Enabled"/>。
/// 不走这条路的：填充图（进度条等）、旋转打包的图 —— 照旧用 Canvas / 专用控件。
///
/// <b>序列帧（MovieClip）</b>：每一帧都是图集里的一张子图（id = <c>动画id_帧号</c>），也登记成原生图集逻辑名；
/// 控件建成普通控件，切帧时换 Image 逻辑名（同一张图集，已加载，不闪）。见 <see cref="CanUse(GMovieClip)"/>。
/// 原来走 Canvas：先画进 Canvas 自己的帧缓冲再贴到界面上，半透明像素颜色被压暗，光效发灰发银（2026-10-23 金币扫光实测）。
/// </summary>
public static class FGUINativeAtlas
{
    /// <summary>总开关。</summary>
    public static bool Enabled { get; set; }

    /// <summary>发布时裁过透明边的图也走原生（原型用来验证引擎按 OriginSize 能不能放对位置）。</summary>
    public static bool IncludeTrimmed { get; set; } = true;

    /// <summary>
    /// 九宫格怎么切：true = 只填逻辑名，按图集配置里的 <c>Border</c> 由引擎切（星火推荐写法）；
    /// false = 另在控件上设 <c>SlicedEdges</c>。
    /// </summary>
    public static bool SliceByConfigBorder { get; set; } = true;

    /// <summary>这张图要不要用原生控件画。</summary>
    public static bool CanUse(GImage image)
    {
        if (!Enabled || image is GMovieClip || image.FillMethod != FillMethod.None)
        {
            return false;
        }

        var item = image.PackageItem;
        var sprite = item?.Sprite;
        if (item?.Owner == null || string.IsNullOrEmpty(item.Id) || sprite == null || sprite.Rotated)
        {
            return false;
        }

        if (!IncludeTrimmed && IsTrimmed(sprite))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 这个序列帧要不要用原生控件画：每一帧的子图都在、没旋转、没裁边。
    /// 裁过边的帧不行 —— 原生图集按 OriginSize 拉满，而 MovieClip 本来也不认帧偏移，帧必须是完整尺寸。
    /// </summary>
    public static bool CanUse(GMovieClip clip)
    {
        if (!Enabled)
        {
            return false;
        }

        var item = clip.PackageItem;
        var owner = item?.Owner;
        var frames = item?.MovieClipFrames;
        if (owner == null || frames == null || frames.Count == 0)
        {
            return false;
        }

        foreach (var frame in frames)
        {
            var sprite = string.IsNullOrEmpty(frame.SpriteId) ? null : owner.GetSprite(frame.SpriteId!);
            if (sprite == null || sprite.Rotated || IsTrimmed(sprite))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>引擎原生图集里的逻辑名（控件 Image 属性写这个，不需要真有这个文件）。</summary>
    public static string LogicalPath(PackageItem item) => $"image/fguiatlas/{item.Owner!.Name}/{item.Id}.png";

    /// <summary>序列帧某一帧的逻辑名（帧子图 id = <c>动画id_帧号</c>，和图集配置生成器一致）。</summary>
    public static string FrameLogicalPath(PackageItem clipItem, string spriteId) => $"image/fguiatlas/{clipItem.Owner!.Name}/{spriteId}.png";

    private static bool IsTrimmed(AtlasSprite sprite)
    {
        const float Epsilon = 0.01f;
        return MathF.Abs(sprite.Offset.X) > Epsilon || MathF.Abs(sprite.Offset.Y) > Epsilon
            || MathF.Abs(sprite.OriginalSize.Width - sprite.Rect.Width) > Epsilon
            || MathF.Abs(sprite.OriginalSize.Height - sprite.Rect.Height) > Epsilon;
    }
}
#endif
