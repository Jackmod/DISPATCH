using Dispatch.Core.Configuration;
using Dispatch.Core.Controls;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Configuration;

/// <summary>
/// The writer turns a control scheme into edits to the mod config files. The
/// promises that matter are: it writes in each file's own token dialect, it backs
/// every target up before touching it, a scheme survives a write-then-read round
/// trip unchanged, and a file that does not exist yet is reported rather than
/// invented. Each is asserted against a throwaway fixture folder.
/// </summary>
public sealed class ControlWriterTests : IDisposable
{
    private readonly string _root;
    private readonly ControlWriter _writer = new();

    public ControlWriterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dispatch-cw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>The catalogue actions used across these tests, one per dialect.</summary>
    private static readonly string[] Ids =
    [
        "lspdfr.arrest",       // WinForms letter
        "stp.transport",       // Bare number-row digit (D9 -> "9")
        "compulite.citation",  // Bare + a Shift modifier
        "ci.alpr",             // Bare + a Control modifier
        "pad.stop",            // controller input
    ];

    private IReadOnlyList<BoundAction> Subset() =>
        ControlCatalogue.Bind(ControlCatalogue.Suggested)
            .Where(b => Ids.Contains(b.Action.Id))
            .ToList();

    // Seeds each mod's file with the real key the action targets, so the writer —
    // which only ever changes a key that already exists — has something to edit.
    private void CreateFilesFor(IEnumerable<BoundAction> bindings)
    {
        foreach (var group in bindings.GroupBy(b => b.Action.ConfigFile, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(_root, group.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var lines = new List<string> { "; fixture config" };
            foreach (var bound in group)
            {
                lines.Add($"{bound.Action.ConfigKey} = PLACEHOLDER   ; the bind");
            }

            File.WriteAllText(path, string.Join('\n', lines) + "\n");
        }
    }

    [Fact]
    public async Task A_scheme_survives_a_write_then_read_round_trip()
    {
        var bindings = Subset();
        CreateFilesFor(bindings);

        await _writer.WriteAsync(_root, bindings);
        var readBack = await _writer.ReadAsync(_root, bindings.Select(b => b.Action).ToList());

        foreach (var bound in bindings)
        {
            readBack.Should().ContainKey(bound.Action.Id);
            readBack[bound.Action.Id].Should().Be(
                bound.Binding,
                "the binding written for {0} must read back unchanged", bound.Action.Id);
        }
    }

    [Fact]
    public async Task Each_written_file_is_backed_up_first()
    {
        var bindings = Subset();
        CreateFilesFor(bindings);

        var keys = Path.Combine(_root, "lspdfr", "keys.ini");
        var before = await File.ReadAllTextAsync(keys);

        await _writer.WriteAsync(_root, bindings);

        var backup = keys + ".bak";
        File.Exists(backup).Should().BeTrue("every target is copied to a .bak before it is written");
        (await File.ReadAllTextAsync(backup)).Should().Be(before, "the backup is the pre-write state");
    }

    [Fact]
    public async Task Bindings_are_written_in_each_files_own_dialect()
    {
        var bindings = Subset();
        CreateFilesFor(bindings);

        await _writer.WriteAsync(_root, bindings);

        // Stop The Ped keeps the WinForms number-row spelling: D9 stays "D9".
        var stp = await IniDocument.LoadAsync(Path.Combine(_root, "plugins", "LSPDFR", "StopThePed.ini"));
        stp.GetAnywhere("CallTransportKey").Should().Be("D9");

        // Compulite's citation is Left Shift + X; the modifier lands in its own
        // companion field, in the WinForms token form the mod reads.
        var compulite = await IniDocument.LoadAsync(Path.Combine(_root, "plugins", "LSPDFR", "CompuLite.ini"));
        compulite.GetAnywhere("GiveCitationKey").Should().Be("X");
        compulite.GetAnywhere("GiveCitationModifierKey").Should().Be("LShiftKey");
    }

    [Fact]
    public async Task A_missing_file_is_reported_rather_than_created()
    {
        var bindings = Subset();
        // Deliberately create nothing: every file is missing.

        var result = await _writer.WriteAsync(_root, bindings);

        result.Changes.Should().BeEmpty();
        result.MissingFiles.Should().Contain("lspdfr/keys.ini");
        File.Exists(Path.Combine(_root, "lspdfr", "keys.ini")).Should().BeFalse();
    }

    [Fact]
    public async Task A_second_apply_with_no_edits_changes_nothing()
    {
        var bindings = Subset();
        CreateFilesFor(bindings);

        await _writer.WriteAsync(_root, bindings);
        var second = await _writer.WriteAsync(_root, bindings);

        second.IsEmpty.Should().BeTrue("re-applying an unchanged scheme is a no-op");
    }

    [Fact]
    public async Task Preview_reports_the_same_changes_without_touching_disk()
    {
        var bindings = Subset();
        CreateFilesFor(bindings);

        var keysPath = Path.Combine(_root, "lspdfr", "keys.ini");
        var before = await File.ReadAllTextAsync(keysPath);

        var preview = await _writer.PreviewAsync(_root, bindings);

        preview.Changes.Should().NotBeEmpty();
        (await File.ReadAllTextAsync(keysPath)).Should().Be(before, "a preview must not write");
        File.Exists(keysPath + ".bak").Should().BeFalse("a preview must not back anything up");
    }

    [Fact]
    public async Task An_in_game_change_is_read_back_as_a_difference()
    {
        var bindings = Subset();
        CreateFilesFor(bindings);
        await _writer.WriteAsync(_root, bindings);

        // Simulate a mod's in-game menu rewriting its own ini.
        var stpPath = Path.Combine(_root, "plugins", "LSPDFR", "StopThePed.ini");
        var stp = await IniDocument.LoadAsync(stpPath);
        stp.SetAnywhere("CallTransportKey", "D8");
        await stp.SaveAsync(stpPath);

        var readBack = await _writer.ReadAsync(_root, bindings.Select(b => b.Action).ToList());

        readBack["stp.transport"].Key.Canonical.Should().Be("D8", "the read must reflect what is on disk now");
    }

    [Fact]
    public async Task Restoring_backups_puts_the_pre_write_files_back()
    {
        var bindings = Subset();
        CreateFilesFor(bindings);

        var keysPath = Path.Combine(_root, "lspdfr", "keys.ini");
        var original = await File.ReadAllTextAsync(keysPath);

        await _writer.WriteAsync(_root, bindings);
        (await File.ReadAllTextAsync(keysPath)).Should().NotBe(original, "the write changed the file");

        var restored = await _writer.RestoreBackupsAsync(_root, bindings.Select(b => b.Action).ToList());

        restored.Should().Contain("lspdfr/keys.ini");
        (await File.ReadAllTextAsync(keysPath)).Should().Be(original, "restore returns the pre-write content");
    }

    [Fact]
    public async Task Restoring_with_no_backups_present_restores_nothing()
    {
        var bindings = Subset();
        CreateFilesFor(bindings);
        // No write has happened, so there are no .bak files.

        var restored = await _writer.RestoreBackupsAsync(_root, bindings.Select(b => b.Action).ToList());

        restored.Should().BeEmpty("there is no earlier apply to undo");
    }

    [Fact]
    public async Task Comments_in_a_config_file_survive_a_write()
    {
        var path = Path.Combine(_root, "plugins", "LSPDFR", "StopThePed.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "; keep me\nCallTransportKey = D5   ; the transport key\n");

        var transport = ControlCatalogue.Bind(ControlCatalogue.Suggested)
            .Where(b => b.Action.Id == "stp.transport")
            .ToList();

        await _writer.WriteAsync(_root, transport);

        var text = await File.ReadAllTextAsync(path);
        text.Should().Contain("; keep me");
        text.Should().Contain("CallTransportKey = D9   ; the transport key");
    }
}
