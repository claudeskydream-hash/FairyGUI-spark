#if CLIENT
namespace FairyGUI;

public enum KeyCode
{
    None = 0,
    UpArrow = 273,
    DownArrow = 274,
    RightArrow = 275,
    LeftArrow = 276
}

public class InputEvent
{
    public KeyCode keyCode { get; set; }
    public bool shift { get; set; }
    public bool ctrl { get; set; }
}

public class EventContext
{
    public object? Sender { get; set; }
    public string Type { get; set; } = "";
    public object? Data { get; set; }
    public bool DefaultPrevented { get; private set; }
    public InputEvent? inputEvent { get; set; }

    public void PreventDefault() => DefaultPrevented = true;
    public void StopPropagation() { }
}

public delegate void EventCallback(EventContext context);

public class EventListener
{
    private readonly List<EventCallback> _callbacks = new();
    private readonly Action? _onFirstCallbackAdded;

    public EventListener(Action? onFirstCallbackAdded = null)
    {
        _onFirstCallbackAdded = onFirstCallbackAdded;
    }

    public void Add(EventCallback callback)
    {
        var wasEmpty = _callbacks.Count == 0;
        if (!_callbacks.Contains(callback))
        {
            _callbacks.Add(callback);
        }

        if (wasEmpty && _callbacks.Count > 0)
        {
            _onFirstCallbackAdded?.Invoke();
        }
    }

    public void Remove(EventCallback callback) => _callbacks.Remove(callback);
    public void Clear() => _callbacks.Clear();

    public void Call(EventContext context)
    {
        foreach (var callback in _callbacks.ToArray())
            callback(context);
    }

    public bool IsEmpty => _callbacks.Count == 0;
}

public class EventDispatcher
{
    private readonly Dictionary<string, EventListener> _listeners = new();

    public void AddEventListener(string type, EventCallback callback)
    {
        if (!_listeners.TryGetValue(type, out var listener))
        {
            listener = CreateListener(type);
            _listeners[type] = listener;
        }
        listener.Add(callback);
    }

    public void RemoveEventListener(string type, EventCallback callback)
    {
        if (_listeners.TryGetValue(type, out var listener))
            listener.Remove(callback);
    }

    public void RemoveEventListeners(string type) => _listeners.Remove(type);
    public void RemoveAllEventListeners() => _listeners.Clear();

    public bool HasEventListener(string type) =>
        _listeners.TryGetValue(type, out var listener) && !listener.IsEmpty;

    public bool DispatchEvent(string type, object? data = null)
    {
        if (!_listeners.TryGetValue(type, out var listener))
            return false;
        var context = new EventContext { Sender = this, Type = type, Data = data };
        listener.Call(context);
        return !context.DefaultPrevented;
    }
    
    /// <summary>
    /// Dispatch event with a provided EventContext, allowing caller to check DefaultPrevented
    /// </summary>
    public bool DispatchEventWithContext(string type, EventContext context, object? data = null)
    {
        if (!_listeners.TryGetValue(type, out var listener))
            return false;
        context.Data = data;
        listener.Call(context);
        return !context.DefaultPrevented;
    }

    public EventListener OnClick => GetOrCreateListener("onClick");
    public EventListener OnRightClick => GetOrCreateListener("onRightClick");
    public EventListener OnTouchBegin => GetOrCreateListener("onTouchBegin");
    public EventListener OnTouchMove => GetOrCreateListener("onTouchMove");
    public EventListener OnTouchEnd => GetOrCreateListener("onTouchEnd");
    public EventListener OnRollOver => GetOrCreateListener("onRollOver");
    public EventListener OnRollOut => GetOrCreateListener("onRollOut");
    public EventListener OnPositionChanged => GetOrCreateListener("onPositionChanged");
    public EventListener OnSizeChanged => GetOrCreateListener("onSizeChanged");
    public EventListener OnDragStart => GetOrCreateListener("onDragStart");
    public EventListener OnDragMove => GetOrCreateListener("onDragMove");
    public EventListener OnDragEnd => GetOrCreateListener("onDragEnd");
    public EventListener OnDrop => GetOrCreateListener("onDrop");
    public EventListener OnKeyDown => GetOrCreateListener("onKeyDown");

    private EventListener GetOrCreateListener(string type)
    {
        if (!_listeners.TryGetValue(type, out var listener))
        {
            listener = CreateListener(type);
            _listeners[type] = listener;
        }
        return listener;
    }

    private EventListener CreateListener(string type)
    {
        return new EventListener(() => OnFirstListenerAdded(type));
    }

    private void OnFirstListenerAdded(string type)
    {
        if (!IsNativeTouchBindingEvent(type) || this is not GObject obj)
        {
            return;
        }

        Render.SCERenderContext.Instance.EnsureTouchBinding(obj);
    }

    private static bool IsNativeTouchBindingEvent(string type)
    {
        return type == "onTouchBegin" ||
               type == "onTouchMove" ||
               type == "onTouchEnd" ||
               type == "onClick" ||
               type == "onRollOver" ||
               type == "onRollOut";
    }
}
#endif

