using Dispatch.Core.Infrastructure;
using Dispatch.Core.Profiles;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Profiles;

/// <summary>
/// The profile is the only copy of an identity and control scheme someone
/// spent a wizard building, so these tests care most about the paths where it
/// could be lost: a corrupt file, a partial write, a newer schema.
/// </summary>
public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dispatch-tests", Guid.NewGuid().ToString("N"));

    private readonly ProfileStore _store;
    private readonly AppPaths _paths;

    public ProfileStoreTests()
    {
        _paths = new AppPaths(_root, Path.Combine(_root, "temp"));
        _store = new ProfileStore(_paths, NullLogger<ProfileStore>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked temp directory must not fail an otherwise passing run.
        }
    }

    private static OfficerProfile Officer(string name = "J. Reyes") =>
        OfficerProfile.Create(name) with
        {
            Agency = Agency.Lssd,
            CallsignDivision = 2,
            CallsignPhonetic = "LINCOLN",
            CallsignBeat = 14,
            DepartmentName = "Blaine County Sheriff",
        };

    [Fact]
    public async Task Loading_before_anything_is_saved_returns_an_empty_profile()
    {
        var profile = await _store.LoadAsync();

        profile.Officers.Should().BeEmpty();
        profile.ActiveOfficer.Should().BeNull();
        profile.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task A_saved_profile_round_trips()
    {
        var officer = Officer();
        var saved = new DispatchProfile { GamePath = @"C:\Games\GTAV" }.WithOfficer(officer);

        await _store.SaveAsync(saved);
        var loaded = await _store.LoadAsync();

        loaded.Officers.Should().HaveCount(1);
        loaded.ActiveOfficer.Should().NotBeNull();
        loaded.ActiveOfficer!.Name.Should().Be("J. Reyes");
        loaded.ActiveOfficer.Agency.Should().Be(Agency.Lssd);
        loaded.ActiveOfficer.Callsign.Should().Be("2 LINCOLN 14");
        loaded.GamePath.Should().Be(@"C:\Games\GTAV");
        loaded.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task Saving_creates_the_directory_if_it_is_missing()
    {
        Directory.Exists(_root).Should().BeFalse("the test root starts absent");

        await _store.SaveAsync(new DispatchProfile().WithOfficer(Officer()));

        File.Exists(_paths.ProfileFile).Should().BeTrue();
    }

    [Fact]
    public async Task Saving_leaves_no_temporary_file_behind()
    {
        // The write goes via a temp file and a move; a leftover .tmp means the
        // move did not happen and the next save would be writing over debris.
        await _store.SaveAsync(new DispatchProfile().WithOfficer(Officer()));

        File.Exists(_paths.ProfileFile + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task Saving_twice_replaces_rather_than_appends()
    {
        await _store.SaveAsync(new DispatchProfile().WithOfficer(Officer("First")));
        await _store.SaveAsync(new DispatchProfile().WithOfficer(Officer("Second")));

        var loaded = await _store.LoadAsync();

        loaded.Officers.Should().HaveCount(1);
        loaded.Officers[0].Name.Should().Be("Second");
    }

    [Fact]
    public async Task A_corrupt_profile_is_set_aside_rather_than_destroyed()
    {
        _paths.EnsureCreated();
        await File.WriteAllTextAsync(_paths.ProfileFile, "{ this is not json");

        var profile = await _store.LoadAsync();

        profile.Officers.Should().BeEmpty("a broken file yields a clean start");

        Directory.GetFiles(_root, "profile.json.corrupt-*")
            .Should().ContainSingle("the original is kept so it can be sent on");
    }

    [Fact]
    public async Task A_newer_schema_is_left_alone()
    {
        // Rewriting a file from a newer build would silently drop whatever
        // that build added.
        _paths.EnsureCreated();
        await File.WriteAllTextAsync(
            _paths.ProfileFile,
            """{"schemaVersion": 99, "officers": [], "appearance": {}}""");

        var profile = await _store.LoadAsync();

        profile.SchemaVersion.Should().Be(99);
    }

    [Fact]
    public async Task Saving_always_stamps_the_current_schema_version()
    {
        await _store.SaveAsync(new DispatchProfile { SchemaVersion = 0 }.WithOfficer(Officer()));

        var loaded = await _store.LoadAsync();

        loaded.SchemaVersion.Should().Be(DispatchProfile.CurrentSchemaVersion);
    }

    [Fact]
    public async Task Appearance_settings_survive_a_round_trip()
    {
        var saved = new DispatchProfile
        {
            Appearance = new AppearanceSettings { ReducedMotion = true, SoundEnabled = true },
        };

        await _store.SaveAsync(saved);
        var loaded = await _store.LoadAsync();

        loaded.Appearance.ReducedMotion.Should().BeTrue();
        loaded.Appearance.SoundEnabled.Should().BeTrue();
        loaded.Appearance.DiscordPresence.Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_saves_do_not_corrupt_the_file()
    {
        // Two screens can both persist on the same tick. Without the gate one
        // write lands mid-way through the other's move.
        var writes = Enumerable.Range(0, 20).Select(i =>
            _store.SaveAsync(new DispatchProfile().WithOfficer(Officer($"Officer {i}"))));

        await Task.WhenAll(writes);

        var loaded = await _store.LoadAsync();
        loaded.Officers.Should().HaveCount(1);
        loaded.ActiveOfficer.Should().NotBeNull();
    }

    [Fact]
    public void Adding_an_officer_makes_them_active()
    {
        var officer = Officer();

        var profile = new DispatchProfile().WithOfficer(officer);

        profile.ActiveOfficerId.Should().Be(officer.Id);
    }

    [Fact]
    public void Replacing_an_officer_updates_rather_than_duplicates()
    {
        var officer = Officer();
        var profile = new DispatchProfile().WithOfficer(officer);

        var renamed = officer with { Name = "K. Mendez" };
        profile = profile.WithOfficer(renamed);

        profile.Officers.Should().HaveCount(1, "the identity is stable across a rename");
        profile.Officers[0].Name.Should().Be("K. Mendez");
    }

    [Fact]
    public void Removing_the_active_officer_promotes_another()
    {
        // Otherwise ActiveOfficerId points at nothing and the launcher opens
        // with no identity at all.
        var first = Officer("First");
        var second = Officer("Second");

        var profile = new DispatchProfile()
            .WithOfficer(first)
            .WithOfficer(second)
            .WithoutOfficer(second.Id);

        profile.Officers.Should().HaveCount(1);
        profile.ActiveOfficerId.Should().Be(first.Id);
    }

    [Fact]
    public void Removing_the_last_officer_clears_the_active_identifier()
    {
        var officer = Officer();

        var profile = new DispatchProfile()
            .WithOfficer(officer)
            .WithoutOfficer(officer.Id);

        profile.Officers.Should().BeEmpty();
        profile.ActiveOfficerId.Should().BeNull();
    }

    [Theory]
    [InlineData(Agency.Lspd, "LSPD")]
    [InlineData(Agency.Lssd, "LSSD")]
    [InlineData(Agency.Sahp, "SAHP")]
    [InlineData(Agency.Bcso, "BCSO")]
    public void Agency_codes_match_what_mod_configs_expect(Agency agency, string expected)
    {
        // Grammar Police and Callout Interface key off this code, not the
        // display name.
        var officer = OfficerProfile.Create("Test") with { Agency = agency };

        officer.AgencyCode.Should().Be(expected);
    }
}
