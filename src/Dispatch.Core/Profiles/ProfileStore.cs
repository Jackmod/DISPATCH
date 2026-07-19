using System.Text.Json;
using System.Text.Json.Serialization;
using Dispatch.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Profiles;

/// <summary>Reads and writes the user's profile.</summary>
public interface IProfileStore
{
    /// <summary>Loads the profile, returning an empty one when none exists.</summary>
    Task<DispatchProfile> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the profile, atomically.</summary>
    Task SaveAsync(DispatchProfile profile, CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON-backed profile storage.
/// </summary>
/// <remarks>
/// Writes go to a temporary file and are then moved over the target, so a
/// crash or a power cut mid-write cannot leave a truncated profile. Writing in
/// place is how people lose the identity and control scheme they spent a
/// wizard building, and the failure only shows up on the next launch.
///
/// <para>
/// A profile that fails to parse is moved aside rather than deleted or
/// overwritten. It is the only copy of that work, and a corrupt file someone
/// can send on is worth more than a clean slate.
/// </para>
/// </remarks>
public sealed class ProfileStore : IProfileStore
{
    private readonly IAppPaths _paths;
    private readonly ILogger<ProfileStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Serialisation shared by load and save, so a round trip is symmetric.</summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Constructs the store.</summary>
    public ProfileStore(IAppPaths paths, ILogger<ProfileStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DispatchProfile> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var path = _paths.ProfileFile;
            if (!File.Exists(path))
            {
                return new DispatchProfile();
            }

            await using var stream = File.OpenRead(path);
            var profile = await JsonSerializer
                .DeserializeAsync<DispatchProfile>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            return profile is null ? new DispatchProfile() : Migrate(profile);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Profile is not valid JSON; setting it aside");
            QuarantineCorruptProfile();
            return new DispatchProfile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable is not the same as absent, so nothing is overwritten.
            _logger.LogError(ex, "Could not read the profile");
            return new DispatchProfile();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(DispatchProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _paths.EnsureCreated();

            var path = _paths.ProfileFile;
            var temp = path + ".tmp";

            await using (var stream = File.Create(temp))
            {
                await JsonSerializer
                    .SerializeAsync(
                        stream,
                        profile with { SchemaVersion = DispatchProfile.CurrentSchemaVersion },
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);

                // Flushed before the move, or the rename can beat the bytes to
                // disk and the atomicity is imaginary.
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Brings an older profile forward.
    /// </summary>
    /// <remarks>
    /// One version so far, so this only guards the shape. Each future bump adds
    /// a case here rather than a conditional inside the model.
    /// </remarks>
    private DispatchProfile Migrate(DispatchProfile profile)
    {
        if (profile.SchemaVersion == DispatchProfile.CurrentSchemaVersion)
        {
            return profile;
        }

        if (profile.SchemaVersion > DispatchProfile.CurrentSchemaVersion)
        {
            // Written by a newer build. Loading it would silently drop whatever
            // that build added, so it is left alone and reported.
            _logger.LogWarning(
                "Profile schema {Found} is newer than this build understands ({Known})",
                profile.SchemaVersion,
                DispatchProfile.CurrentSchemaVersion);

            return profile;
        }

        _logger.LogInformation(
            "Migrating profile from schema {From} to {To}",
            profile.SchemaVersion,
            DispatchProfile.CurrentSchemaVersion);

        return profile with { SchemaVersion = DispatchProfile.CurrentSchemaVersion };
    }

    private void QuarantineCorruptProfile()
    {
        try
        {
            var path = _paths.ProfileFile;
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var aside = $"{path}.corrupt-{stamp}";

            File.Move(path, aside, overwrite: false);
            _logger.LogInformation("Corrupt profile moved to {Path}", aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not set the corrupt profile aside");
        }
    }
}
