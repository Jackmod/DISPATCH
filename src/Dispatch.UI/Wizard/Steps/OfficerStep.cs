using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Dispatch.UI.Wizard.Steps;

/// <summary>
/// Screen 6. The personal bit, and the first screen that should feel like a
/// game rather than a tool.
/// </summary>
public sealed partial class OfficerStep : WizardStep
{
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
