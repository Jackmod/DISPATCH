using Dispatch.Core.Detection;
using Dispatch.Core.Profiles;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Detection;

/// <summary>
/// The game-build watch: turning "Rockstar patched the game and Script Hook V
/// silently died" into a plain, detectable state. This is the failure behind
/// most "LSPDFR is broken" reports, so the comparison has to be exact and its
/// edge cases (no install, unreadable build) handled without crying wolf.
/// </summary>
public sealed class GameBuildWatchTests
{
    private static GameBuildWatch Build(InstallRecord? record, string? currentBuild)
    {
        var records = new StubRecordStore(record);
        var versions = new StubVersionReader(currentBuild);
        return new GameBuildWatch(records, versions, NullLogger<GameBuildWatch>.Instance);
    }

    private static InstallRecord Installed(string? build) => new()
    {
        GameBuild = build,
        PresetId = "full-duty",
        ModIds = ["scripthookv"],
        Files = [new PlacedFile("dinput8.dll", "sha256:x", "scripthookv")],
    };

    [Fact]
    public async Task Same_build_is_up_to_date()
    {
        var status = await Build(Installed("1.0.3889"), "1.0.3889").CheckAsync(@"C:\game");

        status.State.Should().Be(GameBuildState.UpToDate);
        status.NeedsUpdate.Should().BeFalse();
    }

    [Fact]
    public async Task A_changed_build_is_flagged_as_a_game_update()
    {
        var status = await Build(Installed("1.0.3889"), "1.0.3910").CheckAsync(@"C:\game");

        status.State.Should().Be(GameBuildState.GameUpdated);
        status.NeedsUpdate.Should().BeTrue();
        status.InstalledAgainst.Should().Be("1.0.3889");
        status.CurrentBuild.Should().Be("1.0.3910");
        status.Detail.Should().Contain("Script Hook V");
    }

    [Fact]
    public async Task No_install_record_means_nothing_to_compare()
    {
        var status = await Build(record: null, currentBuild: "1.0.3889").CheckAsync(@"C:\game");

        status.State.Should().Be(GameBuildState.NotInstalled);
        status.NeedsUpdate.Should().BeFalse();
    }

    [Fact]
    public async Task An_unreadable_build_does_not_cry_wolf()
    {
        // Build unreadable on either side must not be reported as a game update —
        // a false "your game updated" alarm would be worse than staying quiet.
        var missingCurrent = await Build(Installed("1.0.3889"), currentBuild: null).CheckAsync(@"C:\game");
        missingCurrent.State.Should().Be(GameBuildState.Unknown);
        missingCurrent.NeedsUpdate.Should().BeFalse();

        var missingRecorded = await Build(Installed(null), "1.0.3889").CheckAsync(@"C:\game");
        missingRecorded.State.Should().Be(GameBuildState.Unknown);
        missingRecorded.NeedsUpdate.Should().BeFalse();
    }

    private sealed class StubRecordStore(InstallRecord? record) : IInstallRecordStore
    {
        public Task<InstallRecord?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(record);
    }

    private sealed class StubVersionReader(string? build) : IVersionReader
    {
        public InstalledVersions Read(string gamePath) =>
            new(build, ScriptHookV: null, ScriptHookVDotNet: null, HasModFiles: false);

        public string? ReadFileVersion(string filePath) => null;

        public GameEdition ReadEdition(string gamePath) => GameEdition.Legacy;
    }
}
