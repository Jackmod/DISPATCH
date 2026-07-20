using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Profiles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.UI.Launcher;

/// <summary>One headline figure on the profile screen.</summary>
/// <param name="Label">What it measures.</param>
/// <param name="Value">The figure.</param>
/// <param name="Sub">A line of context.</param>
/// <param name="IconKey">Resource key of the glyph.</param>
public sealed record StatCard(string Label, string Value, string Sub, string IconKey)
{
    /// <summary>The glyph, resolved from application resources like the nav items.</summary>
    public Geometry? Icon =>
        Application.Current?.TryGetResource(IconKey, null, out var value) == true
            ? value as Geometry
            : null;
}

/// <summary>
/// The officer's profile: who they are, a portrait, and a career's worth of
/// numbers pulled from every session they have worked.
/// </summary>
/// <remarks>
/// The identity comes from the profile; the numbers come from the career record,
/// which grows a row per shift. The screen loads asynchronously so a large history
/// never blocks the launcher, and it degrades gracefully — no avatar shows initials,
/// no sessions shows a rookie's empty sheet rather than an error.
/// </remarks>
public sealed partial class ProfileViewModel : ObservableObject
{
    private readonly IProfileStatsStore _stats;
    private readonly IInstallRecordStore _record;
    private readonly IAppPaths _paths;

    [ObservableProperty]
    private Bitmap? _avatar;

    [ObservableProperty]
    private bool _hasAvatar;

    [ObservableProperty]
    private string _rank = "Cadet";

    [ObservableProperty]
    private string _memberSince = "—";

    [ObservableProperty]
    private IReadOnlyList<StatCard> _cards = [];

    /// <summary>Constructs the profile screen. Services default for design-time and tests.</summary>
    public ProfileViewModel(
        OfficerProfile? officer = null,
        string? gamePath = null,
        IProfileStatsStore? stats = null,
        IInstallRecordStore? record = null,
        IAppPaths? paths = null)
    {
        Officer = officer;
        _paths = paths ?? new AppPaths();
        _stats = stats ?? new ProfileStatsStore(_paths);
        _record = record ?? new InstallRecordStore(_paths, NullLogger<InstallRecordStore>.Instance);
    }

    /// <summary>The officer.</summary>
    public OfficerProfile? Officer { get; }

    /// <summary>Officer name, or a placeholder.</summary>
    public string Name => Officer?.Name ?? "Set up an officer";

    /// <summary>Callsign.</summary>
    public string Callsign => Officer?.Callsign ?? "1 ADAM 7";

    /// <summary>Agency code.</summary>
    public string Agency => Officer?.AgencyCode ?? "LSPD";

    /// <summary>Department name.</summary>
    public string Department => Officer?.DepartmentName ?? "Los Santos Police Department";

    /// <summary>The two-letter initials shown when there is no avatar.</summary>
    public string Initials
    {
        get
        {
            var name = Officer?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return "★";
            }

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 1
                ? parts[0][..1].ToUpperInvariant()
                : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
        }
    }

    private bool _loaded;

    /// <summary>Loads once, on first appearance; repeat visits are free.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Loads the career record and portrait. Called when the screen appears.</summary>
    public async Task LoadAsync()
    {
        var stats = await _stats.LoadAsync().ConfigureAwait(true);
        var record = await _record.LoadAsync().ConfigureAwait(true);
        var modCount = record?.ModIds.Count ?? 0;

        LoadAvatar(stats.AvatarPath);

        Rank = RankFor(stats.TotalHours);
        MemberSince = stats.FirstSeen is { } first
            ? "On the force since " + first.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : "New recruit";

        Cards =
        [
            new StatCard("HOURS ON DUTY", stats.TotalHours.ToString(CultureInfo.InvariantCulture),
                $"{stats.SessionCount} shift(s) worked", "IconPlay"),
            new StatCard("CALLOUTS", stats.TotalCallouts.ToString(CultureInfo.InvariantCulture),
                "Answered across your career", "IconRadioWave"),
            new StatCard("ARRESTS", stats.TotalArrests.ToString(CultureInfo.InvariantCulture),
                "Suspects booked", "IconLock"),
            new StatCard("PURSUITS", stats.TotalPursuits.ToString(CultureInfo.InvariantCulture),
                "High-speed chases", "IconLightbar"),
            new StatCard("CITATIONS", stats.TotalCitations.ToString(CultureInfo.InvariantCulture),
                "Tickets written", "IconFile"),
            new StatCard("MODS INSTALLED", modCount.ToString(CultureInfo.InvariantCulture),
                modCount == 0 ? "Run the installer to begin" : "In your current setup", "IconMods"),
            new StatCard("DAYS ON FORCE", stats.DaysOnForce(DateTimeOffset.UtcNow).ToString(CultureInfo.InvariantCulture),
                "Since you first reported", "IconShield"),
            new StatCard("AVG SHIFT", $"{(int)stats.AverageSessionMinutes}m",
                stats.SessionCount == 0 ? "No shifts yet" : "Typical time on duty", "IconDashboard"),
        ];
    }

    /// <summary>
    /// Sets a new profile picture from a chosen file, copying it into app storage so
    /// the original can move or be deleted without breaking the portrait.
    /// </summary>
    public async Task SetAvatarAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            _paths.EnsureCreated();
            var extension = Path.GetExtension(sourcePath);
            var destination = Path.Combine(_paths.Root, "avatar" + extension);
            File.Copy(sourcePath, destination, overwrite: true);

            await _stats.SetAvatarAsync(destination).ConfigureAwait(true);
            LoadAvatar(destination);
        }
        catch (IOException)
        {
            // A locked or vanished source is not worth taking the screen down over.
        }
    }

    private void LoadAvatar(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                Avatar = new Bitmap(path);
                HasAvatar = true;
                return;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            {
                // Unreadable image: fall through to initials.
            }
        }

        Avatar = null;
        HasAvatar = false;
    }

    // A light-hearted rank ladder off hours logged, so the number means something.
    private static string RankFor(int hours) => hours switch
    {
        >= 500 => "Chief",
        >= 250 => "Captain",
        >= 120 => "Lieutenant",
        >= 60 => "Sergeant",
        >= 20 => "Senior Officer",
        >= 5 => "Officer",
        _ => "Cadet",
    };
}
