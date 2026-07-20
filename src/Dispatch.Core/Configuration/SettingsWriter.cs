using Dispatch.Core.Resilience;

namespace Dispatch.Core.Configuration;

/// <summary>A setting paired with the exact value to write for it.</summary>
/// <param name="Setting">The setting being written.</param>
/// <param name="Raw">The literal to put in the file, already quoted if the setting wants quotes.</param>
public sealed record SettingValue(ModSetting Setting, string Raw);

/// <summary>One value a settings write changed.</summary>
/// <param name="File">The config file, relative to the game folder.</param>
/// <param name="Key">The key within it.</param>
/// <param name="OldValue">What was there before, or <c>(absent)</c> when new.</param>
/// <param name="NewValue">What is there now.</param>
public sealed record SettingChangeRecord(string File, string Key, string OldValue, string NewValue);

/// <summary>The outcome of a settings write or preview.</summary>
/// <param name="Changes">Every value that moved.</param>
/// <param name="MissingFiles">Files that hold a setting but do not exist yet.</param>
public sealed record SettingsWriteResult(
    IReadOnlyList<SettingChangeRecord> Changes,
    IReadOnlyList<string> MissingFiles)
{
    /// <summary>True when nothing needed to move.</summary>
    public bool IsEmpty => Changes.Count == 0;

    /// <summary>How many distinct files were touched.</summary>
    public int FilesTouched =>
        Changes.Select(c => c.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
}

/// <summary>Writes plugin settings into the config files that own them, and reads them back.</summary>
public interface ISettingsWriter
{
    /// <summary>Writes each value into its file, backing every target up first.</summary>
    Task<SettingsWriteResult> WriteAsync(
        string gamePath,
        IReadOnlyList<SettingValue> values,
        RunJournal? journal = null,
        CancellationToken cancellationToken = default);

    /// <summary>Computes what a write would change without touching anything.</summary>
    Task<SettingsWriteResult> PreviewAsync(
        string gamePath,
        IReadOnlyList<SettingValue> values,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the current raw value of each setting from disk. Absent keys and
    /// missing files are simply left out, so the caller falls back to defaults.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ReadAsync(
        string gamePath,
        IReadOnlyList<ModSetting> settings,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes plugin settings into their config files, editing each value in place
/// and never regenerating a file.
/// </summary>
/// <remarks>
/// The same discipline as <see cref="ControlWriter"/>: back every file up to a
/// <c>.bak</c> before it is touched, change only the value, report a file that
/// does not exist rather than inventing one, and leave the comments and encoding
/// exactly as they were. A mod update that adds keys therefore survives, and a
/// bad edit is one restore away from undone.
/// </remarks>
public sealed class SettingsWriter : ISettingsWriter
{
    private const string Absent = "(absent)";
    private const string BackupSuffix = ".bak";

    /// <inheritdoc />
    public Task<SettingsWriteResult> WriteAsync(
        string gamePath,
        IReadOnlyList<SettingValue> values,
        RunJournal? journal = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(gamePath, values, commit: true, journal, cancellationToken);

    /// <inheritdoc />
    public Task<SettingsWriteResult> PreviewAsync(
        string gamePath,
        IReadOnlyList<SettingValue> values,
        CancellationToken cancellationToken = default) =>
        RunAsync(gamePath, values, commit: false, journal: null, cancellationToken);

    private static async Task<SettingsWriteResult> RunAsync(
        string gamePath,
        IReadOnlyList<SettingValue> values,
        bool commit,
        RunJournal? journal,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentNullException.ThrowIfNull(values);

        var changes = new List<SettingChangeRecord>();
        var missing = new List<string>();

        foreach (var group in values.GroupBy(v => v.Setting.ConfigFile, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = group.Key;
            var path = Path.Combine(gamePath, relative.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
            {
                missing.Add(relative);
                continue;
            }

            var document = await IniDocument.LoadAsync(path, cancellationToken).ConfigureAwait(false);

            var fileChanges = new List<SettingChangeRecord>();
            foreach (var value in group)
            {
                var old = Read(document, value.Setting);
                if (Write(document, value.Setting, value.Raw))
                {
                    fileChanges.Add(new SettingChangeRecord(relative, value.Setting.ConfigKey, old ?? Absent, value.Raw));
                }
            }

            if (fileChanges.Count == 0)
            {
                continue;
            }

            if (commit)
            {
                File.Copy(path, path + BackupSuffix, overwrite: true);
                await document.SaveAsync(path, cancellationToken).ConfigureAwait(false);

                if (journal is not null)
                {
                    await JournalAsync(journal, group.First().Setting.Plugin, path, fileChanges, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            changes.AddRange(fileChanges);
        }

        return new SettingsWriteResult(
            changes.OrderBy(c => c.File, StringComparer.Ordinal).ThenBy(c => c.Key, StringComparer.Ordinal).ToList(),
            missing.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.Ordinal).ToList());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(
        string gamePath,
        IReadOnlyList<ModSetting> settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentNullException.ThrowIfNull(settings);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in settings.GroupBy(s => s.ConfigFile, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Path.Combine(gamePath, group.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            var document = await IniDocument.LoadAsync(path, cancellationToken).ConfigureAwait(false);

            foreach (var setting in group)
            {
                var raw = Read(document, setting);
                if (raw is not null)
                {
                    values[setting.Id] = raw;
                }
            }
        }

        return values;
    }

    // A curated setting has no section and matches its key anywhere in the file;
    // a scanned setting carries the real section so a duplicated key edits the
    // right one.
    private static string? Read(IniDocument document, ModSetting setting) =>
        string.IsNullOrEmpty(setting.Section)
            ? document.GetAnywhere(setting.ConfigKey)
            : document.Get(setting.Section, setting.ConfigKey);

    private static bool Write(IniDocument document, ModSetting setting, string raw) =>
        string.IsNullOrEmpty(setting.Section)
            ? document.SetAnywhere(setting.ConfigKey, raw)
            : document.Set(setting.Section, setting.ConfigKey, raw);

    private static async Task JournalAsync(
        RunJournal journal,
        string plugin,
        string path,
        IEnumerable<SettingChangeRecord> changes,
        CancellationToken cancellationToken)
    {
        foreach (var change in changes)
        {
            var seq = await journal.BeginAsync(
                new JournalEntry
                {
                    Seq = 0,
                    Op = JournalOp.Configure,
                    Mod = plugin,
                    Dst = path,
                    Key = change.Key,
                    OldValue = change.OldValue,
                    NewValue = change.NewValue,
                },
                cancellationToken).ConfigureAwait(false);

            await journal.CompleteAsync(seq, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
