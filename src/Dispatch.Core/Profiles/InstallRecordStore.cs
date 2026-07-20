using System.Text.Json;
using Dispatch.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Profiles;

/// <summary>Reads the install record written by the last run.</summary>
public interface IInstallRecordStore
{
    /// <summary>
    /// Loads the install record, or null when nothing has been installed or the
    /// file cannot be read.
    /// </summary>
    Task<InstallRecord?> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads the install record from <c>install-record.json</c>.
/// </summary>
/// <remarks>
/// Read-only and forgiving: a missing or corrupt file is "nothing installed"
/// rather than an error, because every caller — the auditor, the build watch, the
/// launcher — only ever asks "is there a record, and what does it say?", and a
/// broken file should degrade to "no record" rather than take a screen down.
/// </remarks>
public sealed class InstallRecordStore : IInstallRecordStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<InstallRecordStore> _logger;

    /// <summary>Constructs the store.</summary>
    public InstallRecordStore(IAppPaths paths, ILogger<InstallRecordStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InstallRecord?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = _paths.InstallRecordFile;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<InstallRecord>(stream, Json, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Install record at {Path} could not be read", path);
            return null;
        }
    }
}
