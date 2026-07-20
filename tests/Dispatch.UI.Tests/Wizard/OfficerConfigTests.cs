using Avalonia.Headless.XUnit;
using Dispatch.Core.Configuration;
using Dispatch.Core.Infrastructure;
using Dispatch.Core.Profiles;
using Dispatch.UI.Wizard;
using Dispatch.UI.Wizard.Steps;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.UI.Tests.Wizard;

/// <summary>
/// The officer's own details — callsign, name, department, air unit — must land
/// in the config files. The install runs before the officer exists, so this
/// checks the values are written when the officer is finalised.
/// </summary>
public sealed class OfficerConfigTests : IDisposable
{
    private readonly string _root =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dispatch-officercfg", System.Guid.NewGuid().ToString("N"));

    private readonly string _game;
    private readonly AppPaths _paths;

    public OfficerConfigTests()
    {
        _game = System.IO.Path.Combine(_root, "game");
        System.IO.Directory.CreateDirectory(_game);
        _paths = new AppPaths(System.IO.Path.Combine(_root, "appdata"), System.IO.Path.Combine(_root, "temp"));
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true); }
        catch (System.IO.IOException) { }
    }

    private void WriteConfig(string relative, string content)
    {
        var full = System.IO.Path.Combine(_game, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
    }

    private string ReadConfig(string relative) =>
        System.IO.File.ReadAllText(System.IO.Path.Combine(_game, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));

    [AvaloniaFact]
    public async Task Finalising_the_officer_writes_the_real_callsign_into_the_config()
    {
        // A Callout Interface config left on the placeholder value by the install.
        WriteConfig("Plugins/LSPDFR/CalloutInterface.ini",
            "MDTCallSign = 1 ADAM 7\n" +
            "CalloutMenuKey = F10\n");

        // An install record naming Callout Interface as installed.
        System.IO.File.WriteAllText(_paths.InstallRecordFile,
            "{\"schemaVersion\":1,\"presetId\":\"full-duty\",\"modIds\":[\"calloutinterface\"]," +
            "\"files\":[{\"relativePath\":\"plugins/CalloutInterface.dll\",\"sha256\":\"sha256:x\",\"mod\":\"calloutinterface\"}]}");

        var config = new ConfigInstaller(
            new IniConfigWriter(NullLogger<IniConfigWriter>.Instance), NullLogger<ConfigInstaller>.Instance);
        var records = new InstallRecordStore(_paths, NullLogger<InstallRecordStore>.Instance);

        var wizard = new WizardViewModel(config: config, installRecords: records);

        // Enter the officer with a distinctive callsign, and point the wizard at
        // the game folder.
        var officerStep = wizard.Steps.OfType<OfficerStep>().Single();
        officerStep.OfficerName = "Jack Portman";
        officerStep.CallsignDivision = 3;
        officerStep.CallsignPhonetic = "LINCOLN";
        officerStep.CallsignBeat = 22;

        var locate = wizard.Steps.OfType<LocateGameStep>().Single();
        locate.Selected = new GameCandidate("Manual", _game, "1.0.3889", GameCandidateState.Verified, "ok");

        await wizard.BuildOfficerAsync();

        ReadConfig("Plugins/LSPDFR/CalloutInterface.ini")
            .Should().Contain("MDTCallSign = 3 LINCOLN 22", "the officer's real callsign replaces the placeholder");
    }
}
