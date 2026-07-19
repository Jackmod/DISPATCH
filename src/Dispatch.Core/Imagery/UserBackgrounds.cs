using Dispatch.Core.Infrastructure;

namespace Dispatch.Core.Imagery;

/// <summary>
/// Resolves optional user-supplied background images.
/// </summary>
/// <remarks>
/// Dispatch ships no photographic art. Every graphic in the application is
/// original vector, partly because Rockstar's and the mod authors' artwork is
/// not ours to redistribute, and partly because a bespoke identity holds
/// together where a collage of screenshots does not.
///
/// <para>
/// This exists so the user can override that for their own install. Anything
/// dropped in the backgrounds folder is picked up at runtime and used behind
/// the matching surface. The folder is outside the repository and is not
/// packaged, so whatever goes in it stays on that machine.
/// </para>
/// </remarks>
public interface IUserBackgrounds
{
    /// <summary>
    /// Returns the full path to a user image for <paramref name="key"/>, or
    /// null when none has been supplied and the vector scene should be used.
    /// </summary>
    /// <param name="key">Surface name, for example <c>standard</c> or <c>full-duty</c>.</param>
    string? TryResolve(string key);

    /// <summary>The folder to drop images into. Created on first read.</summary>
    string BackgroundsDirectory { get; }
}

/// <inheritdoc />
public sealed class UserBackgrounds : IUserBackgrounds
{
    // Ordered by preference: a lossless drop-in wins over a compressed one.
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    private readonly IAppPaths _paths;

    /// <summary>Constructs the resolver against the application's paths.</summary>
    public UserBackgrounds(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    /// <inheritdoc />
    public string BackgroundsDirectory => Path.Combine(_paths.Root, "backgrounds");

    /// <inheritdoc />
    public string? TryResolve(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // A missing or unreadable folder simply means no override. This is a
        // cosmetic lookup on a UI path, so it must never throw.
        try
        {
            Directory.CreateDirectory(BackgroundsDirectory);

            foreach (var extension in Extensions)
            {
                var candidate = Path.Combine(BackgroundsDirectory, key + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }
}
