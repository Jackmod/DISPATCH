using Dispatch.Core.Detection;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Maintenance;
using Dispatch.Core.Profiles;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Maintenance;

/// <summary>
/// The auditor's job is to tell three states apart that look identical from a
/// directory listing, and to name the launcher-verification cause directly when
/// it sees its fingerprint. That naming is the difference between a five-minute
/// fix and a reinstall.
/// </summary>
public sealed class IntegrityAuditorTests : IDisposable
{
    private readonly string _game =
        Path.Combine(Path.GetTempPath(), "dispatch-audit", Guid.NewGuid().ToString("N"));

    private readonly IntegrityAuditor _auditor = new();

    public IntegrityAuditorTests() => Directory.CreateDirectory(_game);

    public void Dispose()
    {
        try { if (Directory.Exists(_game)) Directory.Delete(_game, recursive: true); }
        catch (IOException) { }
    }

    private async Task<PlacedFile> GivenPlaced(string relative, string content, string mod = "mod")
    {
        var full = Path.Combine(_game, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content);
        return new PlacedFile(relative, await Hashing.Sha256Async(full), mod);
    }

    private static InstallRecord Record(IEnumerable<PlacedFile> files, string build = "1.0.3725") =>
        new() { GameBuild = build, PresetId = "full-duty", ModIds = ["lspdfr"], Files = files.ToList() };

    [Fact]
    public async Task An_empty_record_reports_nothing_installed()
    {
        var report = await _auditor.AuditAsync(_game, new InstallRecord(), "1.0.3725");

        report.IsHealthy.Should().BeTrue();
        report.Findings.Should().Contain(f => f.Title.Contains("Nothing installed"));
    }

    [Fact]
    public async Task An_intact_install_is_healthy()
    {
        var files = new[]
        {
            await GivenPlaced("plugins/StopThePed.dll", "a"),
            await GivenPlaced("scripts/LemonUI.dll", "b"),
        };

        var report = await _auditor.AuditAsync(_game, Record(files), "1.0.3725");

        report.IsHealthy.Should().BeTrue();
        report.Verdict.Should().Be("All good");
    }

    [Fact]
    public async Task A_single_missing_file_is_a_problem_but_not_the_wholesale_message()
    {
        var files = new[]
        {
            await GivenPlaced("a.dll", "a"),
            await GivenPlaced("b.dll", "b"),
            await GivenPlaced("c.dll", "c"),
            await GivenPlaced("d.dll", "d"),
        };
        File.Delete(Path.Combine(_game, "a.dll"));

        var report = await _auditor.AuditAsync(_game, Record(files), "1.0.3725");

        report.Worst.Should().Be(AuditSeverity.Problem);
        report.Findings.Should().Contain(f => f.Title.Contains("missing or changed"));
        report.Findings.Should().NotContain(f => f.Title.Contains("removed"));
    }

    [Fact]
    public async Task Most_files_gone_names_the_launcher_verification_cause()
    {
        // The fingerprint of Steam or Epic verifying the game files. Naming it
        // directly is the whole point.
        var files = new[]
        {
            await GivenPlaced("a.dll", "a"),
            await GivenPlaced("b.dll", "b"),
            await GivenPlaced("c.dll", "c"),
            await GivenPlaced("d.dll", "d"),
        };
        File.Delete(Path.Combine(_game, "a.dll"));
        File.Delete(Path.Combine(_game, "b.dll"));
        File.Delete(Path.Combine(_game, "c.dll"));

        var report = await _auditor.AuditAsync(_game, Record(files), "1.0.3725");

        var finding = report.Findings.Should().Contain(f => f.Title.Contains("removed")).Subject;
        finding.Detail.Should().Contain("Steam or Epic verified");
        finding.FixCommand.Should().Be("reinstall-from-cache");
    }

    [Fact]
    public async Task A_changed_hash_counts_as_altered()
    {
        var files = new[] { await GivenPlaced("thing.dll", "original") };
        await File.WriteAllTextAsync(Path.Combine(_game, "thing.dll"), "tampered");

        var report = await _auditor.AuditAsync(_game, Record(files), "1.0.3725");

        report.Worst.Should().Be(AuditSeverity.Problem);
    }

    [Fact]
    public async Task A_changed_game_build_is_the_direct_script_hook_message()
    {
        var files = new[] { await GivenPlaced("thing.dll", "x") };

        var report = await _auditor.AuditAsync(_game, Record(files, build: "1.0.3407"), "1.0.3725");

        var finding = report.Findings.Should().Contain(f => f.Title.Contains("Rockstar updated")).Subject;
        finding.Detail.Should().Contain("locked to the exact build");
        finding.Detail.Should().Contain("this is why LSPDFR stopped working");
    }

    [Fact]
    public async Task Findings_are_ordered_worst_first()
    {
        var files = new[] { await GivenPlaced("thing.dll", "x") };
        File.Delete(Path.Combine(_game, "thing.dll"));

        var report = await _auditor.AuditAsync(_game, Record(files, build: "1.0.3407"), "1.0.3725");

        report.Findings[0].Severity.Should().Be(AuditSeverity.Problem);
    }
}

/// <summary>Compatibility is deliberately narrow: only build-locked things track the build.</summary>
public sealed class CompatibilityCheckerTests
{
    [Fact]
    public void A_matching_script_hook_build_is_ok()
    {
        CompatibilityChecker.CheckScriptHook("1.0.3725", "1.0.3725")
            .Verdict.Should().Be(Compatibility.Ok);
    }

    [Fact]
    public void A_mismatched_script_hook_build_is_incompatible_and_says_why()
    {
        var finding = CompatibilityChecker.CheckScriptHook("1.0.3725", "1.0.3407");

        finding.Verdict.Should().Be(Compatibility.Incompatible);
        finding.Detail.Should().Contain("locked to the exact build");
    }

    [Fact]
    public void Checking_an_archive_before_install_gives_the_useful_wording()
    {
        var finding = CompatibilityChecker.CheckArchiveBeforeInstall("1.0.3725", "1.0.3407");

        finding.Detail.Should().Contain("archive").And.Contain("won't load");
    }

    [Fact]
    public void An_unreadable_version_is_unknown_not_a_false_alarm()
    {
        CompatibilityChecker.CheckScriptHook(null, "1.0.3725").Verdict.Should().Be(Compatibility.Unknown);
    }

    [Fact]
    public void Only_build_locked_components_track_the_game_build()
    {
        CompatibilityChecker.TracksGameBuild(Dispatch.Core.Catalogue.CompatibilityAnchor.GameBuild).Should().BeTrue();
        CompatibilityChecker.TracksGameBuild(Dispatch.Core.Catalogue.CompatibilityAnchor.LspdfrApi).Should().BeFalse();
    }
}

/// <summary>Log translation turns the log's own phrasing into plain findings.</summary>
public sealed class GameLogReaderTests
{
    private readonly GameLogReader _reader = new();

    [Fact]
    public void A_missing_dependency_is_translated()
    {
        const string log = "[ERROR] GrammarPolice could not be loaded because RageNativeUI is missing";

        var findings = _reader.Translate(log);

        findings.Should().ContainSingle();
        findings[0].Title.Should().Contain("GrammarPolice");
        findings[0].Explanation.Should().Contain("RageNativeUI");
        findings[0].FixCommand.Should().Be("install-dependency");
    }

    [Fact]
    public void A_script_hook_version_mismatch_is_translated()
    {
        const string log = "Unsupported game version! Script Hook V requires an update.";

        var findings = _reader.Translate(log);

        findings.Should().Contain(f => f.Title.Contains("Script Hook V"));
    }

    [Fact]
    public void The_same_failure_repeated_is_reported_once()
    {
        var log = string.Join("\n", Enumerable.Repeat(
            "[ERROR] GrammarPolice could not be loaded because RageNativeUI is missing", 20));

        _reader.Translate(log).Should().ContainSingle("a repeated failure is one problem");
    }

    [Fact]
    public void A_clean_log_yields_no_findings()
    {
        const string log = "[INFO] LSPDFR loaded successfully.\n[INFO] On duty.";

        _reader.Translate(log).Should().BeEmpty();
    }

    [Fact]
    public void A_missing_assembly_is_translated()
    {
        const string log = "System.IO.FileNotFoundException: Could not load file or assembly 'RAGENativeUI'";

        var findings = _reader.Translate(log);

        findings.Should().Contain(f => f.Explanation.Contains("antivirus"));
    }
}
