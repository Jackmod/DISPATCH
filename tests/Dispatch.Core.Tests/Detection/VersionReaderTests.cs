using Dispatch.Core.Detection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Detection;

/// <summary>
/// The version reader against a fixture folder. Reading a real version resource
/// needs a real signed binary, which a test cannot conjure, so these cover the
/// parts that are logic rather than P/Invoke: mod-file detection, and the
/// graceful handling of a folder with nothing to read.
/// </summary>
public sealed class VersionReaderTests : IDisposable
{
    private readonly string _game =
        Path.Combine(Path.GetTempPath(), "dispatch-version", Guid.NewGuid().ToString("N"));

    private readonly VersionReader _reader = new(NullLogger<VersionReader>.Instance);

    public VersionReaderTests() => Directory.CreateDirectory(_game);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_game))
            {
                Directory.Delete(_game, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private void Given(string relativePath)
    {
        var full = Path.Combine(_game, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    [Fact]
    public void A_clean_folder_reports_no_mod_files()
    {
        Given("GTA5.exe");

        _reader.FindModFiles(_game).Should().BeEmpty();
        _reader.Read(_game).HasModFiles.Should().BeFalse();
    }

    [Fact]
    public void An_injection_dll_marks_the_folder_as_modded()
    {
        Given("dinput8.dll");
        Given("ScriptHookV.dll");

        var found = _reader.FindModFiles(_game);

        found.Should().Contain("dinput8.dll").And.Contain("ScriptHookV.dll");
        _reader.Read(_game).HasModFiles.Should().BeTrue();
    }

    [Fact]
    public void A_plugins_folder_marks_the_folder_as_modded()
    {
        Given("plugins/LSPDFR/anything.dll");

        _reader.FindModFiles(_game).Should().Contain("plugins/");
    }

    [Fact]
    public void Mod_files_are_named_specifically_so_the_screen_can_say_what_it_found()
    {
        // The locate screen promises to name what it found rather than warn
        // vaguely, so the reader has to return the actual filenames.
        Given("ScriptHookVDotNet.asi");
        Given("RagePluginHook.exe");

        _reader.FindModFiles(_game).Should().BeEquivalentTo("ScriptHookVDotNet.asi", "RagePluginHook.exe");
    }

    [Fact]
    public void Reading_a_folder_with_no_executable_returns_an_unreadable_build()
    {
        // A folder that is not really a game install must not pretend to have a
        // build number.
        var versions = _reader.Read(_game);

        versions.GameBuild.Should().BeNull();
        versions.IsGameReadable.Should().BeFalse();
    }

    [Fact]
    public void Reading_a_missing_file_version_returns_null_rather_than_throwing()
    {
        _reader.ReadFileVersion(Path.Combine(_game, "nope.dll")).Should().BeNull();
    }

    [Fact]
    public void The_mod_marker_list_covers_the_core_injection_files()
    {
        // These are the files antivirus removes and installs depend on; if the
        // list loses one, an already-modified folder reads as clean.
        VersionReader.ModMarkers.Should()
            .Contain("dinput8.dll")
            .And.Contain("ScriptHookV.dll")
            .And.Contain("RagePluginHook.exe");
    }

    [Fact]
    public void GTA5_exe_marks_the_folder_as_the_supported_Legacy_edition()
    {
        Given("GTA5.exe");

        _reader.ReadEdition(_game).Should().Be(GameEdition.Legacy);
        _reader.Read(_game).IsLegacy.Should().BeTrue();
    }

    [Fact]
    public void The_Enhanced_executable_marks_the_folder_as_Enhanced()
    {
        // Enhanced ships a different executable and none of the mod stack runs on
        // it, so it must be recognised as Enhanced rather than mistaken for Legacy.
        Given("GTA5_Enhanced.exe");

        _reader.ReadEdition(_game).Should().Be(GameEdition.Enhanced);
        _reader.Read(_game).IsLegacy.Should().BeFalse();
    }

    [Fact]
    public void A_folder_with_neither_executable_has_an_unknown_edition()
    {
        _reader.ReadEdition(_game).Should().Be(GameEdition.Unknown);
    }

    [Fact]
    public void Edition_names_are_the_words_users_recognise()
    {
        VersionReader.EditionName(GameEdition.Legacy).Should().Be("Legacy");
        VersionReader.EditionName(GameEdition.Enhanced).Should().Be("Enhanced");
    }
}
