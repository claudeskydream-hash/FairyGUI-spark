#if CLIENT
using System.Drawing;
using FairyGUI;

namespace FairyGUI;

public static class EaseManager
{
    private const float PI = 3.14159265359f;
    private const float PI2 = PI / 2;
    private const float BOUNCE_K = 1f / 2.75f;

    public static float Evaluate(EaseType easeType, float time, float duration, float overshootOrAmplitude = 1.70158f, float period = 0)
    {
        if (duration <= 0) return 1;
        float t = time / duration;
        if (t >= 1) return 1;
        if (t <= 0) return 0;

        return easeType switch
        {
            EaseType.Linear => t,
            EaseType.SineIn => 1 - (float)Math.Cos(t * PI2),
            EaseType.SineOut => (float)Math.Sin(t * PI2),
            EaseType.SineInOut => -0.5f * ((float)Math.Cos(PI * t) - 1),
            EaseType.QuadIn => t * t,
            EaseType.QuadOut => -t * (t - 2),
            EaseType.QuadInOut => (t *= 2) < 1 ? 0.5f * t * t : -0.5f * (--t * (t - 2) - 1),
            EaseType.CubicIn => t * t * t,
            EaseType.CubicOut => (t -= 1) * t * t + 1,
            EaseType.CubicInOut => (t *= 2) < 1 ? 0.5f * t * t * t : 0.5f * ((t -= 2) * t * t + 2),
            EaseType.QuartIn => t * t * t * t,
            EaseType.QuartOut => -((t -= 1) * t * t * t - 1),
            EaseType.QuartInOut => (t *= 2) < 1 ? 0.5f * t * t * t * t : -0.5f * ((t -= 2) * t * t * t - 2),
            EaseType.QuintIn => t * t * t * t * t,
            EaseType.QuintOut => (t -= 1) * t * t * t * t + 1,
            EaseType.QuintInOut => (t *= 2) < 1 ? 0.5f * t * t * t * t * t : 0.5f * ((t -= 2) * t * t * t * t + 2),
            EaseType.ExpoIn => t == 0 ? 0 : (float)Math.Pow(2, 10 * (t - 1)),
            EaseType.ExpoOut => t == 1 ? 1 : -(float)Math.Pow(2, -10 * t) + 1,
            EaseType.ExpoInOut => t == 0 ? 0 : t == 1 ? 1 : (t *= 2) < 1 ? 0.5f * (float)Math.Pow(2, 10 * (t - 1)) : 0.5f * (-(float)Math.Pow(2, -10 * --t) + 2),
            EaseType.CircIn => -((float)Math.Sqrt(1 - t * t) - 1),
            EaseType.CircOut => (float)Math.Sqrt(1 - (t -= 1) * t),
            EaseType.CircInOut => (t *= 2) < 1 ? -0.5f * ((float)Math.Sqrt(1 - t * t) - 1) : 0.5f * ((float)Math.Sqrt(1 - (t -= 2) * t) + 1),
            EaseType.ElasticIn => EaseElasticIn(t, overshootOrAmplitude, period),
            EaseType.ElasticOut => EaseElasticOut(t, overshootOrAmplitude, period),
            EaseType.ElasticInOut => EaseElasticInOut(t, overshootOrAmplitude, period),
            EaseType.BackIn => t * t * ((overshootOrAmplitude + 1) * t - overshootOrAmplitude),
            EaseType.BackOut => (t -= 1) * t * ((overshootOrAmplitude + 1) * t + overshootOrAmplitude) + 1,
            EaseType.BackInOut => (t *= 2) < 1 ? 0.5f * (t * t * (((overshootOrAmplitude *= 1.525f) + 1) * t - overshootOrAmplitude)) : 0.5f * ((t -= 2) * t * (((overshootOrAmplitude *= 1.525f) + 1) * t + overshootOrAmplitude) + 2),
            EaseType.BounceIn => 1 - EaseBounceOut(1 - t),
            EaseType.BounceOut => EaseBounceOut(t),
            EaseType.BounceInOut => t < 0.5f ? (1 - EaseBounceOut(1 - t * 2)) * 0.5f : EaseBounceOut(t * 2 - 1) * 0.5f + 0.5f,
            _ => t
        };
    }

    private static float EaseElasticIn(float t, float amplitude, float period)
    {
        if (period == 0) period = 0.3f;
        float s;
        if (amplitude < 1) { amplitude = 1; s = period / 4; }
        else s = period / (2 * PI) * (float)Math.Asin(1 / amplitude);
        return -(amplitude * (float)Math.Pow(2, 10 * (t -= 1)) * (float)Math.Sin((t - s) * (2 * PI) / period));
    }

    private static float EaseElasticOut(float t, float amplitude, float period)
    {
        if (period == 0) period = 0.3f;
        float s;
        if (amplitude < 1) { amplitude = 1; s = period / 4; }
        else s = period / (2 * PI) * (float)Math.Asin(1 / amplitude);
        return amplitude * (float)Math.Pow(2, -10 * t) * (float)Math.Sin((t - s) * (2 * PI) / period) + 1;
    }

    private static float EaseElasticInOut(float t, float amplitude, float period)
    {
        if (period == 0) period = 0.3f * 1.5f;
        float s;
        if (amplitude < 1) { amplitude = 1; s = period / 4; }
        else s = period / (2 * PI) * (float)Math.Asin(1 / amplitude);
        if ((t *= 2) < 1)
            return -0.5f * (amplitude * (float)Math.Pow(2, 10 * (t -= 1)) * (float)Math.Sin((t - s) * (2 * PI) / period));
        return amplitude * (float)Math.Pow(2, -10 * (t -= 1)) * (float)Math.Sin((t - s) * (2 * PI) / period) * 0.5f + 1;
    }

    private static float EaseBounceOut(float t)
    {
        if (t < BOUNCE_K) return 7.5625f * t * t;
        if (t < 2 * BOUNCE_K) return 7.5625f * (t -= 1.5f * BOUNCE_K) * t + 0.75f;
        if (t < 2.5f * BOUNCE_K) return 7.5625f * (t -= 2.25f * BOUNCE_K) * t + 0.9375f;
        return 7.5625f * (t -= 2.625f * BOUNCE_K) * t + 0.984375f;
    }
}

public class TweenValue
{
    public float X, Y, Z, W;
    public double D;

    public PointF GetVec2() => new(X, Y);
    public void SetVec2(float x, float y) { X = x; Y = y; }
    public Color GetColor() => Color.FromArgb((int)(W * 255), (int)(X * 255), (int)(Y * 255), (int)(Z * 255));
    public void SetColor(Color c) { X = c.R / 255f; Y = c.G / 255f; Z = c.B / 255f; W = c.A / 255f; }
}

public class GTweener
{
    private TweenValue _startValue = new();
    private TweenValue _endValue = new();
    private TweenValue _value = new();
    private TweenValue _deltaValue = new();

    private float _duration;
    private float _delay;
    private float _elapsedTime;
    private float _normalizedTime;
    private EaseType _easeType = EaseType.QuadOut;
    private int _repeat;
    private bool _yoyo;
    private float _timeScale = 1;
    private bool _snapping;
    private object? _target;
    private Action<GTweener>? _onUpdate;
    private Action<GTweener>? _onComplete;
    private Action<GTweener>? _onStart;
    private bool _started;
    private int _valueSize;
    private bool _killed;
    private bool _paused;

    public TweenValue StartValue => _startValue;
    public TweenValue EndValue => _endValue;
    public TweenValue Value => _value;
    public TweenValue DeltaValue => _deltaValue;
    public float Duration => _duration;
    public float Delay => _delay;
    public float NormalizedTime => _normalizedTime;
    public object? Target => _target;
    public bool Killed => _killed;
    public bool Paused { get => _paused; set => _paused = value; }

    public GTweener SetDelay(float value) { _delay = value; return this; }
    public GTweener SetDuration(float value) { _duration = value; return this; }
    public GTweener SetEase(EaseType value) { _easeType = value; return this; }
    public GTweener SetRepeat(int value, bool yoyo = false) { _repeat = value; _yoyo = yoyo; return this; }
    public GTweener SetTimeScale(float value) { _timeScale = value; return this; }
    public GTweener SetSnapping(bool value) { _snapping = value; return this; }
    public GTweener SetTarget(object value) { _target = value; return this; }
    public GTweener OnUpdate(Action<GTweener> callback) { _onUpdate = callback; return this; }
    public GTweener OnComplete(Action<GTweener> callback) { _onComplete = callback; return this; }
    public GTweener OnStart(Action<GTweener> callback) { _onStart = callback; return this; }

    internal GTweener() { }

    internal void Init()
    {
        _delay = 0;
        _duration = 0;
        _elapsedTime = 0;
        _normalizedTime = 0;
        _easeType = EaseType.QuadOut;
        _repeat = 0;
        _yoyo = false;
        _timeScale = 1;
        _snapping = false;
        _target = null;
        _onUpdate = null;
        _onComplete = null;
        _onStart = null;
        _started = false;
        _killed = false;
        _paused = false;
    }

    internal void SetValueSize(int size) => _valueSize = size;

    public void Kill(bool complete = false)
    {
        if (_killed) return;
        _killed = true;
        if (complete)
        {
            _normalizedTime = 1;
            CallUpdateCallback();
        }
        _onComplete?.Invoke(this);
    }

    internal bool Update(float dt)
    {
        if (_killed || _paused) return _killed;

        dt *= _timeScale;
        _elapsedTime += dt;

        if (_delay > 0)
        {
            if (_elapsedTime < _delay) return false;
            _elapsedTime -= _delay;
            _delay = 0;
        }

        if (!_started)
        {
            _started = true;
            _onStart?.Invoke(this);
        }

        float tt = _elapsedTime;
        bool ended = false;
        if (_repeat != 0)
        {
            int round = (int)(tt / _duration);
            tt -= _duration * round;
            if (_yoyo) tt = round % 2 == 1 ? _duration - tt : tt;
            if (_repeat > 0 && round >= _repeat) { ended = true; tt = _duration; }
        }
        else if (tt >= _duration)
        {
            ended = true;
            tt = _duration;
        }

        _normalizedTime = EaseManager.Evaluate(_easeType, tt, _duration);
        UpdateValue();
        CallUpdateCallback();

        if (ended)
        {
            _onComplete?.Invoke(this);
            _killed = true;
        }
        return _killed;
    }

    private void UpdateValue()
    {
        for (int i = 0; i < _valueSize; i++)
        {
            float start = i switch { 0 => _startValue.X, 1 => _startValue.Y, 2 => _startValue.Z, 3 => _startValue.W, _ => 0 };
            float end = i switch { 0 => _endValue.X, 1 => _endValue.Y, 2 => _endValue.Z, 3 => _endValue.W, _ => 0 };
            float delta = end - start;
            float v = start + delta * _normalizedTime;
            if (_snapping) v = (float)Math.Round(v);
            switch (i)
            {
                case 0: _deltaValue.X = v - _value.X; _value.X = v; break;
                case 1: _deltaValue.Y = v - _value.Y; _value.Y = v; break;
                case 2: _deltaValue.Z = v - _value.Z; _value.Z = v; break;
                case 3: _deltaValue.W = v - _value.W; _value.W = v; break;
            }
        }
    }

    private void CallUpdateCallback() => _onUpdate?.Invoke(this);
}

public static class GTween
{
    private static List<GTweener> _activeTweens = new();
    private static Stack<GTweener> _tweenPool = new();
    private static bool _inUpdate;
    private static float _time;
    private static float _lastTime;

    public static GTweener To(float startValue, float endValue, float duration)
    {
        var tweener = GetTweener();
        tweener.StartValue.X = startValue;
        tweener.EndValue.X = endValue;
        tweener.SetValueSize(1);
        tweener.SetDuration(duration);
        return tweener;
    }

    public static GTweener To(PointF startValue, PointF endValue, float duration)
    {
        var tweener = GetTweener();
        tweener.StartValue.SetVec2(startValue.X, startValue.Y);
        tweener.EndValue.SetVec2(endValue.X, endValue.Y);
        tweener.SetValueSize(2);
        tweener.SetDuration(duration);
        return tweener;
    }

    public static GTweener To(Color startValue, Color endValue, float duration)
    {
        var tweener = GetTweener();
        tweener.StartValue.SetColor(startValue);
        tweener.EndValue.SetColor(endValue);
        tweener.SetValueSize(4);
        tweener.SetDuration(duration);
        return tweener;
    }

    public static GTweener ToDouble(double startValue, double endValue, float duration)
    {
        var tweener = GetTweener();
        tweener.StartValue.D = startValue;
        tweener.EndValue.D = endValue;
        tweener.StartValue.X = (float)startValue;
        tweener.EndValue.X = (float)endValue;
        tweener.SetValueSize(1);
        tweener.SetDuration(duration);
        return tweener;
    }

    public static void DelayedCall(float delay, Action callback)
    {
        var tweener = GetTweener();
        tweener.SetDuration(0).SetDelay(delay).OnComplete(_ => callback());
    }

    public static void Shake(PointF startValue, float amplitude, float duration, Action<PointF>? onUpdate, Action? onComplete)
    {
        var random = new Random();
        To(startValue, startValue, duration)
            .OnUpdate(t =>
            {
                float r = amplitude * (1 - t.NormalizedTime);
                float x = startValue.X + (float)(random.NextDouble() * 2 - 1) * r;
                float y = startValue.Y + (float)(random.NextDouble() * 2 - 1) * r;
                onUpdate?.Invoke(new PointF(x, y));
            })
            .OnComplete(_ => onComplete?.Invoke());
    }

    public static bool IsTweening(object target)
    {
        foreach (var t in _activeTweens)
            if (!t.Killed && t.Target == target) return true;
        return false;
    }

    public static void Kill(object target, bool complete = false)
    {
        foreach (var t in _activeTweens)
            if (t.Target == target) t.Kill(complete);
    }

    public static void KillAll() => _activeTweens.ForEach(t => t.Kill());

    public static void Update(float deltaTime)
    {
        _lastTime = _time;
        _time += deltaTime;
        _inUpdate = true;

        for (int i = _activeTweens.Count - 1; i >= 0; i--)
        {
            var tweener = _activeTweens[i];
            if (tweener.Update(deltaTime))
            {
                _activeTweens.RemoveAt(i);
                _tweenPool.Push(tweener);
            }
        }

        _inUpdate = false;
    }

    private static GTweener GetTweener()
    {
        GTweener tweener;
        if (_tweenPool.Count > 0)
        {
            tweener = _tweenPool.Pop();
            tweener.Init();
        }
        else
            tweener = new GTweener();
        _activeTweens.Add(tweener);
        return tweener;
    }
}
#endif

