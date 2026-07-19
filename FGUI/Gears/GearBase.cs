#if CLIENT
using FairyGUI;
using FairyGUI.Utils;

namespace FairyGUI.Gears;

public abstract class GearBase
{
    public static bool DisableAllTweenEffect = false;
    
    protected GObject _owner;
    protected Controller? _controller;
    protected GearTweenConfig? _tweenConfig;

    public GearBase(GObject owner)
    {
        _owner = owner;
    }

    public void Dispose()
    {
        if (_tweenConfig?.Tweener != null)
        {
            _tweenConfig.Tweener.Kill();
            _tweenConfig.Tweener = null;
        }
    }

    public Controller? Controller
    {
        get => _controller;
        set { if (value != _controller) { _controller = value; if (_controller != null) Init(); } }
    }

    public GearTweenConfig TweenConfig => _tweenConfig ??= new GearTweenConfig();

    public virtual void Setup(ByteBuffer buffer)
    {
        var parent = _owner.Parent as GComponent;
        _controller = parent?.GetControllerAt(buffer.ReadShort());
        Init();

        int cnt = buffer.ReadShort();
        if (this is GearDisplay gd)
        {
            gd.Pages = buffer.ReadSArray(cnt)!;
        }
        else if (this is GearDisplay2 gd2)
        {
            gd2.Pages = buffer.ReadSArray(cnt)!;
        }
        else
        {
            for (int i = 0; i < cnt; i++)
            {
                string? page = buffer.ReadS();
                if (page == null) continue;
                AddStatus(page, buffer);
            }
            if (buffer.ReadBool())
                AddStatus(null, buffer);
        }

        if (buffer.ReadBool())
        {
            _tweenConfig = new GearTweenConfig
            {
                EaseType = (EaseType)buffer.ReadByte(),
                Duration = buffer.ReadFloat(),
                Delay = buffer.ReadFloat()
            };
        }

        if (buffer.Version >= 2)
        {
            if (this is GearXY gxy && buffer.ReadBool())
            {
                gxy.PositionsInPercent = true;
                for (int i = 0; i < cnt; i++)
                {
                    string? page = buffer.ReadS();
                    if (page == null) continue;
                    gxy.AddExtStatus(page, buffer);
                }
                if (buffer.ReadBool())
                    gxy.AddExtStatus(null, buffer);
            }
            else if (this is GearDisplay2 gd2)
            {
                gd2.Condition = buffer.ReadByte();
            }
        }
    }

    public virtual void UpdateFromRelations(float dx, float dy) { }

    protected abstract void AddStatus(string? pageId, ByteBuffer buffer);
    protected abstract void Init();
    public abstract void Apply();
    public abstract void UpdateState();
}

public class GearTweenConfig
{
    public bool Tween = true;
    public EaseType EaseType = EaseType.QuadOut;
    public float Duration = 0.3f;
    public float Delay = 0;
    internal uint DisplayLockToken;
    internal GTweener? Tweener;
}
#endif


