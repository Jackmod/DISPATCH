using Dispatch.Core.Maintenance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Maintenance;

/// <summary>
/// The cleaner is the most dangerous thing in the application. A bug here
/// deletes somebody's game, or their saves, so these tests are written first
/// and weighted toward what must NOT happen.
///
/// The scanner reads only — it cannot remove anything — so every test here is
/// about classification: what is offered, what is withheld, and what is
/// protected outright.
/// </summary>
public sealed class FolderCleanerTests : IDisposable
{
    private readonly string _game =
        Path.Combine(Path.GetTempPath(), "dispatch-cleaner", Guid.NewGuid().ToString("N"));

    public FolderCleanerTests() => Directory.CreateDirectory(_game);

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
            // A locked temp directory must not fail an otherwise passing run.
        }
    }

    private void Given(string relativePath, string content = "x")
    {
        var full = Path.Combine(_game, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static FolderCleaner Cleaner(params string[] known) =>
        new(NullLogger<FolderCleaner>.Instance, known);

    private CleanPlan Scan(params string[] known) => Cleaner(known).Scan(_game);

    // ===== What must never be offered ====================================

    [Fact]
    public void Stock_game_files_are_never_offered()
    {
        Given("GTA5.exe");
        Given("PlayGTAV.exe");
        Given("bink2w64.dll");
        Given("commonData.rpf");

        Scan().Candidates.Should().BeEmpty();
    }

    [Fact]
    public void Everything_inside_a_stock_folder_is_never_offered()
    {
        Given("update/update.rpf");
        Given("x64/audio/sfx/anything.rpf");
        Given("common/data/levels/gta5/vehicles.meta");

        Scan().Candidates.Should().BeEmpty();
    }

    [Theory]
    [InlineData("profiles/settings.xml")]
    [InlineData("Profiles/A1B2C3/pc_settings.bin")]
    [InlineData("savegames/SGTA50001")]
    [InlineData("User Music/track.mp3")]
    [InlineData("Rockstar Games/Social Club/data.dat")]
    [InlineData("Launcher Files/index.bin")]
    public void User_data_is_protected_outright(string path)
    {
        // These are not part of a stock install - they are created afterwards -
        // so a pure "is this stock?" test would happily offer to delete a
        // career. Protection is checked first and has no override.
        Given(path);

        var plan = Scan();

        plan.Candidates.Should().BeEmpty();
        plan.Protected.Should().ContainSingle();
    }

    [Theory]
    [InlineData("something.sav")]
    [InlineData("plugins/config.ini.bak")]
    public void Save_and_backup_extensions_are_protected(string path)
    {
        Given(path);

        Scan().Candidates.Should().BeEmpty();
    }

    [Fact]
    public void Protection_beats_being_inside_a_mod_folder()
    {
        // A save sitting under plugins/ is still a save. Order of checks is
        // what decides this, and getting it backwards loses data.
        Given("plugins/savegames/SGTA50001");

        var plan = Scan();

        plan.Candidates.Should().BeEmpty();
        plan.Protected.Should().ContainSingle();
    }

    [Fact]
    public void A_folder_merely_containing_a_protected_word_is_not_protected()
    {
        // "profiles-plus" is a mod, not the settings folder. Matching on
        // substrings rather than path segments would exempt it forever.
        Given("plugins/profiles-plus/thing.dll");

        Scan().Candidates.Should().ContainSingle();
    }

    // ===== Tiers ==========================================================

    [Fact]
    public void A_file_Dispatch_installed_is_Known()
    {
        Given("plugins/StopThePed.dll");

        var plan = Scan("plugins/StopThePed.dll");

        plan.Candidates.Should().ContainSingle();
        plan.Candidates[0].Tier.Should().Be(CleanTier.Known);
        plan.Candidates[0].IsPreselected.Should().BeTrue();
    }

    [Theory]
    [InlineData("plugins/Unknown.dll")]
    [InlineData("scripts/Whatever.dll")]
    [InlineData("lspdfr/audio/thing.awc")]
    public void Files_in_mod_folders_are_Likely(string path)
    {
        Given(path);

        var plan = Scan();

        plan.Candidates.Should().ContainSingle();
        plan.Candidates[0].Tier.Should().Be(CleanTier.Likely);
        plan.Candidates[0].IsPreselected.Should().BeTrue();
    }

    [Theory]
    [InlineData("dinput8.dll")]
    [InlineData("ScriptHookV.dll")]
    [InlineData("OpenIV.asi")]
    public void Recognised_mod_loaders_at_the_root_are_Likely(string path)
    {
        Given(path);

        Scan().Candidates[0].Tier.Should().Be(CleanTier.Likely);
    }

    [Theory]
    [InlineData("libcurl.dll")]         // stock / launcher library
    [InlineData("gpuperfapidx11-x64.dll")]
    [InlineData("discord-rpc.dll")]
    [InlineData("some-random-tool.dll")]
    public void Unrecognised_loose_files_at_the_root_are_Unknown_and_not_preselected(string path)
    {
        // The root mixes stock game/launcher libraries with mods, so an
        // unrecognised loose file there must never be ticked on the user's behalf.
        // This is the fix for the cleaner offering to delete libcurl.dll.
        Given(path);

        var plan = Scan();

        plan.Candidates[0].Tier.Should().Be(CleanTier.Unknown);
        plan.Candidates[0].IsPreselected.Should().BeFalse();
    }

    [Theory]
    [InlineData("BattlEye/beservice_x64.exe")]
    [InlineData("battleye/beclient_x64.dll")]
    public void BattlEye_is_stock_and_never_offered(string path)
    {
        // Anti-cheat. Removing it can break launch and GTA Online, so it must be
        // recognised as stock and not appear in the plan at all.
        Given(path);

        Scan().Candidates.Should().BeEmpty();
    }

    [Theory]
    [InlineData("holiday-photo.png")]
    [InlineData("notes.txt")]
    [InlineData("random/nested/thing.dat")]
    public void Anything_unrecognised_is_Unknown_and_never_preselected(string path)
    {
        // This is the tier most likely to hold something the user cares about,
        // so it is listed for a decision rather than ticked on their behalf.
        Given(path);

        var plan = Scan();

        plan.Candidates.Should().ContainSingle();
        plan.Candidates[0].Tier.Should().Be(CleanTier.Unknown);
        plan.Candidates[0].IsPreselected.Should().BeFalse();
    }

    [Fact]
    public void Every_candidate_explains_itself()
    {
        // The preview has to justify each row, or the user is confirming a
        // list they cannot evaluate.
        Given("plugins/Thing.dll");
        Given("mystery.dat");

        Scan().Candidates.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Reason));
    }

    // ===== The plan =======================================================

    [Fact]
    public void The_plan_counts_only_preselected_bytes()
    {
        Given("plugins/Known.dll", new string('x', 100));
        Given("mystery.dat", new string('x', 500));

        var plan = Scan("plugins/Known.dll");

        plan.PreselectedBytes.Should().Be(100, "the unknown file is not preselected");
    }

    [Fact]
    public void The_plan_groups_by_tier_for_the_preview_tree()
    {
        Given("plugins/Known.dll");
        Given("plugins/Other.dll");
        Given("mystery.dat");

        var plan = Scan("plugins/Known.dll");

        plan.ByTier[CleanTier.Known].Should().HaveCount(1);
        plan.ByTier[CleanTier.Likely].Should().HaveCount(1);
        plan.ByTier[CleanTier.Unknown].Should().HaveCount(1);
    }

    [Fact]
    public void Candidates_are_ordered_by_tier_so_the_safest_appear_first()
    {
        Given("mystery.dat");
        Given("plugins/Known.dll");
        Given("plugins/Other.dll");

        var plan = Scan("plugins/Known.dll");

        plan.Candidates.Select(c => c.Tier).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Scanning_reports_how_many_files_it_looked_at()
    {
        Given("GTA5.exe");
        Given("plugins/Thing.dll");
        Given("mystery.dat");

        Scan().FilesScanned.Should().Be(3, "stock files are examined even though they are not offered");
    }

    [Fact]
    public void An_empty_folder_produces_an_empty_plan()
    {
        var plan = Scan();

        plan.Candidates.Should().BeEmpty();
        plan.FilesScanned.Should().Be(0);
        plan.PreselectedBytes.Should().Be(0);
    }

    [Fact]
    public void Scanning_a_folder_that_does_not_exist_throws_rather_than_returning_nothing()
    {
        // An empty plan would read as "nothing to clean", which is a different
        // and much more dangerous statement than "that folder is not there".
        var cleaner = Cleaner();

        var scan = () => cleaner.Scan(Path.Combine(_game, "nope"));

        scan.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Cancellation_is_honoured()
    {
        for (var i = 0; i < 50; i++)
        {
            Given($"plugins/mod{i}.dll");
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var scan = () => Cleaner().Scan(_game, null, cancellation.Token);

        scan.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Path_separators_do_not_change_classification()
    {
        // The known-file list may be written with either separator.
        Given("plugins/StopThePed.dll");

        var plan = Cleaner(@"plugins\StopThePed.dll").Scan(_game);

        plan.Candidates[0].Tier.Should().Be(CleanTier.Known);
    }

    [Fact]
    public void Casing_does_not_change_classification()
    {
        // Windows filesystems are case-insensitive; a mod writing Gta5.exe
        // must not slip past the stock check.
        Given("gta5.EXE");

        Scan().Candidates.Should().BeEmpty();
    }

    [Fact]
    public void A_large_mixed_folder_classifies_everything_exactly_once()
    {
        Given("GTA5.exe");
        Given("update/update.rpf");
        Given("profiles/settings.xml");
        Given("plugins/StopThePed.dll");
        Given("plugins/Unknown.dll");
        Given("dinput8.dll");
        Given("holiday.png");

        var plan = Scan("plugins/StopThePed.dll");

        plan.FilesScanned.Should().Be(7);
        plan.Candidates.Should().HaveCount(4, "three are stock or protected");
        plan.Candidates.Select(c => c.RelativePath).Should().OnlyHaveUniqueItems();
        plan.Protected.Should().ContainSingle();
    }
}
