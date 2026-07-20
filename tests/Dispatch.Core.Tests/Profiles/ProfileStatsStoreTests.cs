using Dispatch.Core.Infrastructure;
using Dispatch.Core.Profiles;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Profiles;

/// <summary>
/// The career record: totals derive from sessions (never a stored running total
/// that can drift), the join date is stamped once, and a corrupt file degrades to
/// an empty career rather than taking the profile screen down.
/// </summary>
public sealed class ProfileStatsStoreTests : IDisposable
{
    private readonly string _root;
    private readonly ProfileStatsStore _store;

    public ProfileStatsStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dispatch-stats-{Guid.NewGuid():N}");
        _store = new ProfileStatsStore(new AppPaths(_root, _root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task First_load_stamps_the_join_date()
    {
        var stats = await _store.LoadAsync();

        stats.FirstSeen.Should().NotBeNull();
        stats.SessionCount.Should().Be(0);
    }

    [Fact]
    public async Task Recording_sessions_accumulates_the_career_totals()
    {
        await _store.RecordSessionAsync(new SessionStat(DateTimeOffset.UtcNow.AddDays(-1), 72, 6, 4, 2, 3));
        var stats = await _store.RecordSessionAsync(new SessionStat(DateTimeOffset.UtcNow, 48, 3, 1, 1, 0));

        stats.SessionCount.Should().Be(2);
        stats.TotalMinutes.Should().Be(120);
        stats.TotalHours.Should().Be(2);
        stats.TotalCallouts.Should().Be(9);
        stats.TotalArrests.Should().Be(5);
        stats.LastSession!.Minutes.Should().Be(48);
        stats.AverageSessionMinutes.Should().Be(60);
    }

    [Fact]
    public async Task Setting_an_avatar_persists_across_a_reload()
    {
        await _store.SetAvatarAsync("C:/pics/me.png");

        var reloaded = await new ProfileStatsStore(new AppPaths(_root, _root)).LoadAsync();

        reloaded.AvatarPath.Should().Be("C:/pics/me.png");
    }

    [Fact]
    public async Task Days_on_force_counts_from_the_join_date()
    {
        var stats = new ProfileStats { FirstSeen = DateTimeOffset.UtcNow.AddDays(-10) };

        stats.DaysOnForce(DateTimeOffset.UtcNow).Should().Be(10);
    }

    [Fact]
    public async Task A_corrupt_file_reads_as_an_empty_career()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "profile-stats.json"), "{ not json");

        var stats = await _store.LoadAsync();

        stats.SessionCount.Should().Be(0);
        stats.FirstSeen.Should().NotBeNull();
    }
}
