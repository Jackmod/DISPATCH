using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Dispatch.UI.Imagery;

/// <summary>
/// Finds and caches the photographic assets compiled into the application.
/// </summary>
/// <remarks>
/// Images are discovered rather than referenced by name. The folder is filled
/// by hand with whatever files happen to be to hand, so anything that depended
/// on exact filenames would break the moment a browser saved something as
/// <c>xnvCsQmaSTg4BrungmQhni.jpg</c>.
///
/// <para>
/// Two conventions, in order. A file named for its purpose in
/// <c>Assets/Presets</c> wins, so the mapping can be made deliberate later.
/// Failing that, the pool in <c>Assets/Loading</c> is indexed positionally, so
/// arbitrary filenames still produce a stable, sensible assignment.
/// </para>
///
/// <para>
/// Bitmaps are cached because the same image backs a preset card and a
/// slideshow frame, and decoding a 300KB JPEG twice per screen is wasteful for
/// something that never changes.
/// </para>
/// </remarks>
public static class ImageCatalog
{
    private static readonly Uri LoadingFolder = new("avares://Dispatch.UI/Assets/Loading");
    private static readonly Uri PresetFolder = new("avares://Dispatch.UI/Assets/Presets");

    // Browsers save ordinary JPEGs under all of these.
    private static readonly string[] Extensions =
        [".jpg", ".jpeg", ".jfif", ".png", ".webp", ".bmp"];

    private static readonly Dictionary<Uri, Bitmap> Cache = [];
    private static readonly object Gate = new();

    private static IReadOnlyList<Uri>? _loadingPool;

    /// <summary>Every image in the loading pool, in filename order.</summary>
    public static IReadOnlyList<Uri> LoadingPool => _loadingPool ??= Enumerate(LoadingFolder);

    /// <summary>True when at least one photograph is compiled in.</summary>
    public static bool HasAny => LoadingPool.Count > 0;

    /// <summary>
    /// Resolves the image for a named surface â€” a preset tier, the welcome
    /// backdrop, and so on.
    /// </summary>
    /// <param name="key">Surface name, for example <c>full-duty</c>.</param>
    /// <param name="poolIndex">
    /// Position in the loading pool to fall back to when no file is named for
    /// this surface. Wraps, so a small pool still fills every surface.
    /// </param>
    /// <returns>A cached bitmap, or null when no images are present at all.</returns>
    public static Bitmap? For(string key, int poolIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var named = Enumerate(PresetFolder)
            .FirstOrDefault(uri => MatchesKey(uri, key));

        if (named is not null)
        {
            return Load(named);
        }

        var pool = LoadingPool;
        if (pool.Count == 0)
        {
            return null;
        }

        // Modulo twice so a negative index cannot produce a negative position.
        var index = ((poolIndex % pool.Count) + pool.Count) % pool.Count;
        return Load(pool[index]);
    }

    /// <summary>Loads one asset, caching the decoded bitmap.</summary>
    public static Bitmap? Load(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        lock (Gate)
        {
            if (Cache.TryGetValue(uri, out var cached))
            {
                return cached;
            }

            // A corrupt or half-copied drop-in must never take a screen down
            // over what is decoration.
            try
            {
                var bitmap = new Bitmap(AssetLoader.Open(uri));
                Cache[uri] = bitmap;
                return bitmap;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                return null;
            }
        }
    }

    private static bool MatchesKey(Uri uri, string key)
    {
        var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        return string.Equals(name, key, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<Uri> Enumerate(Uri folder)
    {
        try
        {
            return AssetLoader.GetAssets(folder, null)
                .Where(IsImage)
                .OrderBy(uri => uri.AbsolutePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // A folder that was never created is the normal empty case.
            return [];
        }
    }

    private static bool IsImage(Uri uri) =>
        Extensions.Any(extension =>
            uri.AbsolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
