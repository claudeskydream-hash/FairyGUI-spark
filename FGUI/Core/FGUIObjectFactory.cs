#if CLIENT
using FairyGUI;

namespace FairyGUI;

public static class UIObjectFactory
{
    private static readonly Dictionary<string, Func<GComponent>> _extensions = new();
    private static readonly Dictionary<string, Type> _packageItemExtensions = new();
    private static Type? _loaderExtension;

    /// <summary>
    /// Set extension for a component URL
    /// </summary>
    public static void SetExtension(string url, Func<GComponent> creator) => _extensions[url] = creator;

    /// <summary>
    /// Set extension class for a package item URL (like MailItem for virtual list)
    /// </summary>
    public static void SetPackageItemExtension(string url, Type type) => _packageItemExtensions[url] = type;

    /// <summary>
    /// Set custom loader extension class
    /// </summary>
    public static void SetLoaderExtension<T>() where T : GLoader, new() => _loaderExtension = typeof(T);

    /// <summary>
    /// Set custom loader extension class by type
    /// </summary>
    public static void SetLoaderExtension(Type type) => _loaderExtension = type;

    /// <summary>
    /// Clear loader extension
    /// </summary>
    public static void ClearLoaderExtension() => _loaderExtension = null;

    /// <summary>
    /// Try to get extension for a URL
    /// </summary>
    public static bool TryGetExtension(string url, out Func<GComponent>? creator) => _extensions.TryGetValue(url, out creator);

    /// <summary>
    /// Try to create object from package item extension
    /// </summary>
    public static GObject? TryCreateFromExtension(string url)
    {
        if (_packageItemExtensions.TryGetValue(url, out var type))
        {
            try
            {
                return Activator.CreateInstance(type) as GObject;
            }
            catch { }
        }
        return null;
    }

    public static GObject? NewObject(PackageItem item)
    {
        // Check extension with the canonical exported URL first: ui://{pkgId}{itemId}
        GObject? extObj = null;
        if (!string.IsNullOrWhiteSpace(item.Owner?.Id) && !string.IsNullOrWhiteSpace(item.Id))
        {
            var idUrl = UIPackage.URL_PREFIX + item.Owner!.Id + item.Id;
            extObj = TryCreateFromExtension(idUrl);
        }

        // Fallback: ui://{packageName}/{itemName}
        if (extObj == null)
        {
            string url = $"ui://{item.Owner?.Name}/{item.Name}";
            extObj = TryCreateFromExtension(url);
        }

        if (extObj != null)
            return extObj;

        return NewObject(item.ObjectType);
    }

    public static GObject? NewObject(ObjectType type)
    {
        return type switch
        {
            ObjectType.Image => new FairyGUI.GImage(),
            ObjectType.MovieClip => new FairyGUI.GMovieClip(),
            ObjectType.Component => new FairyGUI.GComponent(),
            ObjectType.Text => new FairyGUI.GTextField(),
            ObjectType.RichText => new GRichTextField(),
            ObjectType.InputText => new GTextInput(),
            ObjectType.Group => new FairyGUI.GGroup(),
            ObjectType.List => new FairyGUI.GList(),
            ObjectType.Graph => new FairyGUI.GGraph(),
            ObjectType.Loader => CreateLoader(),
            ObjectType.Button => new FairyGUI.GButton(),
            ObjectType.Label => new FairyGUI.GLabel(),
            ObjectType.ProgressBar => new FairyGUI.GProgressBar(),
            ObjectType.Slider => new GSlider(),
            ObjectType.ScrollBar => new GScrollBar(),
            ObjectType.ComboBox => new FairyGUI.GComboBox(),
            ObjectType.Tree => new GTree(),
            _ => null
        };
    }

    private static GLoader CreateLoader()
    {
        if (_loaderExtension != null)
        {
            try
            {
                var loader = Activator.CreateInstance(_loaderExtension) as GLoader;
                if (loader != null)
                    return loader;
            }
            catch { }
        }
        return new GLoader();
    }
}
#endif

