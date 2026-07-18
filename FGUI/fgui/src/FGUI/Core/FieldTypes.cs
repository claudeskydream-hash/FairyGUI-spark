#if CLIENT
namespace FairyGUI;

public enum PackageItemType
{
    Image,
    MovieClip,
    Sound,
    Component,
    Atlas,
    Font,
    Swf,
    Misc,
    Unknown,
    Spine,
    DragonBones
}

public enum ObjectType
{
    Image,
    MovieClip,
    Swf,
    Graph,
    Loader,
    Group,
    Text,
    RichText,
    InputText,
    Component,
    List,
    Label,
    Button,
    ComboBox,
    ProgressBar,
    Slider,
    ScrollBar,
    Tree,
    Loader3D
}

public enum AlignType
{
    Left,
    Center,
    Right
}

public enum VertAlignType
{
    Top,
    Middle,
    Bottom
}

public enum OverflowType
{
    Visible,
    Hidden,
    Scroll
}

public enum FillType
{
    None,
    Scale,
    ScaleMatchHeight,
    ScaleMatchWidth,
    ScaleFree,
    ScaleNoBorder
}

public enum AutoSizeType
{
    None,
    Both,
    Height,
    Shrink,
    Ellipsis
}

public enum ScrollType
{
    Horizontal,
    Vertical,
    Both
}

public enum ScrollBarDisplayType
{
    Default,
    Visible,
    Auto,
    Hidden
}

public enum RelationType
{
    Left_Left,
    Left_Center,
    Left_Right,
    Center_Center,
    Right_Left,
    Right_Center,
    Right_Right,

    Top_Top,
    Top_Middle,
    Top_Bottom,
    Middle_Middle,
    Bottom_Top,
    Bottom_Middle,
    Bottom_Bottom,

    Width,
    Height,

    LeftExt_Left,
    LeftExt_Right,
    RightExt_Left,
    RightExt_Right,
    TopExt_Top,
    TopExt_Bottom,
    BottomExt_Top,
    BottomExt_Bottom,

    Size
}

public enum ListLayoutType
{
    SingleColumn,
    SingleRow,
    FlowHorizontal,
    FlowVertical,
    Pagination
}

public enum ListSelectionMode
{
    Single,
    Multiple,
    Multiple_SingleClick,
    None
}

public enum ProgressTitleType
{
    Percent,
    ValueAndMax,
    Value,
    Max
}

public enum TransitionActionType
{
    XY,
    Size,
    Scale,
    Pivot,
    Alpha,
    Rotation,
    Color,
    Animation,
    Visible,
    Sound,
    Transition,
    Shake,
    ColorFilter,
    Skew,
    Text,
    Icon,
    Unknown
}

public enum GroupLayoutType
{
    None,
    Horizontal,
    Vertical
}

public enum ChildrenRenderOrder
{
    Ascent,
    Descent,
    Arch,
}

public enum PopupDirection
{
    Auto,
    Up,
    Down
}

public enum FlipType
{
    None,
    Horizontal,
    Vertical,
    Both
}

public enum FillMethod
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Radial90 = 3,
    Radial180 = 4,
    Radial360 = 5,
}

public enum OriginHorizontal
{
    Left,
    Right,
}

public enum OriginVertical
{
    Top,
    Bottom
}

public enum Origin90
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public enum Origin180
{
    Top,
    Bottom,
    Left,
    Right
}

public enum Origin360
{
    Top,
    Bottom,
    Left,
    Right
}

public enum BlendMode
{
    Normal,
    None,
    Add,
    Multiply,
    Screen,
    Erase,
    Mask,
    Below,
    Off,
    Custom1,
    Custom2,
    Custom3
}

public enum EaseType
{
    Linear,
    SineIn,
    SineOut,
    SineInOut,
    QuadIn,
    QuadOut,
    QuadInOut,
    CubicIn,
    CubicOut,
    CubicInOut,
    QuartIn,
    QuartOut,
    QuartInOut,
    QuintIn,
    QuintOut,
    QuintInOut,
    ExpoIn,
    ExpoOut,
    ExpoInOut,
    CircIn,
    CircOut,
    CircInOut,
    ElasticIn,
    ElasticOut,
    ElasticInOut,
    BackIn,
    BackOut,
    BackInOut,
    BounceIn,
    BounceOut,
    BounceInOut,
    Custom
}

public struct Margin
{
    public int Left, Right, Top, Bottom;
    
    public Margin(int left, int right, int top, int bottom)
    {
        Left = left; Right = right; Top = top; Bottom = bottom;
    }
}

public enum ButtonMode
{
    Common,
    Check,
    Radio
}

public enum ObjectSortingOrder
{
    Ascend,
    Descend,
    Arch
}
#endif

