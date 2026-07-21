using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dispatch.Core.Audio;

namespace Dispatch.UI.Wizard.Steps;

/// <summary>
/// Screen 6. The personal bit, and the first screen that should feel like a
/// game rather than a tool.
/// </summary>
public sealed partial class OfficerStep : WizardStep
{
    private readonly ICallsignVoice _voice;

    /// <summary>Constructs the screen against a voice.</summary>
    public OfficerStep(ICallsignVoice? voice = null) =>
        _voice = voice ?? new SilentCallsignVoice();

    /// <summary>
    /// Whether the callsign can actually be played back. False hides the
    /// button rather than offering one that silently does nothing.
    /// </summary>
    public bool CanHearCallsign => _voice.IsAvailable;

    /// <summary>Reads the current callsign back in radio phrasing.</summary>
    [RelayCommand]
    private async Task HearCallsignAsync() =>
        await _voice.SpeakAsync(CallsignPreview).ConfigureAwait(false);

    [ObservableProperty]
    private string _officerName = string.Empty;

    [ObservableProperty]
    private string _agency = "LSPD";

    [ObservableProperty]
    private int _callsignDivision = 1;

    [ObservableProperty]
    private string _callsignPhonetic = "ADAM";

    [ObservableProperty]
    private int _callsignBeat = 7;

    [ObservableProperty]
    private string _departmentName = "Los Santos Police Department";

    [ObservableProperty]
    private string _airUnitCallsign = "AIR 1";

    [ObservableProperty]
    private string _controlProfile = "Suggested";

    /// <summary>
    /// Whether to open the DISPATCH launcher the moment setup finishes. On by
    /// default — most people want to go straight in; unticking closes DISPATCH
    /// instead, to be reopened later from the Desktop shortcut.
    /// </summary>
    [ObservableProperty]
    private bool _launchWhenDone = true;

    /// <summary>The four agencies, as cards with vector insignia.</summary>
    public ObservableCollection<string> Agencies { get; } = ["LSPD", "LSSD", "SAHP", "BCSO"];

    /// <summary>The LAPD phonetic set, as the scrolling middle column of the callsign builder.</summary>
    public ObservableCollection<string> Phonetics { get; } =
    [
        "ADAM", "BOY", "CHARLES", "DAVID", "EDWARD", "FRANK", "GEORGE", "HENRY",
        "IDA", "JOHN", "KING", "LINCOLN", "MARY", "NORA", "OCEAN", "PAUL",
        "QUEEN", "ROBERT", "SAM", "TOM", "UNION", "VICTOR", "WILLIAM",
        "XRAY", "YOUNG", "ZEBRA",
    ];

    /// <summary>The three keybind profiles.</summary>
    public ObservableCollection<string> ControlProfiles { get; } = ["Default", "Suggested", "Custom"];

    /// <inheritdoc />
    public override string Title => "Your officer";

    /// <inheritdoc />
    public override string AdvanceLabel => "Finish";

    /// <inheritdoc />
    public override bool CanAdvance => !string.IsNullOrWhiteSpace(OfficerName);

    /// <summary>Live callsign preview, rendered large in display type.</summary>
    public string CallsignPreview => $"{CallsignDivision} {CallsignPhonetic} {CallsignBeat}";

    partial void OnOfficerNameChanged(string value) => OnPropertyChanged(nameof(CanAdvance));

    partial void OnCallsignDivisionChanged(int value) => OnPropertyChanged(nameof(CallsignPreview));

    partial void OnCallsignPhoneticChanged(string value) => OnPropertyChanged(nameof(CallsignPreview));

    partial void OnCallsignBeatChanged(int value) => OnPropertyChanged(nameof(CallsignPreview));
}
