#if CLIENT
using System.Drawing;
using FairyGUI;
using FairyGUI.Utils;

namespace FairyGUI;

public class Relations
{
    private GObject _owner;
    private List<RelationItem> _items = new();
    public GObject? Handling;

    public Relations(GObject owner) => _owner = owner;

    public void Add(GObject target, RelationType relationType, bool usePercent = false)
    {
        foreach (var item in _items)
        {
            if (item.Target == target)
            {
                item.Add(relationType, usePercent);
                return;
            }
        }
        var newItem = new RelationItem(_owner);
        newItem.Target = target;
        newItem.Add(relationType, usePercent);
        _items.Add(newItem);
    }

    public void Remove(GObject target, RelationType relationType)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if (item.Target == target)
            {
                item.Remove(relationType);
                if (item.IsEmpty)
                {
                    item.Dispose();
                    _items.RemoveAt(i);
                }
            }
        }
    }

    public bool Contains(GObject target) => _items.Any(i => i.Target == target);

    public void ClearFor(GObject target)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].Target == target)
            {
                _items[i].Dispose();
                _items.RemoveAt(i);
            }
        }
    }

    public void ClearAll()
    {
        foreach (var item in _items)
            item.Dispose();
        _items.Clear();
    }

    public void CopyFrom(Relations source)
    {
        ClearAll();
        foreach (var ri in source._items)
        {
            var item = new RelationItem(_owner);
            item.CopyFrom(ri);
            _items.Add(item);
        }
    }

    public void Dispose()
    {
        ClearAll();
        Handling = null;
    }

    public void OnOwnerSizeChanged(float dWidth, float dHeight, bool applyPivot)
    {
        foreach (var item in _items)
            item.ApplyOnSelfSizeChanged(dWidth, dHeight, applyPivot);
    }

    public bool IsEmpty => _items.Count == 0;

    public void Setup(ByteBuffer buffer, bool parentToChild)
    {
        int cnt = buffer.ReadByte();
        for (int i = 0; i < cnt; i++)
        {
            int targetIndex = buffer.ReadShort();
            GObject? target;
            if (targetIndex == -1)
                target = _owner.Parent;
            else if (parentToChild)
                target = (_owner as GComponent)?.GetChildAt(targetIndex);
            else
                target = _owner.Parent?.GetChildAt(targetIndex);

            if (target == null) continue;

            var newItem = new RelationItem(_owner);
            newItem.Target = target;
            _items.Add(newItem);

            int cnt2 = buffer.ReadByte();
            for (int j = 0; j < cnt2; j++)
            {
                var rt = (RelationType)buffer.ReadByte();
                bool usePercent = buffer.ReadBool();
                newItem.InternalAdd(rt, usePercent);
            }
        }
    }
}

class RelationDef
{
    public bool Percent;
    public RelationType Type;
    public int Axis;

    public void CopyFrom(RelationDef source)
    {
        Percent = source.Percent;
        Type = source.Type;
        Axis = source.Axis;
    }
}

public class RelationItem
{
    private GObject _owner;
    private GObject? _target;
    private List<RelationDef> _defs = new();
    private (float x, float y, float w, float h) _targetData;

    public RelationItem(GObject owner) => _owner = owner;

    public GObject? Target
    {
        get => _target;
        set
        {
            if (_target != value)
            {
                if (_target != null) ReleaseRefTarget(_target);
                _target = value;
                if (_target != null) AddRefTarget(_target);
            }
        }
    }

    public void Add(RelationType relationType, bool usePercent)
    {
        if (relationType == RelationType.Size)
        {
            Add(RelationType.Width, usePercent);
            Add(RelationType.Height, usePercent);
            return;
        }
        if (_defs.Any(d => d.Type == relationType)) return;
        InternalAdd(relationType, usePercent);
    }

    public void InternalAdd(RelationType relationType, bool usePercent)
    {
        if (relationType == RelationType.Size)
        {
            InternalAdd(RelationType.Width, usePercent);
            InternalAdd(RelationType.Height, usePercent);
            return;
        }
        var info = new RelationDef
        {
            Percent = usePercent,
            Type = relationType,
            Axis = (relationType <= RelationType.Right_Right || relationType == RelationType.Width || 
                   (relationType >= RelationType.LeftExt_Left && relationType <= RelationType.RightExt_Right)) ? 0 : 1
        };
        _defs.Add(info);
    }

    public void Remove(RelationType relationType)
    {
        if (relationType == RelationType.Size)
        {
            Remove(RelationType.Width);
            Remove(RelationType.Height);
            return;
        }
        _defs.RemoveAll(d => d.Type == relationType);
    }

    public void CopyFrom(RelationItem source)
    {
        Target = source.Target;
        _defs.Clear();
        foreach (var info in source._defs)
        {
            var info2 = new RelationDef();
            info2.CopyFrom(info);
            _defs.Add(info2);
        }
    }

    public void Dispose()
    {
        if (_target != null)
        {
            ReleaseRefTarget(_target);
            _target = null;
        }
    }

    public bool IsEmpty => _defs.Count == 0;

    public void ApplyOnSelfSizeChanged(float dWidth, float dHeight, bool applyPivot)
    {
        if (_defs.Count == 0) return;

        float ox = _owner.X, oy = _owner.Y;

        foreach (var info in _defs)
        {
            switch (info.Type)
            {
                case RelationType.Center_Center:
                    _owner.X -= (0.5f - (applyPivot ? _owner.PivotX : 0)) * dWidth;
                    break;
                case RelationType.Right_Center:
                case RelationType.Right_Left:
                case RelationType.Right_Right:
                    _owner.X -= (1 - (applyPivot ? _owner.PivotX : 0)) * dWidth;
                    break;
                case RelationType.Middle_Middle:
                    _owner.Y -= (0.5f - (applyPivot ? _owner.PivotY : 0)) * dHeight;
                    break;
                case RelationType.Bottom_Middle:
                case RelationType.Bottom_Top:
                case RelationType.Bottom_Bottom:
                    _owner.Y -= (1 - (applyPivot ? _owner.PivotY : 0)) * dHeight;
                    break;
            }
        }

        if (Math.Abs(ox - _owner.X) > 0.001f || Math.Abs(oy - _owner.Y) > 0.001f)
        {
            var dx = _owner.X - ox;
            var dy = _owner.Y - oy;
            _owner.UpdateGearFromRelations(1, dx, dy);
            if (_owner.Parent is GComponent parent)
            {
                parent.UpdateTransitionsFromRelations(_owner, dx, dy);
            }
        }
    }

    void ApplyOnXYChanged(RelationDef info, float dx, float dy)
    {
        switch (info.Type)
        {
            case RelationType.Left_Left:
            case RelationType.Left_Center:
            case RelationType.Left_Right:
            case RelationType.Center_Center:
            case RelationType.Right_Left:
            case RelationType.Right_Center:
            case RelationType.Right_Right:
                _owner.X += dx;
                break;
            case RelationType.Top_Top:
            case RelationType.Top_Middle:
            case RelationType.Top_Bottom:
            case RelationType.Middle_Middle:
            case RelationType.Bottom_Top:
            case RelationType.Bottom_Middle:
            case RelationType.Bottom_Bottom:
                _owner.Y += dy;
                break;
            case RelationType.LeftExt_Left:
            case RelationType.LeftExt_Right:
                if (_owner != _target?.Parent)
                {
                    float tmp = _owner.X;
                    _owner.Width = _owner.Width - dx;
                    _owner.X = tmp + dx;
                }
                else
                    _owner.Width = _owner.Width - dx;
                break;
            case RelationType.RightExt_Left:
            case RelationType.RightExt_Right:
                _owner.Width = _owner.Width + dx;
                break;
            case RelationType.TopExt_Top:
            case RelationType.TopExt_Bottom:
                if (_owner != _target?.Parent)
                {
                    float tmp = _owner.Y;
                    _owner.Height = _owner.Height - dy;
                    _owner.Y = tmp + dy;
                }
                else
                    _owner.Height = _owner.Height - dy;
                break;
            case RelationType.BottomExt_Top:
            case RelationType.BottomExt_Bottom:
                _owner.Height = _owner.Height + dy;
                break;
        }
    }

    void ApplyOnSizeChanged(RelationDef info)
    {
        if (_target == null) return;
        
        float pos = 0, pivot = 0, delta = 0;
        if (info.Axis == 0)
        {
            if (_target != _owner.Parent)
            {
                pos = _target.X;
                if (_target.PivotAsAnchor) pivot = _target.PivotX;
            }
            if (info.Percent)
            {
                if (_targetData.w != 0)
                    delta = _target.Width / _targetData.w;
            }
            else
                delta = _target.Width - _targetData.w;
        }
        else
        {
            if (_target != _owner.Parent)
            {
                pos = _target.Y;
                if (_target.PivotAsAnchor) pivot = _target.PivotY;
            }
            if (info.Percent)
            {
                if (_targetData.h != 0)
                    delta = _target.Height / _targetData.h;
            }
            else
                delta = _target.Height - _targetData.h;
        }

        switch (info.Type)
        {
            case RelationType.Left_Left:
            case RelationType.Left_Center:
            case RelationType.Left_Right:
                if (info.Percent)
                    _owner.X = pos + (_owner.X - pos) * delta;
                else if (info.Type == RelationType.Left_Center)
                    _owner.X += delta * (0.5f - pivot);
                else if (info.Type == RelationType.Left_Right)
                    _owner.X += delta * (1 - pivot);
                break;
            case RelationType.Center_Center:
                if (info.Percent)
                    _owner.X = pos + (_owner.X + _owner.Width * 0.5f - pos) * delta - _owner.Width * 0.5f;
                else
                    _owner.X += delta * (0.5f - pivot);
                break;
            case RelationType.Right_Left:
            case RelationType.Right_Center:
            case RelationType.Right_Right:
                if (info.Percent)
                    _owner.X = pos + (_owner.X + _owner.Width - pos) * delta - _owner.Width;
                else if (info.Type == RelationType.Right_Center)
                    _owner.X += delta * (0.5f - pivot);
                else if (info.Type == RelationType.Right_Right)
                    _owner.X += delta * (1 - pivot);
                break;
            case RelationType.Top_Top:
            case RelationType.Top_Middle:
            case RelationType.Top_Bottom:
                if (info.Percent)
                    _owner.Y = pos + (_owner.Y - pos) * delta;
                else if (info.Type == RelationType.Top_Middle)
                    _owner.Y += delta * (0.5f - pivot);
                else if (info.Type == RelationType.Top_Bottom)
                    _owner.Y += delta * (1 - pivot);
                break;
            case RelationType.Middle_Middle:
                if (info.Percent)
                    _owner.Y = pos + (_owner.Y + _owner.Height * 0.5f - pos) * delta - _owner.Height * 0.5f;
                else
                    _owner.Y += delta * (0.5f - pivot);
                break;
            case RelationType.Bottom_Top:
            case RelationType.Bottom_Middle:
            case RelationType.Bottom_Bottom:
                if (info.Percent)
                    _owner.Y = pos + (_owner.Y + _owner.Height - pos) * delta - _owner.Height;
                else if (info.Type == RelationType.Bottom_Middle)
                    _owner.Y += delta * (0.5f - pivot);
                else if (info.Type == RelationType.Bottom_Bottom)
                    _owner.Y += delta * (1 - pivot);
                break;
            case RelationType.Width:
                if (_target == _owner.Parent)
                    _owner.Width = _target.Width + (_owner.Width - _targetData.w) * (info.Percent ? delta : 1);
                else
                    _owner.Width = _target.Width + (_owner.Width - _targetData.w);
                break;
            case RelationType.Height:
                if (_target == _owner.Parent)
                    _owner.Height = _target.Height + (_owner.Height - _targetData.h) * (info.Percent ? delta : 1);
                else
                    _owner.Height = _target.Height + (_owner.Height - _targetData.h);
                break;
        }
    }

    void AddRefTarget(GObject target)
    {
        if (target != _owner.Parent)
            target.AddEventListener("onPositionChanged", OnTargetXYChanged);
        target.AddEventListener("onSizeChanged", OnTargetSizeChanged);
        _targetData = (target.X, target.Y, target.Width, target.Height);
    }

    void ReleaseRefTarget(GObject target)
    {
        target.RemoveEventListener("onPositionChanged", OnTargetXYChanged);
        target.RemoveEventListener("onSizeChanged", OnTargetSizeChanged);
    }

    void OnTargetXYChanged(EventContext context)
    {
        if (_target == null) return;
        if (_owner.Parent?.Relations?.Handling != null) 
        {
            _targetData = (_target.X, _target.Y, _targetData.w, _targetData.h);
            return;
        }

        if (_owner.Parent != null)
            _owner.Parent.Relations!.Handling = _target;

        float ox = _owner.X;
        float oy = _owner.Y;
        float dx = _target.X - _targetData.x;
        float dy = _target.Y - _targetData.y;

        foreach (var info in _defs)
            ApplyOnXYChanged(info, dx, dy);

        _targetData = (_target.X, _target.Y, _targetData.w, _targetData.h);

        if (Math.Abs(ox - _owner.X) > 0.001f || Math.Abs(oy - _owner.Y) > 0.001f)
        {
            var ownerDx = _owner.X - ox;
            var ownerDy = _owner.Y - oy;
            _owner.UpdateGearFromRelations(1, ownerDx, ownerDy);
            if (_owner.Parent is GComponent parent)
            {
                parent.UpdateTransitionsFromRelations(_owner, ownerDx, ownerDy);
            }
        }

        if (_owner.Parent != null)
            _owner.Parent.Relations!.Handling = null;
    }

    void OnTargetSizeChanged(EventContext context)
    {
        if (_target == null) return;
        if (_owner.Parent?.Relations?.Handling != null)
        {
            _targetData = (_targetData.x, _targetData.y, _target.Width, _target.Height);
            return;
        }

        if (_owner.Parent != null)
            _owner.Parent.Relations!.Handling = _target;

        float ox = _owner.X;
        float oy = _owner.Y;
        float ow = _owner.Width;
        float oh = _owner.Height;

        foreach (var info in _defs)
            ApplyOnSizeChanged(info);

        _targetData = (_targetData.x, _targetData.y, _target.Width, _target.Height);

        if (Math.Abs(ox - _owner.X) > 0.001f || Math.Abs(oy - _owner.Y) > 0.001f)
        {
            var ownerDx = _owner.X - ox;
            var ownerDy = _owner.Y - oy;
            _owner.UpdateGearFromRelations(1, ownerDx, ownerDy);
            if (_owner.Parent is GComponent parent)
            {
                parent.UpdateTransitionsFromRelations(_owner, ownerDx, ownerDy);
            }
        }

        if (Math.Abs(ow - _owner.Width) > 0.001f || Math.Abs(oh - _owner.Height) > 0.001f)
        {
            var ownerDw = _owner.Width - ow;
            var ownerDh = _owner.Height - oh;
            _owner.UpdateGearFromRelations(2, ownerDw, ownerDh);
        }

        if (_owner.Parent != null)
            _owner.Parent.Relations!.Handling = null;
    }
}
#endif


