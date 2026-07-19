#if CLIENT
using System.IO;
using System.Text;
using System.Text.Json;

namespace FairyGUI.Render;

internal static class FGUIScatterManifest
{
    private const string ManifestResourceName = "ui/image/fgui/scatter/manifest";
    private const string MovieClipManifestResourceName = "ui/image/fgui/scatter/movieclip-manifest";
    private static readonly Dictionary<string, string> Mapping = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> MovieClipMapping = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<MovieClipFrameMap>> MovieClipFramesByClip = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MissingLogged = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MovieClipMissingLogged = new(StringComparer.OrdinalIgnoreCase);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static bool _movieClipManifestUnavailableLogged;
    private static bool _loaded;
    private static bool _manifestAvailable;
    private static bool _movieClipManifestAvailable;

    public static bool TryResolve(string? packageId, string? itemId, out string imagePath)
    {
        imagePath = string.Empty;
        if (UIRuntime.CurrentImageAssetMode == ImageAssetMode.Atlas) return false; // Atlas 模式不走散图
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        EnsureLoaded();
        if (!_manifestAvailable)
        {
            return false;
        }

        var key = BuildKey(packageId, itemId);
        if (Mapping.TryGetValue(key, out var path))
        {
            imagePath = path;
            return true;
        }

        if (MissingLogged.Add(key))
        {
            Game.Logger.LogError("[FGUI][SCATTER] manifest missing mapping packageId={PackageId} itemId={ItemId}",
                packageId, itemId);
        }

        return false;
    }

    public static bool TryResolveMovieClipFrame(string? packageId, string? clipItemId, int frameIndex, out string imagePath)
    {
        imagePath = string.Empty;
        if (UIRuntime.CurrentImageAssetMode == ImageAssetMode.Atlas) return false; // Atlas 模式不走散图
        packageId = packageId?.Trim();
        clipItemId = clipItemId?.Trim();
        if (string.IsNullOrWhiteSpace(packageId) ||
            string.IsNullOrWhiteSpace(clipItemId) ||
            frameIndex < 0)
        {
            return false;
        }

        EnsureLoaded();
        if (!_movieClipManifestAvailable)
        {
            if (!TryReloadMovieClipManifest())
            {
                if (!_movieClipManifestUnavailableLogged)
                {
                    _movieClipManifestUnavailableLogged = true;
                    Game.Logger.LogError("[FGUI][SCATTER][MOVIECLIP] manifest unavailable, cannot resolve movie clip frames");
                }

                return false;
            }
        }

        var key = BuildMovieClipKey(packageId, clipItemId, frameIndex);
        if (MovieClipMapping.TryGetValue(key, out var path))
        {
            imagePath = path;
            return true;
        }

        // Runtime may have loaded before latest manifest sync; retry one hot reload on miss.
        if (TryReloadMovieClipManifest() && MovieClipMapping.TryGetValue(key, out path))
        {
            imagePath = path;
            Game.Logger.LogInformation(
                "[FGUI][SCATTER][MOVIECLIP] resolved after manifest reload packageId={PackageId} clipItemId={ClipItemId} frameIndex={FrameIndex}",
                packageId, clipItemId, frameIndex);
            return true;
        }

        var clipKey = BuildMovieClipClipKey(packageId, clipItemId);
        if (MovieClipFramesByClip.TryGetValue(clipKey, out var frames) && frames.Count > 0)
        {
            var fallbackSlot = frameIndex % frames.Count;
            if (fallbackSlot < 0)
            {
                fallbackSlot += frames.Count;
            }

            var fallback = frames[fallbackSlot];
            imagePath = fallback.ImagePath;
            var fallbackKey = $"{key}::fallback";
            if (MovieClipMissingLogged.Add(fallbackKey))
            {
                Game.Logger.LogWarning(
                    "[FGUI][SCATTER][MOVIECLIP] exact frame missing, fallback packageId={PackageId} clipItemId={ClipItemId} frameIndex={FrameIndex} fallbackFrame={FallbackFrame}",
                    packageId, clipItemId, frameIndex, fallback.FrameIndex);
            }
            return true;
        }

        if (MovieClipMissingLogged.Add(key))
        {
            Game.Logger.LogError(
                "[FGUI][SCATTER][MOVIECLIP] manifest missing mapping packageId={PackageId} clipItemId={ClipItemId} frameIndex={FrameIndex}",
                packageId, clipItemId, frameIndex);
        }

        return false;
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        // Atlas 模式不使用散图清单：直接置为不可用，避免加载缺失的 manifest 刷错误日志。
        if (UIRuntime.CurrentImageAssetMode == ImageAssetMode.Atlas)
        {
            _manifestAvailable = false;
            _movieClipManifestAvailable = false;
            return;
        }
        LoadImageManifest();
        LoadMovieClipManifest();
    }

    private static void LoadImageManifest()
    {
        var data = LoadManifestBytes(ManifestResourceName);
        if (data == null || data.Length == 0)
        {
            // Manifest is optional when runtime uses Scatter fallback path convention.
            _manifestAvailable = false;
            return;
        }

        ScatterManifestFile? manifest;
        try
        {
            var jsonBytes = NormalizeJsonPayload(data);
            manifest = JsonSerializer.Deserialize<ScatterManifestFile>(jsonBytes, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            Game.Logger.LogError(ex, "[FGUI][SCATTER] manifest parse failed");
            return;
        }

        if (manifest?.Entries == null || manifest.Entries.Count == 0)
        {
            Game.Logger.LogWarning("[FGUI][SCATTER] manifest has no entries");
            _manifestAvailable = false;
            return;
        }

        foreach (var entry in manifest.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.PackageId) ||
                string.IsNullOrWhiteSpace(entry.ItemId) ||
                string.IsNullOrWhiteSpace(entry.ImagePath))
            {
                continue;
            }

            var key = BuildKey(entry.PackageId, entry.ItemId);
            if (!Mapping.ContainsKey(key))
            {
                Mapping[key] = NormalizeImagePath(entry.ImagePath);
            }
        }

        _manifestAvailable = true;
        Game.Logger.LogInformation("[FGUI][SCATTER] manifest loaded entries={Count}", Mapping.Count);
    }

    private static void LoadMovieClipManifest()
    {
        var data = LoadManifestBytes(MovieClipManifestResourceName);
        if (data == null || data.Length == 0)
        {
            _movieClipManifestAvailable = false;
            var checkedBytes = GameEntry.FGUIResourceLoader.DescribeCandidates(MovieClipManifestResourceName, ".bytes", 8);
            var checkedJson = GameEntry.FGUIResourceLoader.DescribeCandidates(MovieClipManifestResourceName, ".json", 8);
            Game.Logger.LogError(
                "[FGUI][SCATTER][MOVIECLIP] manifest file missing or empty: {Path}(.bytes/.json) cwd={Cwd} appBase={AppBase} checkedBytes={CheckedBytes} checkedJson={CheckedJson}",
                MovieClipManifestResourceName,
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
                checkedBytes,
                checkedJson);
            return;
        }

        MovieClipScatterManifestFile? manifest;
        try
        {
            var jsonBytes = NormalizeJsonPayload(data);
            manifest = JsonSerializer.Deserialize<MovieClipScatterManifestFile>(jsonBytes, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            Game.Logger.LogError(ex, "[FGUI][SCATTER][MOVIECLIP] manifest parse failed");
            return;
        }

        if (manifest?.Entries == null || manifest.Entries.Count == 0)
        {
            Game.Logger.LogWarning("[FGUI][SCATTER][MOVIECLIP] manifest has no entries");
            _movieClipManifestAvailable = false;
            return;
        }

        foreach (var entry in manifest.Entries)
        {
            var packageId = entry.PackageId?.Trim();
            var clipItemId = entry.ClipItemId?.Trim();
            if (string.IsNullOrWhiteSpace(packageId) ||
                string.IsNullOrWhiteSpace(clipItemId) ||
                string.IsNullOrWhiteSpace(entry.ImagePath) ||
                entry.FrameIndex < 0)
            {
                continue;
            }

            var key = BuildMovieClipKey(packageId, clipItemId, entry.FrameIndex);
            if (!MovieClipMapping.ContainsKey(key))
            {
                var normalizedPath = NormalizeImagePath(entry.ImagePath);
                MovieClipMapping[key] = normalizedPath;

                var clipKey = BuildMovieClipClipKey(packageId, clipItemId);
                if (!MovieClipFramesByClip.TryGetValue(clipKey, out var list))
                {
                    list = [];
                    MovieClipFramesByClip[clipKey] = list;
                }

                if (!list.Exists(x => x.FrameIndex == entry.FrameIndex))
                {
                    list.Add(new MovieClipFrameMap(entry.FrameIndex, normalizedPath));
                }
            }
        }

        foreach (var frames in MovieClipFramesByClip.Values)
        {
            frames.Sort(static (a, b) => a.FrameIndex.CompareTo(b.FrameIndex));
        }

        _movieClipManifestAvailable = true;
        _movieClipManifestUnavailableLogged = false;
        Game.Logger.LogInformation("[FGUI][SCATTER][MOVIECLIP] manifest loaded entries={Count}", MovieClipMapping.Count);
    }

    private static bool TryReloadMovieClipManifest()
    {
        MovieClipMapping.Clear();
        MovieClipFramesByClip.Clear();
        _movieClipManifestAvailable = false;
        LoadMovieClipManifest();
        return _movieClipManifestAvailable && MovieClipMapping.Count > 0;
    }

    private static byte[]? LoadManifestBytes(string resourceName)
    {
        // Spark runtime packaging is more stable for .bytes than .json.
        var bytes = GameEntry.FGUIResourceLoader.LoadBytes(resourceName, ".bytes");
        if (bytes != null && bytes.Length > 0)
        {
            return bytes;
        }

        return GameEntry.FGUIResourceLoader.LoadBytes(resourceName, ".json");
    }

    private static byte[] NormalizeJsonPayload(byte[] data)
    {
        if (data.Length >= 3 &&
            data[0] == Utf8Bom[0] &&
            data[1] == Utf8Bom[1] &&
            data[2] == Utf8Bom[2])
        {
            return data[3..];
        }

        // Defensive fallback for UTF-16 manifests.
        if (data.Length >= 2)
        {
            if (data[0] == 0xFF && data[1] == 0xFE)
            {
                var text = Encoding.Unicode.GetString(data, 2, data.Length - 2);
                return Encoding.UTF8.GetBytes(text);
            }

            if (data[0] == 0xFE && data[1] == 0xFF)
            {
                var text = Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
                return Encoding.UTF8.GetBytes(text);
            }
        }

        return data;
    }

    private static string NormalizeImagePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string BuildKey(string packageId, string itemId) => $"{packageId}::{itemId}";

    private static string BuildMovieClipKey(string packageId, string clipItemId, int frameIndex) =>
        $"{packageId.Trim()}::{clipItemId.Trim()}::{frameIndex}";

    private static string BuildMovieClipClipKey(string packageId, string clipItemId) =>
        $"{packageId.Trim()}::{clipItemId.Trim()}";

    private readonly record struct MovieClipFrameMap(int FrameIndex, string ImagePath);
}

internal sealed class ScatterManifestFile
{
    public int Version { get; set; }
    public string GeneratedAt { get; set; } = string.Empty;
    public List<ScatterManifestEntry> Entries { get; set; } = [];
}

internal sealed class ScatterManifestEntry
{
    public string PackageId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
}

internal sealed class MovieClipScatterManifestFile
{
    public int Version { get; set; }
    public string GeneratedAt { get; set; } = string.Empty;
    public List<MovieClipScatterManifestEntry> Entries { get; set; } = [];
}

internal sealed class MovieClipScatterManifestEntry
{
    public string PackageId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string ClipItemId { get; set; } = string.Empty;
    public string ClipName { get; set; } = string.Empty;
    public int FrameIndex { get; set; }
    public string SpriteItemId { get; set; } = string.Empty;
    public string LogicalPath { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
}
#endif
