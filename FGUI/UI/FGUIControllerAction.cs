#if CLIENT
using FairyGUI.Utils;

namespace FairyGUI;

public abstract class ControllerAction
{
    public enum ActionType
    {
        PlayTransition = 0,
        ChangePage = 1
    }

    protected string[]? FromPage;
    protected string[]? ToPage;

    public static ControllerAction? CreateAction(ActionType type) =>
        type switch
        {
            ActionType.PlayTransition => new PlayTransitionAction(),
            ActionType.ChangePage => new ChangePageAction(),
            _ => null
        };

    public void Run(Controller controller, string? prevPageId, string? curPageId)
    {
        if (MatchPages(FromPage, prevPageId, controller.PreviousPage, controller.PreviousIndex)
            && MatchPages(ToPage, curPageId, controller.SelectedPage, controller.SelectedIndex))
            Enter(controller);
        else
            Leave(controller);
    }

    private static bool MatchPages(string[]? pages, string? pageId, string? pageName, int pageIndex)
    {
        if (pages == null || pages.Length == 0)
            return true;

        var pageIndexToken = pageIndex >= 0
            ? pageIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        for (var i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            if (string.IsNullOrWhiteSpace(page))
            {
                continue;
            }

            if ((!string.IsNullOrWhiteSpace(pageId) && string.Equals(page, pageId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(pageName) && string.Equals(page, pageName, StringComparison.OrdinalIgnoreCase))
                || string.Equals(page, pageIndexToken, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    protected virtual void Enter(Controller controller) { }
    protected virtual void Leave(Controller controller) { }

    public virtual void Setup(ByteBuffer buffer)
    {
        var fromCnt = buffer.ReadShort();
        FromPage = new string[fromCnt];
        for (var i = 0; i < fromCnt; i++)
            FromPage[i] = buffer.ReadS() ?? string.Empty;

        var toCnt = buffer.ReadShort();
        ToPage = new string[toCnt];
        for (var i = 0; i < toCnt; i++)
            ToPage[i] = buffer.ReadS() ?? string.Empty;
    }
}

public sealed class ChangePageAction : ControllerAction
{
    private string? _objectId;
    private string? _controllerName;
    private string? _targetPage;

    protected override void Enter(Controller controller)
    {
        if (string.IsNullOrEmpty(_controllerName) || controller.Parent == null)
            return;

        GComponent? component;
        if (!string.IsNullOrEmpty(_objectId))
            component = controller.Parent.GetChildById(_objectId) as GComponent;
        else
            component = controller.Parent;

        if (component == null)
            return;

        var targetController = component.GetController(_controllerName);
        if (targetController == null || ReferenceEquals(targetController, controller) || targetController.Changing)
            return;

        if (_targetPage == "~1")
        {
            if (controller.SelectedIndex >= 0 && controller.SelectedIndex < targetController.PageCount)
                targetController.SelectedIndex = controller.SelectedIndex;
            return;
        }

        if (_targetPage == "~2")
        {
            targetController.SelectedPage = controller.SelectedPage;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_targetPage))
            targetController.SelectedPageId = _targetPage;
    }

    public override void Setup(ByteBuffer buffer)
    {
        base.Setup(buffer);
        _objectId = buffer.ReadS();
        _controllerName = buffer.ReadS();
        _targetPage = buffer.ReadS();
    }
}

public sealed class PlayTransitionAction : ControllerAction
{
    private string? _transitionName;
    private int _playTimes = 1;
    private float _delay;
    private bool _stopOnExit;
    private Transition? _currentTransition;

    protected override void Enter(Controller controller)
    {
        if (controller.Parent == null || string.IsNullOrWhiteSpace(_transitionName))
            return;

        var transition = controller.Parent.GetTransition(_transitionName);
        if (transition == null)
            return;

        if (_currentTransition != null && _currentTransition.Playing)
            transition.ChangePlayTimes(_playTimes);
        else
            transition.Play(null, _playTimes, _delay);

        _currentTransition = transition;
    }

    protected override void Leave(Controller controller)
    {
        if (_stopOnExit && _currentTransition != null)
        {
            _currentTransition.Stop();
            _currentTransition = null;
        }
    }

    public override void Setup(ByteBuffer buffer)
    {
        base.Setup(buffer);
        _transitionName = buffer.ReadS();
        _playTimes = buffer.ReadInt();
        _delay = buffer.ReadFloat();
        _stopOnExit = buffer.ReadBool();
    }
}
#endif

