#if CLIENT
using System.Drawing;
using System.Runtime.CompilerServices;
using FairyGUI;

namespace FairyGUI;

/// <summary>
/// Drag and Drop Manager - handles drag operations across the UI
/// </summary>
public static class DragDropManager
{
    private static GObject? _dragAgent;
    private static object? _sourceData;
    private static GObject? _source;
    private static bool _dragging;
    private static PointF _touchStart;
    private static PointF _agentOffset;
    private static PointF _lastTouchPoint;
    private static PointF _lastLogicalTouchPoint;

    public static GObject? DragAgent => _dragAgent;
    public static object? SourceData => _sourceData;
    public static GObject? Source => _source;
    public static bool IsDragging => _dragging;
    public static PointF LastTouchPoint => _lastTouchPoint;
    public static PointF LastLogicalTouchPoint => _lastLogicalTouchPoint;

    /// <summary>
    /// Start dragging with a visual agent
    /// </summary>
    public static void StartDrag(GObject source, object? sourceData, string? icon, GObject? customAgent = null, PointF? touchPoint = null)
    {
        // 防止重复调用导致的递归
        if (_dragging)
        {
            // 如果已经在拖拽同一个对象，直接返回
            if (_source == source)
                return;
            Cancel();
        }

        _source = source;
        _sourceData = sourceData;
        _dragging = true;  // 先设置为true，防止递归
        _touchStart = touchPoint ?? new PointF(0, 0);
        _lastTouchPoint = _touchStart;

        if (customAgent != null)
        {
            _dragAgent = customAgent;
        }
        else if (!string.IsNullOrEmpty(icon))
        {
            // Create a loader as drag agent
            var loader = new GLoader { Url = icon };
            loader.SetSize(source.Width, source.Height);
            _dragAgent = loader;
        }
        else
        {
            // Use a placeholder
            var graph = new GGraph();
            graph.SetSize(source.Width, source.Height);
            graph.DrawRect(source.Width, source.Height, 1, Color.Gray, Color.FromArgb(100, 128, 128, 128));
            _dragAgent = graph;
        }

        if (_dragAgent != null)
        {
            _dragAgent.Touchable = false;
            _agentOffset = new PointF(_dragAgent.Width / 2, _dragAgent.Height / 2);
            UpdateAgentPosition(_touchStart);
            UIRuntime.AddToRoot(_dragAgent);
        }

        // 注意：不要在这里触发onDragStart事件，因为调用者可能是从onDragStart事件中调用的
        // 如果需要通知，应该由调用者自己处理
        // _source.DispatchEvent("onDragStart", sourceData);
    }

    /// <summary>
    /// Update the drag agent position during drag
    /// </summary>
    public static void OnDragMove(PointF touchPoint)
    {
        if (!_dragging || _dragAgent == null) return;
        UpdateAgentPosition(touchPoint);
    }

    /// <summary>
    /// End the drag operation
    /// </summary>
    public static void OnDragEnd(PointF touchPoint, GObject? target)
    {
        if (!_dragging) return;

        _source?.DispatchEvent("onDragEnd", _sourceData);

        if (target != null && target != _source)
        {
            target.DispatchEvent("onDrop", new DropEventData
            {
                Source = _source,
                SourceData = _sourceData
            });
        }

        Cleanup();
    }

    /// <summary>
    /// Cancel the current drag operation
    /// </summary>
    public static void Cancel()
    {
        if (!_dragging) return;
        Cleanup();
    }

    private static void UpdateAgentPosition(PointF touchPoint)
    {
        _lastTouchPoint = touchPoint;
        if (_dragAgent != null)
        {
            // 触摸坐标是屏幕坐标，需要转换为逻辑坐标
            float scaleFactor = UIRuntime.ContentScaleFactor;
            float logicalX = touchPoint.X / scaleFactor - _agentOffset.X;
            float logicalY = touchPoint.Y / scaleFactor - _agentOffset.Y;
            _lastLogicalTouchPoint = new PointF(touchPoint.X / scaleFactor, touchPoint.Y / scaleFactor);
            _dragAgent.SetXY(logicalX, logicalY);
        }
        else
        {
            float scaleFactor = UIRuntime.ContentScaleFactor;
            _lastLogicalTouchPoint = new PointF(touchPoint.X / scaleFactor, touchPoint.Y / scaleFactor);
        }
    }

    private static void Cleanup()
    {
        if (_dragAgent != null)
        {
            UIRuntime.RemoveFromRoot(_dragAgent, dispose: false);
            if (_dragAgent is GLoader or GGraph)
                _dragAgent.Dispose();
            _dragAgent = null;
        }

        _source = null;
        _sourceData = null;
        _dragging = false;
    }
}

public class DropEventData
{
    public GObject? Source { get; set; }
    public object? SourceData { get; set; }
}

/// <summary>
/// Extension methods for making objects draggable
/// </summary>
public static class DraggableExtensions
{
    private static readonly ConditionalWeakTable<GObject, DragInfo> _dragInfos = new();

    public static void InitDrag(this GObject obj)
    {
        if (_dragInfos.TryGetValue(obj, out _)) return;

        var info = new DragInfo { Owner = obj };
        _dragInfos.Add(obj, info);

        obj.OnTouchBegin.Add(info.OnTouchBegin);
        obj.OnTouchMove.Add(info.OnTouchMove);
        obj.OnTouchEnd.Add(info.OnTouchEnd);
    }

    public static void StopDrag(this GObject obj)
    {
        if (!_dragInfos.TryGetValue(obj, out var info)) return;

        obj.OnTouchBegin.Remove(info.OnTouchBegin);
        obj.OnTouchMove.Remove(info.OnTouchMove);
        obj.OnTouchEnd.Remove(info.OnTouchEnd);
        _dragInfos.Remove(obj);
    }

    private class DragInfo
    {
        public GObject? Owner;
        private bool _dragging;
        private PointF _startPos;  // 屏幕坐标
        private PointF _startObjectPos;  // 逻辑坐标

        public void OnTouchBegin(EventContext ctx)
        {
            if (Owner == null || !Owner.Draggable) return;

            _dragging = true;
            var data = ctx.Data as TouchEventData;
            _startPos = data?.Position ?? new PointF(0, 0);  // 屏幕坐标
            _startObjectPos = new PointF(Owner.X, Owner.Y);  // 逻辑坐标
        }

        public void OnTouchMove(EventContext ctx)
        {
            if (!_dragging || Owner == null) return;

            var data = ctx.Data as TouchEventData;
            if (data == null) return;

            // 触摸位置是屏幕坐标，需要转换为逻辑坐标的位移
            float scaleFactor = UIRuntime.ContentScaleFactor;
            float dx = (data.Position.X - _startPos.X) / scaleFactor;
            float dy = (data.Position.Y - _startPos.Y) / scaleFactor;

            Owner.SetXY(_startObjectPos.X + dx, _startObjectPos.Y + dy);
            Owner.DispatchEvent("onDragMove", null);
        }

        public void OnTouchEnd(EventContext ctx)
        {
            if (!_dragging || Owner == null) return;
            _dragging = false;
            Owner.DispatchEvent("onDragEnd", null);
        }
    }
}

public class TouchEventData
{
    public PointF Position { get; set; }
    public int TouchId { get; set; }
}
#endif

