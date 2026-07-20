using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Dispatch.Core.Acquisition;
using Dispatch.Core.Catalogue;
using Dispatch.Core.Imagery;
using Dispatch.Core.Infrastructure;
using Dispatch.UI.Imagery;

namespace Dispatch.UI.Wizard.Steps;

/// <summary>One mod in the customise list, with its tick state.</summary>
public sealed partial class ModPick : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Constructs a pick from a catalogue definition.</summary>
    public ModPick(ModDefinition mod, bool selected)
    {
        Id = mod.Id;
        Name = mod.Name;
        Author = string.IsNullOrWhiteSpace(mod.Author) ? "Community" : mod.Author;
        AutoFetched = ModPackScaffolder.IsAutoFetched(mod);
        IsRequired = mod.Required;

        // A required mod is always on, whatever the preset asked for — nothing
        // else loads without it.
        IsSelected = mod.Required || selected;
    }

    /// <summary>Whether this is a core dependency that cannot be turned off.</summary>
    public bool IsRequired { get; }

    /// <summary>Whether the row's checkbox can be toggled. Required mods are locked on.</summary>
    public bool CanToggle => !IsRequired;

    partial void OnIsSelectedChanged(bool value)
    {
        // Guard against any attempt to clear a required mod.
        if (IsRequired && !value)
        {
            IsSelected = true;
        }
    }

    /// <summary>Stable mod id.</summary>
    public string Id { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Author.</summary>
    public string Author { get; }

    /// <summary>True when GitHub fetches it automatically; false when it comes from the pack.</summary>
    public bool AutoFetched { get; }

    /// <summary>Where it comes from, for the badge.</summary>
    public string SourceLabel => AutoFetched ? "GITHUB" : "PACK";
}

/// <summary>How much of the setup a preset installs.</summary>
public enum PresetTier
{
    /// <summary>LSPDFR and its dependencies only.</summary>
    Standard,

    /// <summary>Everything in the guide.</summary>
    FullDuty,

    /// <summary>Full Duty plus an extended realism layer. Contents pending.</summary>
    Realism,
}

/// <summary>One selectable setup.</summary>
public sealed partial class PresetOption : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Constructs a preset.</summary>
    public PresetOption(
        PresetTier tier,
        string name,
        string tagline,
        string summary,
        string counts,
        string duration,
        string[] features,
        bool comingSoon = false)
    {
        Tier = tier;
        Name = name;
        Tagline = tagline;
        Summary = summary;
        Counts = counts;
        Duration = duration;
        Features = new ObservableCollection<string>(features);
        ComingSoon = comingSoon;
    }

    /// <summary>Which tier this is.</summary>
    public PresetTier Tier { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Recommended / Most complete, or empty.</summary>
    public string Tagline { get; }

    /// <summary>One paragraph, written for someone who has never modded.</summary>
    public string Summary { get; }

    /// <summary>Mod or file count.</summary>
    public string Counts { get; }

    /// <summary>Rough install time.</summary>
    public string Duration { get; }

    /// <summary>Headline features, listed on the card.</summary>
    public ObservableCollection<string> Features { get; }

    /// <summary>Whether the preset is not yet populated.</summary>
    public bool ComingSoon { get; }

    /// <summary>Catalogue preset id, used for both the background key and mod lookup.</summary>
    public string PresetId => Tier switch
    {
        PresetTier.Standard => "standard",
        PresetTier.FullDuty => "full-duty",
        _ => "realism",
    };

    /// <summary>Key used to look up a user-supplied background image.</summary>
    public string BackgroundKey => PresetId;

    /// <summary>
    /// A user-supplied background, when one has been dropped in the backgrounds
    /// folder. Null means the original vector scene is used instead.
    /// </summary>
    [ObservableProperty]
    private IImage? _background;

    /// <summary>
    /// How busy the vector scene behind this preset should be. Standard is a
    /// single quiet unit; Full Duty adds a second unit and patrol lighting;
    /// Realism adds air support. The scene escalates with the tier so the
    /// cards say what they mean before a word is read.
    /// </summary>
    public int SceneIntensity => Tier switch
    {
        PresetTier.Standard => 1,
        PresetTier.FullDuty => 2,
        _ => 3,
    };

    /// <summary>True when the second unit should be drawn.</summary>
    public bool ShowSecondUnit => SceneIntensity >= 2;

    /// <summary>True when patrol lighting should wash the scene.</summary>
    public bool ShowPatrolLighting => SceneIntensity >= 2;

    /// <summary>True when air support should be drawn.</summary>
    public bool ShowAirUnit => SceneIntensity >= 3;
}

/// <summary>
/// Screen 4. Three setups, side by side, each a live preview surface rather
/// than a radio button with a caption.
/// </summary>
public sealed partial class ChoosePresetStep : WizardStep
{
    private readonly IUserBackgrounds? _backgrounds;

    [ObservableProperty]
    private PresetOption? _selected;

    /// <summary>Constructs the screen, optionally resolving user background images.</summary>
    public ChoosePresetStep(IUserBackgrounds? backgrounds = null)
    {
        _backgrounds = backgrounds;

        Presets =
        [
            new PresetOption(
                PresetTier.Standard,
                "Standard Issue",
                string.Empty,
                "LSPDFR and its dependencies only. Go on duty, take callouts, make arrests. Nothing else changes.",
                "~6 files",
                "about 2 minutes",
                ["Go on duty", "Callouts", "Arrests"]),

            new PresetOption(
                PresetTier.FullDuty,
                "Full Duty",
                "RECOMMENDED",
                "Everything in the guide. Traffic stops with a real MDT, voice control for dispatch, ELS lighting, backup units, K9, spotlights, dash cam and radar.",
                "~41 mods",
                "about 15 minutes",
                ["MDT traffic stops", "Voice dispatch", "ELS lighting", "Backup and K9", "Dash cam and radar"]),

            new PresetOption(
                PresetTier.Realism,
                "Realism",
                "MOST COMPLETE",
                "Everything in Full Duty plus the extended realism layer — extra callouts, traffic AI, sirens and more.",
                "~65 mods",
                "about 25 minutes",
                ["Everything in Full Duty", "Extra callout packs", "Traffic AI & sirens", "Immersion mods"]),
        ];

        // Every installable mod, in install order, as a tickable pick. The
        // superset lives here once; selecting a preset ticks the right subset.
        Mods = new ObservableCollection<ModPick>(
            ModCatalogue.Mods.Values
                .OrderBy(m => m.Order)
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .Select(m => new ModPick(m, selected: false)));

        foreach (var pick in Mods)
        {
            pick.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ModPick.IsSelected))
                {
                    OnPropertyChanged(nameof(CanAdvance));
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(SelectionSummary));
                }
            };
        }

        LoadBackgrounds();

        // Full Duty is preselected, and is the only card with a gold border by
        // default. A recommendation that is not preselected is not a
        // recommendation.
        Select(Presets[1]);
    }

    /// <summary>The three setups.</summary>
    public ObservableCollection<PresetOption> Presets { get; }

    /// <summary>Every installable mod as a tickable pick, for the customise list.</summary>
    public ObservableCollection<ModPick> Mods { get; }

    /// <summary>The mods shown in the customise list after the search filter.</summary>
    public IEnumerable<ModPick> VisibleMods =>
        string.IsNullOrWhiteSpace(ModSearch)
            ? Mods
            : Mods.Where(m =>
                m.Name.Contains(ModSearch, StringComparison.OrdinalIgnoreCase)
                || m.Author.Contains(ModSearch, StringComparison.OrdinalIgnoreCase));

    /// <summary>Filter text for the customise list.</summary>
    [ObservableProperty]
    private string _modSearch = string.Empty;

    partial void OnModSearchChanged(string value) => OnPropertyChanged(nameof(VisibleMods));

    /// <summary>Whether the per-mod customise list is open.</summary>
    [ObservableProperty]
    private bool _isCustomizing;

    private long? _freeBytes;

    /// <summary>Records the game drive so free space and the disk-fit check are real.</summary>
    public void SetGameDrive(string? path)
    {
        _freeBytes = string.IsNullOrWhiteSpace(path) ? null : DiskSpace.FreeBytes(path);
        OnPropertyChanged(nameof(DiskAvailable));
        OnPropertyChanged(nameof(FitsOnDisk));
    }

    /// <summary>The ids of every ticked mod — exactly what the install will unpack.</summary>
    public IReadOnlyList<string> SelectedModIds =>
        Mods.Where(m => m.IsSelected).Select(m => m.Id).ToList();

    /// <summary>How many mods are ticked.</summary>
    public int SelectedCount => Mods.Count(m => m.IsSelected);

    /// <summary>Ticked count, as "12 mods selected".</summary>
    public string SelectionSummary =>
        SelectedCount == 1 ? "1 mod selected" : $"{SelectedCount} mods selected";

    /// <summary>Opens or closes the customise list.</summary>
    public void ToggleCustomize() => IsCustomizing = !IsCustomizing;

    /// <inheritdoc />
    public override string Title => "Choose a setup";

    /// <inheritdoc />
    public override string AdvanceLabel => "Install";

    /// <inheritdoc />
    /// <remarks>
    /// A coming-soon preset cannot be installed even though the required core is
    /// always ticked — its own lineup does not exist yet.
    /// </remarks>
    public override bool CanAdvance => SelectedCount > 0 && Selected is not { ComingSoon: true };

    /// <summary>Total download for the current selection.</summary>
    public string DownloadSize => Selected?.Tier switch
    {
        PresetTier.Standard => "84 MB",
        PresetTier.FullDuty => "1.4 GB",
        _ => "—",
    };

    /// <summary>Disk needed, including staging headroom.</summary>
    public string DiskRequired => Selected?.Tier switch
    {
        PresetTier.Standard => "210 MB",
        PresetTier.FullDuty => "3.6 GB",
        _ => "—",
    };

    /// <summary>Free space on the game drive, read live.</summary>
    public string DiskAvailable => DiskSpace.Format(_freeBytes);

    /// <summary>
    /// Whether the selection fits. Unknown free space is treated as fitting
    /// rather than blocking — a missing reading is not a reason to stop.
    /// </summary>
    public bool FitsOnDisk => _freeBytes is null || _freeBytes >= RequiredBytes();

    /// <summary>A rough disk estimate for the current selection, for the fit check.</summary>
    private long RequiredBytes()
    {
        // ~50 MB per mod plus staging headroom; deliberately generous so the
        // check warns early rather than late.
        const long perMod = 50L * 1024 * 1024;
        return Math.Max(1, SelectedCount) * perMod + 512L * 1024 * 1024;
    }

    /// <summary>Selects a preset, clearing the others and ticking its mods.</summary>
    public void Select(PresetOption preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        foreach (var option in Presets)
        {
            option.IsSelected = ReferenceEquals(option, preset);
        }

        // Setting Selected drives the ticking through OnSelectedChanged, so a
        // card clicked via the list binding and a programmatic Select behave the
        // same.
        Selected = preset;
    }

    /// <summary>Reticks the list to a preset's mods.</summary>
    /// <remarks>
    /// An empty or coming-soon preset — Realism has no contents yet — clears the
    /// selection, so choosing it installs nothing until the user ticks mods by
    /// hand in the customise list. That is what keeps a coming-soon card from
    /// silently carrying the previous card's selection forward.
    /// </remarks>
    private void TickPresetMods(PresetOption preset)
    {
        var wanted = ModCatalogue.ModsFor(preset.PresetId)
            .Select(m => m.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var pick in Mods)
        {
            // Required mods stay on no matter which preset is chosen.
            pick.IsSelected = pick.IsRequired || wanted.Contains(pick.Id);
        }
    }

    /// <summary>
    /// Resolves each card's photograph.
    /// </summary>
    /// <remarks>
    /// Three sources, most specific first: a file the user dropped in their own
    /// backgrounds folder, a file named for the tier in Assets/Presets, then a
    /// positional pick from the compiled-in pool. The last one is what makes
    /// this work with the arbitrary filenames a browser produces, while still
    /// giving each tier a different and consistent image.
    ///
    /// <para>
    /// Standard takes the first image, Full Duty the second, Realism the third,
    /// so the visual escalation the copy describes is at least stable even
    /// before anyone curates the folder.
    /// </para>
    /// </remarks>
    private void LoadBackgrounds()
    {
        for (var index = 0; index < Presets.Count; index++)
        {
            var preset = Presets[index];

            // A user-supplied override always wins.
            var userPath = _backgrounds?.TryResolve(preset.BackgroundKey);
            if (userPath is not null)
            {
                try
                {
                    preset.Background = new Bitmap(userPath);
                    continue;
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
                {
                    // Fall through to the compiled-in art.
                }
            }

            preset.Background = ImageCatalog.For(preset.BackgroundKey, index);
        }
    }

    partial void OnSelectedChanged(PresetOption? value)
    {
        if (value is not null)
        {
            TickPresetMods(value);
        }

        OnPropertyChanged(nameof(CanAdvance));
        OnPropertyChanged(nameof(DownloadSize));
        OnPropertyChanged(nameof(DiskRequired));
        OnPropertyChanged(nameof(FitsOnDisk));
    }
}
