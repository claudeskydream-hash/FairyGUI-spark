#if CLIENT
using SCEFGUI.UI;
using SCEFGUI.Utils;

namespace SCEFGUI.Samples;

/// <summary>
/// MailItem - Custom list item for virtual list demo
/// </summary>
public class MailItem : FGUIButton
{
    private FGUIController? _readController;
    private FGUIController? _fetchController;
    private FGUITextField? _timeText;

    protected override void ConstructExtension(ByteBuffer buffer)
    {
        base.ConstructExtension(buffer);

        _readController = GetController("isRead");
        _fetchController = GetController("isFetched");
        _timeText = GetChild("time") as FGUITextField;
    }

    public void SetRead(bool value)
    {
        if (_readController != null)
            _readController.SelectedIndex = value ? 1 : 0;
    }

    public void SetFetched(bool value)
    {
        if (_fetchController != null)
            _fetchController.SelectedIndex = value ? 1 : 0;
    }

    public void SetTime(string value)
    {
        if (_timeText != null)
            _timeText.Text = value;
    }
}
#endif
