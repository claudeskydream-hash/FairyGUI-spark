#if CLIENT
using System.Drawing;
using SCEFGUI;
using SCEFGUI.Core;
using SCEFGUI.UI;
using SCEFGUI.Event;

namespace SCEFGUI.Samples;

/// <summary>
/// Basics Main - Ported from Unity FairyGUI Examples/Basics/BasicsMain.cs
/// </summary>
public class BasicsMain
{
    private FGUIComponent? _mainView;
    private FGUIObject? _backBtn;
    private FGUIComponent? _demoContainer;
    private FGUIController? _viewController;
    private Dictionary<string, FGUIComponent> _demoObjects = new();

    // Windows for Window demo
    private Window1? _winA;
    private Window2? _winB;

    // Popup for Popup demo
    private PopupMenu? _pm;
    private FGUIComponent? _popupCom;

    // Depth demo
    private float _startPosX;
    private float _startPosY;

    public void Start()
    {
        Game.Logger.LogInformation("[FGUI] Starting BasicsMain...");

        // 标准做法：先将 FGUIRoot 添加到舞台（会自动全屏适配）
        FGUIRoot.Instance.AddToStage();
        Game.Logger.LogInformation($"[FGUI] FGUIRoot added to stage, size: {FGUIRoot.Instance.Width}x{FGUIRoot.Instance.Height}");

        // UIPackage.AddPackage("UI/Basics")
        FGUIManager.AddPackage("ui/Basics", LoadResource);

        // UIConfig settings would go here if needed
        // UIConfig.verticalScrollBar = "ui://Basics/ScrollBar_VT";
        // UIConfig.horizontalScrollBar = "ui://Basics/ScrollBar_HZ";
        // UIConfig.popupMenu = "ui://Basics/PopupMenu";

        // _mainView = this.GetComponent<UIPanel>().ui
        var mainObj = FGUIManager.CreateObject("Basics", "Main");
        if (mainObj == null)
        {
            Game.Logger.LogError("[FGUI] Failed to create Main component!");
            return;
        }

        _mainView = mainObj as FGUIComponent;
        if (_mainView == null)
        {
            Game.Logger.LogError("[FGUI] Main is not a FGUIComponent!");
            return;
        }

        // 标准做法：将组件添加为 FGUIRoot 的子组件
        _mainView.SetXY(0, 0);
        FGUIRoot.Instance.AddChild(_mainView);

        // Setup UI
        _backBtn = _mainView.GetChild("btn_Back");
        if (_backBtn != null)
        {
            _backBtn.Visible = false;
            _backBtn.OnClick.Add(OnClickBack);
        }

        _demoContainer = _mainView.GetChild("container") as FGUIComponent;
        _viewController = _mainView.GetController("c1");

        // Setup demo buttons
        int cnt = _mainView.NumChildren;
        for (int i = 0; i < cnt; i++)
        {
            var obj = _mainView.GetChildAt(i);
            if (obj?.Group != null && obj.Group.Name == "btns")
                obj.OnClick.Add(RunDemo);
        }

        Game.Logger.LogInformation("[FGUI] BasicsMain started successfully!");
    }

    private void RunDemo(EventContext context)
    {
        if (context.Sender is not FGUIObject sender) return;

        string type = sender.Name;
        if (type.StartsWith("btn_"))
            type = type.Substring(4);

        if (!_demoObjects.TryGetValue(type, out var obj))
        {
            var created = FGUIManager.CreateObject("Basics", "Demo_" + type);
            if (created is FGUIComponent comp)
            {
                obj = comp;
                _demoObjects[type] = obj;
            }
            else
            {
                Game.Logger.LogWarning($"[FGUI] Failed to create demo: Demo_{type}");
                return;
            }
        }

        _demoContainer?.RemoveChildren();
        _demoContainer?.AddChild(obj);
        if (_viewController != null)
            _viewController.SelectedIndex = 1;
        if (_backBtn != null)
            _backBtn.Visible = true;

        switch (type)
        {
            case "Graph": PlayGraph(); break;
            case "Button": PlayButton(); break;
            case "Text": PlayText(); break;
            case "Grid": PlayGrid(); break;
            case "Transition": PlayTransition(); break;
            case "Window": PlayWindow(); break;
            case "Popup": PlayPopup(); break;
            case "Drag&Drop": PlayDragDrop(); break;
            case "Depth": PlayDepth(); break;
            case "ProgressBar": PlayProgressBar(); break;
        }
    }

    private void OnClickBack(EventContext context)
    {
        if (_viewController != null)
            _viewController.SelectedIndex = 0;
        if (_backBtn != null)
            _backBtn.Visible = false;
    }

    //-----------------------------
    private void PlayGraph()
    {
        if (!_demoObjects.TryGetValue("Graph", out var obj)) return;

        // Note: SCE doesn't have Shape/EllipseMesh/PolygonMesh/LineMesh like Unity
        // Graph demos would need custom implementation using FGUIGraph.DrawRect/DrawEllipse/DrawPolygon
        
        // In Unity:
        // shape = obj.GetChild("pie").asGraph.shape;
        // EllipseMesh ellipse = shape.graphics.GetMeshFactory<EllipseMesh>();
        // ellipse.startDegree = 30;
        // ellipse.endDegreee = 300;
        
        Game.Logger.LogInformation("[FGUI] PlayGraph - Shape drawing not fully supported in SCE");
    }

    //-----------------------------
    private void PlayButton()
    {
        if (!_demoObjects.TryGetValue("Button", out var obj)) return;

        obj.GetChild("n34")?.OnClick.Add(ctx =>
        {
            Game.Logger.LogInformation("[FGUI] click button");
        });
    }

    //-----------------------------
    private void PlayText()
    {
        if (!_demoObjects.TryGetValue("Text", out var obj)) return;

        var richText = obj.GetChild("n12") as FGUIRichTextField;
        if (richText != null)
        {
            richText.OnClickLink.Add(ctx =>
            {
                var linkData = ctx.Data as string ?? "";
                richText.Text = $"[img]ui://Basics/pet[/img][color=#FF0000]You click the link[/color]: {linkData}";
            });
        }

        obj.GetChild("n25")?.OnClick.Add(ctx =>
        {
            var src = obj.GetChild("n22") as FGUITextField;
            var dst = obj.GetChild("n24") as FGUITextField;
            if (src != null && dst != null)
                dst.Text = src.Text;
        });
    }

    //-----------------------------
    private void PlayGrid()
    {
        if (!_demoObjects.TryGetValue("Grid", out var obj)) return;

        var list1 = obj.GetChild("list1") as FGUIList;
        if (list1 != null)
        {
            list1.RemoveChildrenToPool();
            string[] testNames = { "Windows", "MacOS", "Linux", "Android", "iOS", "WebGL", "PS4", "XboxOne" };
            Color[] testColor = { Color.Yellow, Color.Red, Color.White, Color.Cyan };
            var random = new Random();
            
            for (int i = 0; i < testNames.Length; i++)
            {
                var item = list1.AddItemFromPool() as FGUIButton;
                if (item != null)
                {
                    var t0 = item.GetChild("t0") as FGUITextField;
                    var t1 = item.GetChild("t1") as FGUITextField;
                    var t2 = item.GetChild("t2") as FGUITextField;
                    var star = item.GetChild("star") as FGUIProgressBar;
                    
                    if (t0 != null) t0.Text = (i + 1).ToString();
                    if (t1 != null) t1.Text = testNames[i];
                    if (t2 != null) t2.Color = testColor[random.Next(testColor.Length)];
                    if (star != null) star.Value = random.Next(1, 4) * 100 / 3;
                }
            }
        }

        var list2 = obj.GetChild("list2") as FGUIList;
        if (list2 != null)
        {
            list2.RemoveChildrenToPool();
            string[] testNames = { "Windows", "MacOS", "Linux", "Android", "iOS" };
            var random = new Random();
            
            for (int i = 0; i < testNames.Length; i++)
            {
                var item = list2.AddItemFromPool() as FGUIButton;
                if (item != null)
                {
                    var cb = item.GetChild("cb") as FGUIButton;
                    var t1 = item.GetChild("t1") as FGUITextField;
                    var mc = item.GetChild("mc") as FGUIMovieClip;
                    var t3 = item.GetChild("t3") as FGUITextField;
                    
                    if (cb != null) cb.Selected = false;
                    if (t1 != null) t1.Text = testNames[i];
                    if (mc != null) mc.Playing = i % 2 == 0;
                    if (t3 != null) t3.Text = random.Next(10000).ToString();
                }
            }
        }
    }

    //-----------------------------
    private void PlayTransition()
    {
        if (!_demoObjects.TryGetValue("Transition", out var obj)) return;

        var n2 = obj.GetChild("n2") as FGUIComponent;
        n2?.GetTransition("t0")?.Play(null, int.MaxValue, 0);

        var n3 = obj.GetChild("n3") as FGUIComponent;
        n3?.GetTransition("peng")?.Play(null, int.MaxValue, 0);

        // Note: In Unity, transitions stop on OnAddedToStage
        // obj.onAddedToStage.Add(() => { ... });
    }

    //-----------------------------
    private void PlayWindow()
    {
        if (!_demoObjects.TryGetValue("Window", out var obj)) return;

        obj.GetChild("n0")?.OnClick.Add(ctx =>
        {
            _winA ??= new Window1();
            _winA.Show();
        });

        obj.GetChild("n1")?.OnClick.Add(ctx =>
        {
            _winB ??= new Window2();
            _winB.Show();
        });
    }

    //-----------------------------
    private void PlayPopup()
    {
        if (_pm == null)
        {
            _pm = new PopupMenu();
            _pm.AddItem("Item 1", ClickMenu);
            _pm.AddItem("Item 2", ClickMenu);
            _pm.AddItem("Item 3", ClickMenu);
            _pm.AddItem("Item 4", ClickMenu);
        }

        if (_popupCom == null)
        {
            _popupCom = FGUIManager.CreateObject("Basics", "Component12") as FGUIComponent;
            if (_popupCom != null)
            {
                _popupCom.SetXY(
                    (FGUIRoot.Instance.Width - _popupCom.Width) / 2,
                    (FGUIRoot.Instance.Height - _popupCom.Height) / 2);
            }
        }

        if (!_demoObjects.TryGetValue("Popup", out var obj)) return;

        obj.GetChild("n0")?.OnClick.Add(ctx =>
        {
            _pm?.Show(ctx.Sender as FGUIObject, PopupDirection.Down);
        });

        obj.GetChild("n1")?.OnClick.Add(ctx =>
        {
            if (_popupCom != null)
                FGUIRoot.Instance.ShowPopup(_popupCom);
        });

        obj.OnRightClick.Add(ctx =>
        {
            _pm?.Show();
        });
    }

    private void ClickMenu()
    {
        Game.Logger.LogInformation("[FGUI] Menu item clicked");
    }

    //-----------------------------
    private void PlayDepth()
    {
        if (!_demoObjects.TryGetValue("Depth", out var obj)) return;

        var testContainer = obj.GetChild("n22") as FGUIComponent;
        if (testContainer == null) return;

        var fixedObj = testContainer.GetChild("n0");
        if (fixedObj != null)
        {
            fixedObj.SortingOrder = 100;
            fixedObj.Draggable = true;
            _startPosX = fixedObj.X;
            _startPosY = fixedObj.Y;
        }

        // Remove all children except fixedObj
        int numChildren = testContainer.NumChildren;
        int i = 0;
        while (i < numChildren)
        {
            var child = testContainer.GetChildAt(i);
            if (child != fixedObj)
            {
                testContainer.RemoveChildAt(i);
                numChildren--;
            }
            else
                i++;
        }

        obj.GetChild("btn0")?.OnClick.Add(ctx =>
        {
            var graph = new FGUIGraph();
            _startPosX += 10;
            _startPosY += 10;
            graph.SetXY(_startPosX, _startPosY);
            graph.DrawRect(150, 150, 1, Color.Black, Color.Red);
            testContainer.AddChild(graph);
        });

        obj.GetChild("btn1")?.OnClick.Add(ctx =>
        {
            var graph = new FGUIGraph();
            _startPosX += 10;
            _startPosY += 10;
            graph.SetXY(_startPosX, _startPosY);
            graph.DrawRect(150, 150, 1, Color.Black, Color.Green);
            graph.SortingOrder = 200;
            testContainer.AddChild(graph);
        });
    }

    //-----------------------------
    private void PlayDragDrop()
    {
        if (!_demoObjects.TryGetValue("Drag&Drop", out var obj)) return;

        var a = obj.GetChild("a");
        if (a != null)
            a.Draggable = true;

        var b = obj.GetChild("b") as FGUIButton;
        if (b != null)
        {
            b.Draggable = true;
            b.OnDragStart.Add(ctx =>
            {
                // Cancel the original dragging, and start a new one with an agent
                ctx.PreventDefault();
                DragDropManager.StartDrag(b, ctx.Data, b.Icon);
            });
        }

        var c = obj.GetChild("c") as FGUIButton;
        if (c != null)
        {
            c.Icon = null;
            c.OnDrop.Add(ctx =>
            {
                c.Icon = ctx.Data as string;
            });
        }

        var bounds = obj.GetChild("n7");
        if (bounds != null)
        {
            // In Unity: d.dragBounds = rect
            var d = obj.GetChild("d") as FGUIButton;
            if (d != null)
                d.Draggable = true;
        }
    }

    //-----------------------------
    private void PlayProgressBar()
    {
        if (!_demoObjects.TryGetValue("ProgressBar", out var obj)) return;

        // In Unity: Timers.inst.Add(0.001f, 0, __playProgress);
        // Here we would use Game.Delay or similar for animation
        
        // Initialize progress bars
        int cnt = obj.NumChildren;
        for (int i = 0; i < cnt; i++)
        {
            var child = obj.GetChildAt(i) as FGUIProgressBar;
            if (child != null)
                child.Value = 0;
        }

        // Note: Timer-based animation would need SCE's Game.Delay implementation
        Game.Logger.LogInformation("[FGUI] ProgressBar demo started");
    }

    private byte[]? LoadResource(string name, string extension)
    {
        var prefixes = new[] { "", "AppBundle/", "ui/AppBundle/" };
        foreach (var prefix in prefixes)
        {
            string path = prefix + name + extension;
            try
            {
                if (File.Exists(path))
                    return File.ReadAllBytes(path);
            }
            catch { }
        }
        return null;
    }

    public void Dispose()
    {
        _mainView?.Dispose();
        _winA?.Dispose();
        _winB?.Dispose();
        _popupCom?.Dispose();
        _demoObjects.Clear();
        FGUIManager.RemoveAllPackages();
    }
}
#endif
