namespace Dispatch.Core.Maintenance;

/// <summary>
/// Files the mods in the pack are known to install — the cleaner's cross-reference
/// list. A loose file at the game root whose name is on this list came out of a mod
/// archive, not a stock GTA V install, so it can be recognised for what it is even
/// though the root is where stock and mod files sit side by side.
/// </summary>
/// <remarks>
/// Generated from the actual mod archives (their file listings), then hand-extended
/// with the shared runtime libraries mods bundle that don't carry the mod's own name
/// (EasyHook, SlimDX, Mono.Cecil, irrKlang, the audio codecs). Matching is by file
/// name only: these ship loose at the game root, and a name is enough to know a
/// <c>SlimDX.dll</c> or <c>ELS.asi</c> at the root did not come with the game.
///
/// <para>
/// This is a positive signal used to <em>upgrade</em> confidence — a matched file is
/// reported as a known mod file — never a gate: the cleaner still catches mod files
/// not on this list (a mod's own config and logs, a mod the pack does not carry)
/// through the stock allowlist and the root-extension rule. Being on this list only
/// changes the wording and the tier, not whether a non-stock root file is caught.
/// </para>
/// </remarks>
public static class ModArtifacts
{
    /// <summary>Known mod file names, matched case-insensitively on the file name alone.</summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // ===== From the pack's mod archives ==============================
        "AdvancedHookV.dll",
        "CalloutInterface.dll",
        "CalloutInterfaceAPI.dll",
        "ClearTheWayV.dll",
        "Clipboard.dll",
        "CommonDataFramework.dll",
        "CompuLite.dll",
        "Custom Pullover.dll",
        "DamageTrackerLib.dll",
        "DamageTrackingFramework.dll",
        "DdsConvert.dll",
        "DeadlyWeapons.dll",
        "discord-rpc.dll",
        "DiscordRpcNet.dll",
        "EasyHook.dll",
        "EasyHook64.dll",
        "EasyLoad64.dll",
        "ELS.asi",
        "Fast_Draw.dll",
        "FW1FontWrapper.dll",
        "GrammarPolice.dll",
        "GTAV.ResourceAdjuster.asi",
        "Gwen.dll",
        "Gwen.UnitTest.dll",
        "HeliAssistance.dll",
        "Immersive Effects.dll",
        "IPT.Common.dll",
        "IPTCommon.dll",
        "CalloutInterface.ApplicationExtension.dll",
        "KTFDO.dll",
        "LemonUI.AltV.Async.dll",
        "LemonUI.AltV.dll",
        "LemonUI.FiveM.dll",
        "LemonUI.RageMP.dll",
        "LemonUI.RagePluginHook.dll",
        "LemonUI.SHVDN3.dll",
        "LemonUI.SHVDNC.dll",
        "LidarGun.dll",
        "LMS.Common.dll",
        "LMS.PortableExecutable.dll",
        "LSPD First Response.dll",
        "Microsoft.Expression.Drawing.dll",
        "Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll",
        "Mono.Cecil.dll",
        "Mono.Cecil.Mdb.dll",
        "Mono.Cecil.Pdb.dll",
        "Mono.Cecil.Rocks.dll",
        "PyroCommon.dll",
        "RadioRealismFR.dll",
        "RawCanvasUI.dll",
        "Restrain The Deceased.dll",
        "RichsPoliceEnhancements.dll",
        "RiskierTrafficStops.dll",
        "SimpleHUD.asi",
        "SlimDX.dll",
        "SpeedRadarLite.dll",
        "Spotlight.dll",
        "StickyWheels.dll",
        "StopThePed.dll",
        "System.ValueTuple.dll",
        "UltimateBackup.dll",
        "XInput1_4.dll",
        "FullTraffic.asi",
        "openCameraV.asi",

        // ===== Loaders and the LSPDFR core ===============================
        "dinput8.dll",
        "ScriptHookV.dll",
        "ScriptHookVDotNet.asi",
        "ScriptHookVDotNet2.dll",
        "ScriptHookVDotNet3.dll",
        "RAGENativeUI.dll",
        "RAGEPluginHook.exe",
        "RagePluginHook.exe",
        "TrainerV.asi",
        "NativeTrainer.asi",
        "OpenIV.asi",

        // ===== Shared runtime libraries mods bundle ======================
        // These carry no mod name of their own but only ever reach the game root
        // inside a mod archive; a stock install has none of them.
        "irrKlang.NET4.dll",
        "irrKlang.dll",
        "ikpMP3.dll",
        "ikpFlac.dll",
        "fvad.dll",
        "opus.dll",
        "opusenc.dll",
        "libtox.dll",
        "Newtonsoft.Json.dll",
        "vosk.dll",
        "PolyGlot.dll",
        "NAudio.dll",
        "System.Buffers.dll",
        "System.Memory.dll",
        "System.Numerics.Vectors.dll",
        "System.Runtime.CompilerServices.Unsafe.dll",
        "System.Threading.Tasks.Extensions.dll",
        "System.Text.Json.dll",
        "Microsoft.Bcl.AsyncInterfaces.dll",
    };

    /// <summary>True when a file name is known to belong to a mod the pack installs.</summary>
    public static bool Knows(string fileName) => Names.Contains(Path.GetFileName(fileName));
}
