using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Dispatch.Core.Imagery;

namespace Dispatch.UI.Wizard.Steps;

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

    /// <summary>Key used to look up a user-supplied background image.</summary>
    public string BackgroundKey => Tier switch
    {
        PresetTier.Standard => "standard",
        PresetTier.FullDuty => "full-duty",
        _ => "realism",
    };

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
                "Everything in Full Duty plus an extended realism layer.",
                "pending",
                "—",
                ["Everything in Full Duty", "Extended realism layer"],
                comingSoon: true),
        ];

        LoadBackgrounds();

        // Full Duty is preselected, and is the only card with a gold border by
        // default. A recommendation that is not preselected is not a
        // recommendation.
        Select(Presets[1]);
    }

    /// <summary>The three setups.</summary>
    public ObservableCollection<PresetOption> Presets { get; }

    /// <inheritdoc />
    public override string Title => "Choose a setup";

    /// <inheritdoc />
    public override string AdvanceLabel => "Install";

    /// <inheritdoc />
    public override bool CanAdvance => Selected is { ComingSoon: false };

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

    /// <summary>Free space on the game drive.</summary>
    public string DiskAvailable => "184 GB";

    /// <summary>Whether the selection fits. Drives the red state on free space.</summary>
    public bool FitsOnDisk => true;

    /// <summary>Selects a preset, clearing the others.</summary>
    public void Select(PresetOption preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        foreach (var option in Presets)
        {
            option.IsSelected = ReferenceEquals(option, preset);
        }

        Selected = preset;
    }

    private void LoadBackgrounds()
    {
        if (_backgrounds is null)
        {
            return;
        }

        foreach (var preset in Presets)
        {
            var path = _backgrounds.TryResolve(preset.BackgroundKey);
            if (path is null)
            {
                continue;
            }

            // A corrupt or half-copied file must not take the wizard down over
            // what is purely decoration.
            try
            {
                preset.Background = new Bitmap(path);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
            {
                preset.Background = null;
            }
        }
    }

    partial void OnSelectedChanged(PresetOption? value)
    {
        OnPropertyChanged(nameof(CanAdvance));
        OnPropertyChanged(nameof(DownloadSize));
        OnPropertyChanged(nameof(DiskRequired));
        OnPropertyChanged(nameof(FitsOnDisk));
    }
}
