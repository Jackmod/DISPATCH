using System.Text.Json;
using Dispatch.Core.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.Core.Profiles;

/// <summary>Reads and writes the officer's career record.</summary>
public interface IProfileStatsStore
{
    /// <summary>Loads the stats, returning an empty record when none exists.</summary>
    Task<ProfileStats> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the stats, atomically.</summary>
    Task SaveAsync(ProfileStats stats, CancellationToken cancellationToken = default);

    /// <summary>Sets the profile picture path and returns the updated record.</summary>
    Task<ProfileStats> SetAvatarAsync(string? avatarPath, CancellationToken cancellationToken = default);

    /// <summary>Appends a session and returns the updated record.</summary>
    Task<ProfileStats> RecordSessionAsync(SessionStat session, CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON-backed career storage at <c>profile-stats.json</c>.
/// </summary>
/// <remarks>
/// Same atomic temp-then-move as <see cref="ProfileStore"/>, and just as forgiving
/// on read: an absent or unreadable file is an empty career, never an error,
/// because a corrupt stats file must not take the profile screen down. The first
/// load stamps <see cref="ProfileStats.FirstSeen"/>, so "on the force since" is set
/// the first time the app is opened rather than needing a separate onboarding step.
/// </remarks>
public sealed class ProfileStatsStore : IProfileStatsStore
{
    private const string FileName = "profile-stats.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<ProfileStatsStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Constructs the store.</summary>
    public ProfileStatsStore(IAppPaths paths, ILogger<ProfileStatsStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _paths = paths;
        _logger = logger ?? NullLogger<ProfileStatsStore>.Instance;
    }

    private string Path => System.IO.Path.Combine(_paths.Root, FileName);

    /// <inheritdoc />
    public async Task<ProfileStats> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(ProfileStats stats, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stats);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveUnlockedAsync(stats, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ProfileStats> SetAvatarAsync(string? avatarPath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stats = (await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false)) with { AvatarPath = avatarPath };
            await SaveUnlockedAsync(stats, cancellationToken).ConfigureAwait(false);
            return stats;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ProfileStats> RecordSessionAsync(SessionStat session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var stats = current with { Sessions = [.. current.Sessions, session] };
            await SaveUnlockedAsync(stats, cancellationToken).ConfigureAwait(false);
            return stats;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ProfileStats> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            // First run: stamp the join date so "on the force since" is real.
            var seeded = new ProfileStats { FirstSeen = DateTimeOffset.UtcNow };
            await SaveUnlockedAsync(seeded, cancellationToken).ConfigureAwait(false);
            return seeded;
        }

        try
        {
            await using var stream = File.OpenRead(Path);
            var stats = await JsonSerializer.DeserializeAsync<ProfileStats>(stream, Json, cancellationToken)
                .ConfigureAwait(false);
            return stats ?? new ProfileStats { FirstSeen = DateTimeOffset.UtcNow };
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Profile stats at {Path} could not be read", Path);
            return new ProfileStats { FirstSeen = DateTimeOffset.UtcNow };
        }
    }

    private async Task SaveUnlockedAsync(ProfileStats stats, CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();

        var temp = Path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                stats with { SchemaVersion = ProfileStats.CurrentSchemaVersion },
                Json,
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, Path, overwrite: true);
    }
}
