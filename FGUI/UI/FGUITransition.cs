#if CLIENT
using System.Drawing;
using FairyGUI;
using FairyGUI.Utils;

namespace FairyGUI;

public delegate void PlayCompleteCallback();

public class Transition
{
    public string Name { get; set; } = "";
    public GComponent? Owner { get; set; }

    private List<TransitionItem> _items = new();
    private int _totalTimes;
    private int _totalTasks;
    private bool _playing;
    private bool _paused;
    private float _ownerBaseX;
    private float _ownerBaseY;
    private PlayCompleteCallback? _onComplete;
    private bool _reversed;
    private float _totalDuration;
    private bool _autoPlay;
    private int _autoPlayTimes = 1;
    private float _autoPlayDelay;
    private float _timeScale = 1;
    private float _startTime;
    private float _endTime = -1;
    private int _decodeDiagLogCount;
    private int _relationDiagLogCount;
    private const int DecodeDiagLogLimit = 24;
    private const int RelationDiagLogLimit = 64;

    public bool Playing => _playing;
    public bool Paused { get => _paused; set => _paused = value; }
    public float TimeScale { get => _timeScale; set => _timeScale = value; }

    public void ChangePlayTimes(int value)
    {
        _totalTimes = value;
    }

    public void Play(PlayCompleteCallback? onComplete = null, int times = 1, float delay = 0)
    {
        PlayInternal(times, delay, 0, -1, onComplete, false);
    }

    public void PlayReverse(PlayCompleteCallback? onComplete = null, int times = 1, float delay = 0)
    {
        PlayInternal(times, delay, 0, -1, onComplete, true);
    }

    private void PlayInternal(int times, float delay, float startTime, float endTime, PlayCompleteCallback? onComplete, bool reversed)
    {
        Stop(true, false);
        _totalTimes = times;
        _reversed = reversed;
        _startTime = startTime;
        _endTime = endTime;
        _playing = true;
        _paused = false;
        _onComplete = onComplete;

        if (Owner != null)
        {
            _ownerBaseX = Owner.X;
            _ownerBaseY = Owner.Y;
        }

        _totalTasks = 0;
        bool needSkipAnimations = false;
        int cnt = _items.Count;

        if (delay == 0)
        {
            for (int i = 0; i < cnt; i++)
            {
                var item = _items[i];
                if (item.Target == null) continue;

                if (item.Type == TransitionActionType.Animation && startTime != 0)
                    needSkipAnimations = true;

                PlayItem(item);
            }
        }
        else
        {
            GTween.DelayedCall(delay, () =>
            {
                for (int i = 0; i < cnt; i++)
                {
                    var item = _items[i];
                    if (item.Target == null) continue;
                    PlayItem(item);
                }
            });
        }
    }

    private void PlayItem(TransitionItem item)
    {
        if (item.TweenConfig != null)
        {
            float startTime = _reversed ? (_totalDuration - item.Time - item.TweenConfig.Duration) : item.Time;
            if (_endTime >= 0 && startTime > _endTime) return;

            _totalTasks++;
            float delay = startTime > _startTime ? startTime - _startTime : 0;
            StartTween(item, delay);
        }
        else
        {
            float time = _reversed ? (_totalDuration - item.Time) : item.Time;
            if (time <= _startTime)
                ApplyValue(item);
            else if (_endTime < 0 || time <= _endTime)
            {
                _totalTasks++;
                float delay = time - _startTime;
                GTween.DelayedCall(delay, () =>
                {
                    _totalTasks--;
                    ApplyValue(item);
                    CheckAllComplete();
                });
            }
        }
    }

    private void StartTween(TransitionItem item, float delay)
    {
        if (item.TweenConfig == null || item.Target == null) return;

        var tweener = GTween.To(0f, 1f, item.TweenConfig.Duration)
            .SetDelay(delay)
            .SetEase(item.TweenConfig.EaseType)
            .SetTarget(item)
            .OnUpdate(t => OnTweenUpdate(item, t.NormalizedTime))
            .OnComplete(t => OnTweenComplete(item));
    }

    private void OnTweenUpdate(TransitionItem item, float ratio)
    {
        if (item.Target == null) return;
        ApplyValue(item, ratio);
    }

    private void OnTweenComplete(TransitionItem item)
    {
        _totalTasks--;
        CheckAllComplete();
    }

    private void ApplyValue(TransitionItem item, float ratio = 1)
    {
        if (item.Target == null) return;
        var target = item.Target;

        switch (item.Type)
        {
            case TransitionActionType.XY:
                {
                    var start = item.StartValue;
                    var end = item.EndValue;
                    float x = start.X + (end.X - start.X) * ratio;
                    float y = start.Y + (end.Y - start.Y) * ratio;
                    if (item.TweenConfig?.Path != null)
                    {
                        // Path animation - simplified
                    }
                    target.SetXY(x, y);
                }
                break;
            case TransitionActionType.Size:
                {
                    var start = item.StartValue;
                    var end = item.EndValue;
                    float w = start.X + (end.X - start.X) * ratio;
                    float h = start.Y + (end.Y - start.Y) * ratio;
                    target.SetSize(w, h);
                }
                break;
            case TransitionActionType.Scale:
                {
                    var start = item.StartValue;
                    var end = item.EndValue;
                    float sx = start.X + (end.X - start.X) * ratio;
                    float sy = start.Y + (end.Y - start.Y) * ratio;
                    target.SetScale(sx, sy);
                }
                break;
            case TransitionActionType.Alpha:
                {
                    float a = item.StartValue.X + (item.EndValue.X - item.StartValue.X) * ratio;
                    target.Alpha = a;
                }
                break;
            case TransitionActionType.Rotation:
                {
                    float r = item.StartValue.X + (item.EndValue.X - item.StartValue.X) * ratio;
                    target.Rotation = r;
                }
                break;
            case TransitionActionType.Visible:
                target.Visible = item.EndValue.B1;
                break;
            case TransitionActionType.Color:
                if (target is IColorGear cg)
                {
                    var startC = item.StartValue.C;
                    var endC = item.EndValue.C;
                    int r = (int)(startC.R + (endC.R - startC.R) * ratio);
                    int g = (int)(startC.G + (endC.G - startC.G) * ratio);
                    int b = (int)(startC.B + (endC.B - startC.B) * ratio);
                    int a = (int)(startC.A + (endC.A - startC.A) * ratio);
                    cg.Color = Color.FromArgb(a, r, g, b);
                }
                break;
            case TransitionActionType.Animation:
                if (target is GMovieClip mc)
                {
                    mc.Frame = (int)item.StartValue.X;
                    mc.Playing = item.StartValue.B1;
                }
                break;
            case TransitionActionType.Pivot:
                {
                    float px = item.StartValue.X + (item.EndValue.X - item.StartValue.X) * ratio;
                    float py = item.StartValue.Y + (item.EndValue.Y - item.StartValue.Y) * ratio;
                    target.SetPivot(px, py, target.PivotAsAnchor);
                }
                break;
            case TransitionActionType.Text:
                target.Text = item.EndValue.S;
                break;
            case TransitionActionType.Icon:
                target.Icon = item.EndValue.S;
                break;
            case TransitionActionType.Shake:
                // Shake implementation
                break;
        }
    }

    private void CheckAllComplete()
    {
        if (_playing && _totalTasks == 0)
        {
            if (_totalTimes < 0)
            {
                // Infinite loop - restart
                PlayInternal(_totalTimes, 0, _startTime, _endTime, _onComplete, _reversed);
            }
            else
            {
                _totalTimes--;
                if (_totalTimes > 0)
                    PlayInternal(_totalTimes, 0, _startTime, _endTime, _onComplete, _reversed);
                else
                {
                    _playing = false;
                    _onComplete?.Invoke();
                }
            }
        }
    }

    public void Stop(bool setToComplete = true, bool processCallback = false)
    {
        if (!_playing) return;

        _playing = false;
        _totalTasks = 0;
        _totalTimes = 0;

        GTween.Kill(this);
        foreach (var item in _items)
            GTween.Kill(item);

        if (processCallback)
            _onComplete?.Invoke();
    }

    internal void UpdateFromRelations(GObject target, float dx, float dy)
    {
        if (_items.Count == 0 || target == null)
        {
            return;
        }

        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (item.Type != TransitionActionType.XY || !ReferenceEquals(item.Target, target))
            {
                continue;
            }

            if (item.TweenConfig != null)
            {
                var appliedStart = false;
                var appliedEnd = false;
                if (!item.StartValue.B3)
                {
                    item.StartValue.X += dx;
                    item.StartValue.Y += dy;
                    appliedStart = true;
                }

                if (!item.EndValue.B3)
                {
                    item.EndValue.X += dx;
                    item.EndValue.Y += dy;
                    appliedEnd = true;
                }

                EmitRelationDiag(target, dx, dy, appliedStart, appliedEnd);
            }
            else if (!item.StartValue.B3)
            {
                item.StartValue.X += dx;
                item.StartValue.Y += dy;
                EmitRelationDiag(target, dx, dy, true, false);
            }
            else
            {
                EmitRelationDiag(target, dx, dy, false, false);
            }
        }
    }

    public void SetValue(string label, params object[] aParams)
    {
        foreach (var item in _items)
        {
            if (item.Label == label || item.Label2 == label)
            {
                if (aParams.Length == 0) continue;
                if (item.TweenConfig != null && item.Label == label)
                    SetItemStartValue(item, aParams);
                else
                    SetItemEndValue(item, aParams);
            }
        }
    }

    private void SetItemStartValue(TransitionItem item, object[] aParams)
    {
        switch (item.Type)
        {
            case TransitionActionType.XY:
            case TransitionActionType.Size:
            case TransitionActionType.Scale:
            case TransitionActionType.Pivot:
            case TransitionActionType.Skew:
                if (aParams.Length >= 2)
                {
                    item.StartValue.X = Convert.ToSingle(aParams[0]);
                    item.StartValue.Y = Convert.ToSingle(aParams[1]);
                }
                break;
            case TransitionActionType.Alpha:
            case TransitionActionType.Rotation:
                if (aParams.Length >= 1)
                    item.StartValue.X = Convert.ToSingle(aParams[0]);
                break;
            case TransitionActionType.Color:
                if (aParams.Length >= 1 && aParams[0] is Color c)
                    item.StartValue.C = c;
                break;
        }
    }

    private void SetItemEndValue(TransitionItem item, object[] aParams)
    {
        switch (item.Type)
        {
            case TransitionActionType.XY:
            case TransitionActionType.Size:
            case TransitionActionType.Scale:
            case TransitionActionType.Pivot:
            case TransitionActionType.Skew:
                if (aParams.Length >= 2)
                {
                    item.EndValue.X = Convert.ToSingle(aParams[0]);
                    item.EndValue.Y = Convert.ToSingle(aParams[1]);
                }
                break;
            case TransitionActionType.Alpha:
            case TransitionActionType.Rotation:
                if (aParams.Length >= 1)
                    item.EndValue.X = Convert.ToSingle(aParams[0]);
                break;
            case TransitionActionType.Color:
                if (aParams.Length >= 1 && aParams[0] is Color c)
                    item.EndValue.C = c;
                break;
            case TransitionActionType.Visible:
                if (aParams.Length >= 1)
                    item.EndValue.B1 = Convert.ToBoolean(aParams[0]);
                break;
            case TransitionActionType.Text:
            case TransitionActionType.Icon:
                if (aParams.Length >= 1)
                    item.EndValue.S = aParams[0]?.ToString();
                break;
        }
    }

    public void Setup(ByteBuffer buffer)
    {
        Name = buffer.ReadS() ?? "";
        _ = buffer.ReadInt();
        _autoPlay = buffer.ReadBool();
        _autoPlayTimes = buffer.ReadInt();
        _autoPlayDelay = buffer.ReadFloat();

        int cnt = buffer.ReadShort();
        _items.Clear();
        for (int i = 0; i < cnt; i++)
        {
            int dataLen = buffer.ReadShort();
            int curPos = buffer.Position;
            var item = new TransitionItem();
            item.TweenConfig = null;

            if (buffer.Seek(curPos, 0))
            {
                item.Type = (TransitionActionType)buffer.ReadByte();
                item.Time = buffer.ReadFloat();
                int targetId = buffer.ReadShort();
                if (targetId < 0)
                {
                    item.Target = Owner;
                }
                else
                {
                    item.Target = Owner?.GetChildAt(targetId);
                }

                item.Label = buffer.ReadS();
                var hasTween = buffer.ReadBool();
                if (hasTween)
                {
                    if (buffer.Seek(curPos, 1))
                    {
                        item.TweenConfig = new TweenConfig
                        {
                            Duration = buffer.ReadFloat(),
                            EaseType = (EaseType)buffer.ReadByte()
                        };
                        int repeat = buffer.ReadInt();
                        item.TweenConfig.Repeat = repeat != 0;
                        item.TweenConfig.Yoyo = buffer.ReadBool();
                        item.Label2 = buffer.ReadS();
                    }

                    if (buffer.Seek(curPos, 2))
                    {
                        DecodeTransitionValue(item.Type, buffer, item.StartValue);
                    }

                    if (buffer.Seek(curPos, 3))
                    {
                        DecodeTransitionValue(item.Type, buffer, item.EndValue);
                    }
                }
                else if (buffer.Seek(curPos, 2))
                {
                    DecodeTransitionValue(item.Type, buffer, item.StartValue);
                }

                EmitDecodeDiag(item, buffer.Version);
            }

            _items.Add(item);
            buffer.Position = curPos + dataLen;
        }

        // Calculate total duration
        _totalDuration = 0;
        foreach (var item in _items)
        {
            float endTime = item.Time;
            if (item.TweenConfig != null)
                endTime += item.TweenConfig.Duration;
            if (endTime > _totalDuration)
                _totalDuration = endTime;
        }
    }

    public void Dispose()
    {
        Stop(false, false);
        _items.Clear();
    }

    private static void DecodeTransitionValue(TransitionActionType type, ByteBuffer buffer, TransitionValue value)
    {
        switch (type)
        {
            case TransitionActionType.XY:
            case TransitionActionType.Size:
            case TransitionActionType.Scale:
            case TransitionActionType.Pivot:
            case TransitionActionType.Skew:
                value.B1 = buffer.ReadBool();
                value.B2 = buffer.ReadBool();
                value.X = buffer.ReadFloat();
                value.Y = buffer.ReadFloat();
                if (buffer.Version >= 2 && type == TransitionActionType.XY)
                {
                    value.B3 = buffer.ReadBool();
                }
                break;

            case TransitionActionType.Alpha:
            case TransitionActionType.Rotation:
                value.X = buffer.ReadFloat();
                break;

            case TransitionActionType.Color:
                value.C = buffer.ReadColor();
                break;

            case TransitionActionType.Animation:
                value.X = buffer.ReadInt();
                value.B1 = buffer.ReadBool();
                break;

            case TransitionActionType.Visible:
                value.B1 = buffer.ReadBool();
                break;

            case TransitionActionType.Sound:
                value.S = buffer.ReadS();
                value.X = buffer.ReadFloat();
                break;

            case TransitionActionType.Transition:
                value.S = buffer.ReadS();
                value.I = buffer.ReadInt();
                break;

            case TransitionActionType.Shake:
                value.X = buffer.ReadFloat();
                value.Y = buffer.ReadFloat();
                break;

            case TransitionActionType.ColorFilter:
                value.X = buffer.ReadFloat();
                value.Y = buffer.ReadFloat();
                value.Z = buffer.ReadFloat();
                value.W = buffer.ReadFloat();
                break;

            case TransitionActionType.Text:
            case TransitionActionType.Icon:
                value.S = buffer.ReadS();
                break;
        }
    }

    private void EmitDecodeDiag(TransitionItem item, int version)
    {
        if (_decodeDiagLogCount >= DecodeDiagLogLimit)
        {
            return;
        }

        _decodeDiagLogCount++;
        Game.Logger.LogInformation(
            "[FGUI][T11][DECODE] idx={Idx} type={Type} hasTween={HasTween} startB3={StartB3} endB3={EndB3} version={Version} label={Label} endLabel={EndLabel}",
            _decodeDiagLogCount,
            item.Type,
            item.TweenConfig != null,
            item.StartValue.B3,
            item.EndValue.B3,
            version,
            item.Label ?? string.Empty,
            item.Label2 ?? string.Empty);
    }

    private void EmitRelationDiag(GObject target, float dx, float dy, bool appliedStart, bool appliedEnd)
    {
        if (_relationDiagLogCount >= RelationDiagLogLimit)
        {
            return;
        }

        _relationDiagLogCount++;
        Game.Logger.LogInformation(
            "[FGUI][T11][REL] idx={Idx} target={Target} dx={Dx} dy={Dy} appliedStart={AppliedStart} appliedEnd={AppliedEnd}",
            _relationDiagLogCount,
            target.Name,
            dx,
            dy,
            appliedStart,
            appliedEnd);
    }
}

class TransitionValue
{
    public float X, Y, Z, W;
    public bool B1, B2, B3, B4;
    public Color C = Color.White;
    public string? S;
    public int I;
}

class TweenConfig
{
    public float Duration;
    public EaseType EaseType = EaseType.QuadOut;
    public bool Repeat;
    public bool Yoyo;
    public object? Path; // For path animation
}

class TransitionItem
{
    public float Time;
    public GObject? Target;
    public TransitionActionType Type;
    public TweenConfig? TweenConfig;
    public string? Label;
    public string? Label2;
    public TransitionValue StartValue = new();
    public TransitionValue EndValue = new();
}
#endif

