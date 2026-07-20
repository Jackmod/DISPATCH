using Dispatch.Core.Infrastructure;
using Dispatch.Core.Maintenance;
using Dispatch.Core.Profiles;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Maintenance;

/// <summary>
/// The uninstaller has to be safe by construction: the app-data wipe only ever
/// deletes Dispatch's own folders and never the game, and the return-to-stock only
/// touches files the install record names, restoring stock from backups and
/// removing new files only while they still match what Dispatch placed.
/// </summary>
public sealed class UninstallerTests : IDisposable
{
    private readonly string _base;

    public UninstallerTests()
    {
        // Deliberately neutral: the base must not contain "Dispatch" or the
        // guard's substring check would pass for the non-Dispatch folder test.
        _base = Path.Combine(Path.GetTempPath(), $"uninst-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_base);
    }

    public void Dispose()
    {
        if (Directory.Exists(_base))
        {
            Directory.Delete(_base, recursive: true);
        }
    }

    private sealed class FakeRecords(InstallRecord? record) : IInstallRecordStore
    {
        public Task<InstallRecord?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(record);
    }

    private Uninstaller Build(IAppPaths paths, InstallRecord? record = null)
    {
        var backups = new BackupStore(paths.BackupsDirectory, NullLogger<BackupStore>.Instance);
        var quarantine = new Quarantine(paths.QuarantineDirectory, NullLogger<Quarantine>.Instance);
        return new Uninstaller(paths, new FakeRecords(record), backups, quarantine);
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // ===== App-data wipe ======================================

    [Fact]
    public async Task Removing_app_data_wipes_dispatch_folders_but_never_the_game()
    {
        // Root paths that name Dispatch, as the real app's do.
        var paths = new AppPaths(Path.Combine(_base, "Dispatch"), Path.Combine(_base, "DispatchTemp"));
        Directory.CreateDirectory(paths.Root);
        Write(Path.Combine(paths.Root, "profile.json"), "{}");
        Write(Path.Combine(paths.StagingRoot, "scratch.tmp"), "x");

        // A game folder that is NOT under any Dispatch path.
        var game = Path.Combine(_base, "GTAV");
        Write(Path.Combine(game, "GTA5.exe"), "stock");
        Write(Path.Combine(game, "plugins", "StopThePed.dll"), "mod");

        var report = await Build(paths).RemoveAppDataAsync();

        Directory.Exists(paths.Root).Should().BeFalse("Dispatch's own data is wiped");
        Directory.Exists(paths.StagingRoot).Should().BeFalse();
        report.Count.Should().BeGreaterThan(0);

        // The game folder is untouched.
        Directory.Exists(game).Should().BeTrue();
        File.Exists(Path.Combine(game, "GTA5.exe")).Should().BeTrue();
        File.Exists(Path.Combine(game, "plugins", "StopThePed.dll")).Should().BeTrue();
    }

    [Fact]
    public async Task Removing_app_data_refuses_a_folder_that_is_not_a_dispatch_folder()
    {
        // Roots that do not name Dispatch must never be deleted.
        var paths = new AppPaths(Path.Combine(_base, "plain-root"), Path.Combine(_base, "plain-temp"));
        Directory.CreateDirectory(paths.Root);
        Write(Path.Combine(paths.Root, "important.txt"), "keep me");

        var report = await Build(paths).RemoveAppDataAsync();

        Directory.Exists(paths.Root).Should().BeTrue("a non-Dispatch folder is refused, not deleted");
        report.Skipped.Should().Contain(s => s.Contains("refused"));
    }

    // ===== Return to stock ====================================

    [Fact]
    public async Task Returning_to_stock_restores_overwrites_and_quarantines_new_files()
    {
        var paths = new AppPaths(Path.Combine(_base, "Dispatch"), Path.Combine(_base, "DispatchTemp"));
        var game = Path.Combine(_base, "GTAV");

        // An overwritten file: currently the mod version, with a stock backup.
        Write(Path.Combine(game, "plugins", "over.dll"), "MOD-VERSION");
        Write(Path.Combine(paths.BackupsDirectory, "run1", "plugins", "over.dll"), "STOCK-ORIGINAL");

        // A new mod file with no backup.
        Write(Path.Combine(game, "scripts", "new.dll"), "BRAND-NEW-MOD");

        // A file the user changed after install — must be left alone.
        Write(Path.Combine(game, "plugins", "user.dll"), "USER-EDITED-SINCE");

        var newHash = await Hashing.Sha256Async(Path.Combine(game, "scripts", "new.dll"));

        var record = new InstallRecord
        {
            GameBuild = "1.0.3725",
            ModIds = ["a", "b", "c"],
            Files =
            [
                new PlacedFile("plugins/over.dll", "any", "a"),
                new PlacedFile("scripts/new.dll", newHash, "b"),
                new PlacedFile("plugins/user.dll", "the-hash-it-was-placed-with", "c"),
            ],
        };

        var report = await Build(paths, record).ReturnGameToStockAsync(game);

        // Overwritten file is back to stock.
        (await File.ReadAllTextAsync(Path.Combine(game, "plugins", "over.dll"))).Should().Be("STOCK-ORIGINAL");
        report.FilesRestored.Should().Be(1);

        // New mod file removed (to quarantine).
        File.Exists(Path.Combine(game, "scripts", "new.dll")).Should().BeFalse();
        report.FilesRemoved.Should().Be(1);

        // User-edited file left exactly as it was.
        File.Exists(Path.Combine(game, "plugins", "user.dll")).Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(game, "plugins", "user.dll"))).Should().Be("USER-EDITED-SINCE");
    }

    [Fact]
    public async Task Returning_to_stock_with_nothing_installed_is_a_no_op()
    {
        var paths = new AppPaths(Path.Combine(_base, "Dispatch"), Path.Combine(_base, "DispatchTemp"));
        var game = Path.Combine(_base, "GTAV");
        Directory.CreateDirectory(game);

        var report = await Build(paths, record: null).ReturnGameToStockAsync(game);

        report.DidAnything.Should().BeFalse();
    }
}
