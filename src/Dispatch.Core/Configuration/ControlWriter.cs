using Dispatch.Core.Controls;
using Dispatch.Core.Resilience;

namespace Dispatch.Core.Configuration;

/// <summary>Writes a control scheme into the mod config files, and reads it back.</summary>
public interface IControlWriter
{
    /// <summary>
    /// Writes every binding into the file that holds it, backing each target up
    /// first, and returns exactly what moved.
    /// </summary>
    Task<ControlWriteResult> WriteAsync(
        string gamePath,
        IReadOnlyList<BoundAction> bindings,
        RunJournal? journal = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes what a write would change, against the files as they are on disk
    /// right now, without touching anything.
    /// </summary>
    Task<ControlWriteResult> PreviewAsync(
        string gamePath,
        IReadOnlyList<BoundAction> bindings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the bindings currently in the config files back into a scheme, so a
    /// value changed in-game can be reconciled with the stored profile.
    /// </summary>
    Task<IReadOnlyDictionary<string, KeyBinding>> ReadAsync(
        string gamePath,
        IReadOnlyList<GameAction> actions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores each config file that owns one of these actions from the <c>.bak</c>
    /// sibling the last write left, undoing an apply even after the app was closed.
    /// Returns the files put back, relative to the game folder.
    /// </summary>
    Task<IReadOnlyList<string>> RestoreBackupsAsync(
        string gamePath,
        IReadOnlyList<GameAction> actions,
        CancellationToken cancellationToken = default);
}

/// <summary>One value a write changed: where it is, and what it moved from and to.</summary>
/// <param name="File">The config file, relative to the game folder.</param>
/// <param name="Key">The key within it.</param>
/// <param name="OldValue">What was there before, or <c>(absent)</c> when the key was new.</param>
/// <param name="NewValue">What is there now.</param>
public sealed record ControlChange(string File, string Key, string OldValue, string NewValue);

/// <summary>The outcome of a write or a preview.</summary>
/// <param name="Changes">Every value that moved, in file then key order.</param>
/// <param name="MissingFiles">
/// Files that hold a binding but do not exist yet. Several mods write their config
/// on first game launch, so this is reported rather than treated as a failure.
/// </param>
/// <param name="Notes">Anything worth telling the user that is not itself a change.</param>
public sealed record ControlWriteResult(
    IReadOnlyList<ControlChange> Changes,
    IReadOnlyList<string> MissingFiles,
    IReadOnlyList<string> Notes)
{
    /// <summary>True when nothing needed to move.</summary>
    public bool IsEmpty => Changes.Count == 0;

    /// <summary>How many distinct files were touched.</summary>
    public int FilesTouched =>
        Changes.Select(c => c.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
}

/// <summary>
/// Writes control bindings into the mod config files that own them, editing each
/// value in place and never regenerating a file.
/// </summary>
/// <remarks>
/// The catalogue says, for every action, which file and key hold its binding and
/// which dialect that file speaks. This turns a scheme into edits: format each
/// binding into the file's own token language, change only the value, and leave
/// the comments, ordering and encoding that <see cref="IniDocument"/> preserves
/// exactly as they were.
///
/// <para>
/// Two safety properties matter. Every file is copied to a <c>.bak</c> sibling
/// before it is written, so a bad scheme is one restore away from undone. And a
/// file that does not exist yet is reported, not created — a mod that generates
/// its own config on first launch owns the structure of that file, and writing a
/// bare stub over it would lose whatever the mod intended to put there.
/// </para>
///
/// <para>
/// Where a binding carries a modifier — <c>Left Shift + X</c> — the modifier is
/// recorded in a companion <c>&lt;key&gt;Modifier</c> field. Reading is the exact
/// inverse, so a scheme survives a write-then-read round trip unchanged, which is
/// what lets "read settings from game" reconcile drift without inventing edits.
/// </para>
/// </remarks>
public sealed class ControlWriter : IControlWriter
{
    private const string Absent = "(absent)";
    private const string BackupSuffix = ".bak";

    /// <inheritdoc />
    public Task<ControlWriteResult> WriteAsync(
        string gamePath,
        IReadOnlyList<BoundAction> bindings,
        RunJournal? journal = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(gamePath, bindings, commit: true, journal, cancellationToken);

    /// <inheritdoc />
    public Task<ControlWriteResult> PreviewAsync(
        string gamePath,
        IReadOnlyList<BoundAction> bindings,
        CancellationToken cancellationToken = default) =>
        RunAsync(gamePath, bindings, commit: false, journal: null, cancellationToken);

    private static async Task<ControlWriteResult> RunAsync(
        string gamePath,
        IReadOnlyList<BoundAction> bindings,
        bool commit,
        RunJournal? journal,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentNullException.ThrowIfNull(bindings);

        var changes = new List<ControlChange>();
        var missing = new List<string>();
        var notes = new List<string>();

        // Grouped by file so each is loaded, backed up and saved once, however
        // many actions live in it.
        foreach (var group in bindings.GroupBy(b => b.Action.ConfigFile, StringComparer.OrdinalIgnoreCase))
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

            var fileChanges = new List<ControlChange>();
            foreach (var bound in group)
            {
                fileChanges.AddRange(ApplyToDocument(document, relative, bound));
            }

            if (fileChanges.Count == 0)
            {
                continue;
            }

            if (commit)
            {
                // Back up the on-disk state before this write, so the revert beside
                // the change undoes exactly this apply.
                File.Copy(path, path + BackupSuffix, overwrite: true);
                await document.SaveAsync(path, cancellationToken).ConfigureAwait(false);

                if (journal is not null)
                {
                    await JournalAsync(journal, group.First().Action.Plugin, path, fileChanges, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            changes.AddRange(fileChanges);
        }

        return new ControlWriteResult(
            changes.OrderBy(c => c.File, StringComparer.Ordinal).ThenBy(c => c.Key, StringComparer.Ordinal).ToList(),
            missing.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.Ordinal).ToList(),
            notes);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, KeyBinding>> ReadAsync(
        string gamePath,
        IReadOnlyList<GameAction> actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentNullException.ThrowIfNull(actions);

        var scheme = new Dictionary<string, KeyBinding>(StringComparer.Ordinal);

        foreach (var group in actions.GroupBy(a => a.ConfigFile, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Path.Combine(gamePath, group.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            var document = await IniDocument.LoadAsync(path, cancellationToken).ConfigureAwait(false);

            foreach (var action in group)
            {
                var raw = document.GetAnywhere(action.ConfigKey);
                if (raw is null)
                {
                    // The key is not in the file; leave the caller's stored value
                    // in place rather than inventing an Unbound reading.
                    continue;
                }

                var key = KeyTokens.Parse(raw, action.Dialect);
                var modifier = action.Device == InputDevice.Keyboard
                    ? ParseModifier(document.GetAnywhere(ModifierField(document, action.ConfigKey)))
                    : KeyModifier.None;

                scheme[action.Id] = new KeyBinding(key, modifier);
            }
        }

        return scheme;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> RestoreBackupsAsync(
        string gamePath,
        IReadOnlyList<GameAction> actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentNullException.ThrowIfNull(actions);

        var restored = new List<string>();

        foreach (var relative in actions.Select(a => a.ConfigFile).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Path.Combine(gamePath, relative.Replace('/', Path.DirectorySeparatorChar));
            var backup = path + BackupSuffix;

            // Only files with a backup from a prior apply can be put back; a file
            // never written has no .bak, and is left exactly as it is.
            if (!File.Exists(backup))
            {
                continue;
            }

            File.Copy(backup, path, overwrite: true);
            restored.Add(relative);
        }

        return Task.FromResult<IReadOnlyList<string>>(
            restored.OrderBy(f => f, StringComparer.Ordinal).ToList());
    }

    private static IReadOnlyList<ControlChange> ApplyToDocument(IniDocument document, string file, BoundAction bound)
    {
        var action = bound.Action;
        var changes = new List<ControlChange>();

        // Only ever change a key the mod actually has. Inserting a key the file
        // never defined does nothing in-game and leaves a dead line behind, so a
        // catalogue key that does not match this file is skipped, not appended.
        if (!document.HasAnywhere(action.ConfigKey))
        {
            return changes;
        }

        var mainValue = KeyTokens.Format(bound.Binding.Key, action.Dialect);
        var oldMain = document.GetAnywhere(action.ConfigKey);
        if (document.SetAnywhere(action.ConfigKey, mainValue))
        {
            changes.Add(new ControlChange(file, action.ConfigKey, oldMain ?? Absent, mainValue));
        }

        // Controller inputs are single buttons; they carry no modifier field.
        if (action.Device != InputDevice.Keyboard)
        {
            return changes;
        }

        var modKey = ModifierField(document, action.ConfigKey);
        var hasModifier = bound.Binding.Modifier != KeyModifier.None;
        var modExists = document.HasAnywhere(modKey);

        // Only touch the modifier field when there is a modifier to record, or one
        // already on disk that this binding no longer wants. Never add a "None"
        // modifier line to a file that never had one.
        if (!hasModifier && !modExists)
        {
            return changes;
        }

        var newMod = ModifierToFile(bound.Binding.Modifier);
        var oldMod = document.GetAnywhere(modKey);
        if (document.SetAnywhere(modKey, newMod))
        {
            changes.Add(new ControlChange(file, modKey, oldMod ?? Absent, newMod));
        }

        return changes;
    }

    private static async Task JournalAsync(
        RunJournal journal,
        string plugin,
        string path,
        IEnumerable<ControlChange> changes,
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

    /// <summary>
    /// The companion modifier field for a key, resolved against the file because
    /// these mods spell it two ways: LSPDFR's <c>PERFORM_ARREST_Key</c> pairs with
    /// <c>PERFORM_ARREST_ModifierKey</c> and Stop The Ped's <c>SearchKey</c> with
    /// <c>SearchModifierKey</c>, while Grammar Police's <c>InterfaceKey</c> pairs with
    /// <c>InterfaceModifier</c> (no trailing "Key"). Whichever the file actually has
    /// is used; the <c>…ModifierKey</c> form is the default when it has neither.
    /// </summary>
    private static string ModifierField(IniDocument document, string configKey)
    {
        var baseName = configKey.EndsWith("Key", StringComparison.Ordinal)
            ? configKey[..^3]
            : configKey;

        var withKey = baseName + "ModifierKey";
        var without = baseName + "Modifier";

        if (document.HasAnywhere(withKey))
        {
            return withKey;
        }

        return document.HasAnywhere(without) ? without : withKey;
    }

    private static string ModifierToFile(KeyModifier modifier)
    {
        if (modifier == KeyModifier.None)
        {
            return "None";
        }

        // WinForms modifier tokens — the form every one of these config files reads,
        // e.g. LSPDFR's TRAFFICSTOP_START_Key=LShiftKey and Compulite's
        // GiveCitationModifierKey=LControlKey. "Left Shift" would not parse.
        var parts = new List<string>(3);
        if (modifier.HasFlag(KeyModifier.Control)) { parts.Add("LControlKey"); }
        if (modifier.HasFlag(KeyModifier.Shift)) { parts.Add("LShiftKey"); }
        if (modifier.HasFlag(KeyModifier.Alt)) { parts.Add("LMenu"); }

        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Reads a modifier field's raw value into flags, tolerant of every spelling
    /// these files use. Shared with the scanner so a discovered keybind folds in its
    /// companion modifier exactly as a write would emit it.
    /// </summary>
    internal static KeyModifier ParseModifier(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return KeyModifier.None;
        }

        var value = raw.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (value is "none" or "0")
        {
            return KeyModifier.None;
        }

        var modifier = KeyModifier.None;

        // Tolerant of every spelling these files use for a modifier: "Left Shift",
        // "LeftShift", "Shift" and the WinForms "LShiftKey" all resolve the same.
        if (value.Contains("shift", StringComparison.Ordinal)) { modifier |= KeyModifier.Shift; }
        if (value.Contains("control", StringComparison.Ordinal) || value.Contains("ctrl", StringComparison.Ordinal)) { modifier |= KeyModifier.Control; }
        if (value.Contains("alt", StringComparison.Ordinal) || value.Contains("menu", StringComparison.Ordinal)) { modifier |= KeyModifier.Alt; }

        return modifier;
    }
}
