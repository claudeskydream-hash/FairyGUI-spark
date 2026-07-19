#if CLIENT
using FairyGUI;

namespace GameEntry;

// SCE-only UI assembly helper for this project. Not designed as a cross-project abstraction.
internal static class FguiMgr
{
    public static GComponent? LoadToRoot(string packagePath, string packageName, string componentName)
    {
        return FGUIBootstrapClientSys.LoadAndShow(packagePath, packageName, componentName);
    }

    public static GComponent? LoadToHost(
        string packagePath,
        string packageName,
        string componentName,
        GComponent host,
        bool replaceHostChildren = false)
    {
        if (host == null || host.Disposed)
        {
            return null;
        }

        FGUIBootstrapClientSys.EnsureInitialized();
        var pkg = FGUIBootstrapClientSys.EnsurePackageLoaded(packagePath, packageName);
        if (pkg == null)
        {
            Game.Logger.LogWarning(
                "[FGUI][Mgr] load-to-host package missing package={Package} path={Path} component={Component}",
                packageName,
                packagePath,
                componentName);
            return null;
        }

        var view = FGUIBootstrapClientSys.CreateComponentWithFallback(pkg, packageName, componentName);
        if (view == null)
        {
            Game.Logger.LogWarning(
                "[FGUI][Mgr] load-to-host component missing package={Package} component={Component}",
                packageName,
                componentName);
            return null;
        }

        if (replaceHostChildren)
        {
            host.RemoveChildren(0, -1, dispose: false);
        }

        host.AddChild(view);
        view.SetXY(0f, 0f);
        return view;
    }

    public static void AttachToRoot(GComponent view, string packageName = "", string componentName = "")
    {
        if (view == null || view.Disposed)
        {
            return;
        }

        UIRuntime.AddToFullScreenRoot(view);
    }

    public static void Remove(GObject? view, bool dispose = true)
    {
        if (view == null)
        {
            return;
        }

        if (UIRuntime.IsFullScreenContent(view))
        {
            UIRuntime.RemoveFromRoot(view, dispose);
            return;
        }

        if (view.Parent != null)
        {
            view.RemoveFromParent();
            if (dispose && !view.Disposed)
            {
                view.Dispose();
            }

            return;
        }

        UIRuntime.RemoveFromRoot(view, dispose);
    }
}
#endif
