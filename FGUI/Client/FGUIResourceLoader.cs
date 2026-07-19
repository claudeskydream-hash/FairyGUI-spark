#if CLIENT
namespace GameEntry;

public static class FGUIResourceLoader
{
    private const string PackageDescSuffix = "_fui.bytes";
    private static readonly Lazy<string[]> EmbeddedResources = new(() => typeof(FGUIResourceLoader).Assembly.GetManifestResourceNames());

    private static readonly string[] SearchPrefixes =
    [
        "user_files/",          // 本项目：SyncAsset 把 *_fui.bytes 放到 ui/AppBundle/user_files/ui（运行时最可靠）
        "",
        "ui/",
        "ui/image/",
        "ui/image/fgui/scatter/",
        "ui/image/fgui/",
        "AppBundle/",
        "ui/AppBundle/",
    ];

    public static byte[]? LoadBytes(string name, string extension)
    {
        var normalizedName = NormalizePath(name);
        var normalizedExtension = extension ?? string.Empty;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inspected = new List<string>(16);
        foreach (var candidate in EnumerateCandidates(normalizedName, normalizedExtension))
        {
            if (!visited.Add(candidate))
            {
                continue;
            }

            inspected.Add(candidate);
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                var data = File.ReadAllBytes(candidate);
                return data;
            }
            catch (Exception ex)
            {
                Game.Logger.LogWarning(ex, "[FGUI] Failed to read resource bytes: {Path}", candidate);
            }
        }

        var embeddedData = LoadEmbeddedBytes(normalizedName, normalizedExtension);
        if (embeddedData != null)
        {
            return embeddedData;
        }

        if (inspected.Count > 0)
        {
            var sampleList = new List<string>(Math.Min(inspected.Count, 12));
            for (var i = 0; i < inspected.Count && i < 12; i++)
            {
                sampleList.Add(inspected[i]);
            }

            var sample = string.Join(" | ", sampleList);
            Game.Logger.LogWarning(
                "[FGUI] Resource not found: name={Name} ext={Ext} cwd={Cwd} appBase={AppBase} checked={Sample}",
                normalizedName,
                normalizedExtension,
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
                sample);
        }

        return null;
    }

    public static string DescribeCandidates(string name, string extension, int maxCount = 12)
    {
        var normalizedName = NormalizePath(name);
        var normalizedExtension = extension ?? string.Empty;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>(Math.Max(4, maxCount));
        foreach (var candidate in EnumerateCandidates(normalizedName, normalizedExtension))
        {
            if (!visited.Add(candidate))
            {
                continue;
            }

            candidates.Add(candidate);
            if (candidates.Count >= maxCount)
            {
                break;
            }
        }

        return string.Join(" | ", candidates);
    }

    public static string? ResolvePackagePath(string packagePath)
    {
        var normalizedPackagePath = NormalizePath(packagePath).TrimEnd('/');
        if (string.IsNullOrEmpty(normalizedPackagePath))
        {
            return null;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateCandidates(normalizedPackagePath + "_fui", ".bytes"))
        {
            if (!visited.Add(candidate))
            {
                continue;
            }

            if (!File.Exists(candidate))
            {
                continue;
            }

            if (!candidate.EndsWith(PackageDescSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var resolvedPath = candidate.Substring(0, candidate.Length - PackageDescSuffix.Length);
            try
            {
                return Path.GetFullPath(resolvedPath);
            }
            catch
            {
                return resolvedPath;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string normalizedName, string normalizedExtension)
    {
        var results = new List<string>(64);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefixedCandidates = new List<string>(SearchPrefixes.Length);
        var seenPrefixed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prefix in SearchPrefixes)
        {
            var candidate = BuildPath(prefix, normalizedName, normalizedExtension);
            if (seenPrefixed.Add(candidate))
            {
                prefixedCandidates.Add(candidate);
            }
        }

        if (Path.IsPathRooted(normalizedName))
        {
            var rooted = normalizedName + normalizedExtension;
            if (visited.Add(rooted))
            {
                results.Add(rooted);
            }
        }

        foreach (var candidate in prefixedCandidates)
        {
            if (visited.Add(candidate))
            {
                results.Add(candidate);
            }
        }

        foreach (var root in EnumerateSearchRoots())
        {
            foreach (var candidate in prefixedCandidates)
            {
                if (Path.IsPathRooted(candidate))
                {
                    continue;
                }

                var relativePath = candidate.Replace('/', Path.DirectorySeparatorChar);
                var combined = Path.Combine(root, relativePath);
                if (visited.Add(combined))
                {
                    results.Add(combined);
                }
            }
        }

        return results;
    }

    private static IEnumerable<string> EnumerateProjectRootHints(string searchRoot)
    {
        // Disabled for compatibility with stripped runtimes.
        return Array.Empty<string>();
    }

    private static string[] SafeGetDirectories(string root)
    {
        try
        {
            return Directory.GetDirectories(root);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var roots = new List<string>(16);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var startPoints = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        };

        foreach (var start in startPoints)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            var current = Path.GetFullPath(start);
            for (var i = 0; i < 8 && !string.IsNullOrEmpty(current); i++)
            {
                if (visited.Add(current))
                {
                    roots.Add(current);
                }

                var parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                current = parent.FullName;
            }

            foreach (var hint in EnumerateProjectRootHints(start))
            {
                if (visited.Add(hint))
                {
                    roots.Add(hint);
                }
            }
        }

        return roots;
    }

    private static string BuildPath(string prefix, string name, string extension)
    {
        var normalizedPrefix = NormalizePath(prefix);
        if (string.IsNullOrEmpty(normalizedPrefix))
        {
            return name + extension;
        }

        if (name.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return name + extension;
        }

        return $"{normalizedPrefix}{name}{extension}";
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Replace('\\', '/').TrimStart('/');
    }

    private static byte[]? LoadEmbeddedBytes(string normalizedName, string normalizedExtension)
    {
        var relativeKey = (normalizedName + normalizedExtension).Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(relativeKey))
        {
            return null;
        }

        var dotKey = relativeKey.Replace('/', '.');
        var fileKey = Path.GetFileName(relativeKey);
        foreach (var resourceName in EmbeddedResources.Value)
        {
            if (!resourceName.EndsWith(dotKey, StringComparison.OrdinalIgnoreCase) &&
                !resourceName.EndsWith(relativeKey, StringComparison.OrdinalIgnoreCase) &&
                !resourceName.EndsWith(fileKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var stream = typeof(FGUIResourceLoader).Assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    continue;
                }

                using var memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                Game.Logger.LogWarning(ex, "[FGUI] Failed to read embedded resource: {ResourceName}", resourceName);
                return null;
            }
        }

        return null;
    }
}
#endif
