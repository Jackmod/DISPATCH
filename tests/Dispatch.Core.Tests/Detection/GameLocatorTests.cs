using Dispatch.Core.Detection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Detection;

/// <summary>
/// Detection has to find every copy and mis-attribute none. The parsers are
/// tested against the real quirks of Valve's VDF and Epic's manifests —
/// doubled backslashes, a game that is not GTA V — because those are exactly
/// what a naive read gets wrong, and the cost of getting it wrong is a later
/// install writing to the wrong folder.
/// </summary>
public sealed class GameLocatorTests
{
    // ===== Steam VDF ======================================================

    [Fact]
    public void Steam_library_paths_are_unescaped()
    {
        // Valve doubles the backslashes; leaving them doubled yields a folder
        // that does not exist.
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"    "C:\\Program Files (x86)\\Steam"
                }
                "1"
                {
                    "path"    "D:\\SteamLibrary"
                }
            }
            """;

        var paths = GameLocator.ParseSteamLibraries(vdf);

        paths.Should().Equal(
            @"C:\Program Files (x86)\Steam",
            @"D:\SteamLibrary");
    }

    [Fact]
    public void An_empty_vdf_yields_no_libraries()
    {
        GameLocator.ParseSteamLibraries("\"libraryfolders\"\n{\n}").Should().BeEmpty();
    }

    // ===== Epic manifest ==================================================

    [Fact]
    public void An_epic_manifest_for_gta_yields_its_install_location()
    {
        const string manifest = """
            {
                "AppName": "GTA5",
                "DisplayName": "Grand Theft Auto V",
                "InstallLocation": "D:\\Epic\\GTAV"
            }
            """;

        GameLocator.ParseEpicManifest(manifest).Should().Be(@"D:\Epic\GTAV");
    }

    [Fact]
    public void An_epic_manifest_for_another_game_is_ignored()
    {
        // Epic writes one manifest per game; returning a different game's
        // folder as GTA V is a silent mis-attribution.
        const string manifest = """
            {
                "AppName": "Fortnite",
                "DisplayName": "Fortnite",
                "InstallLocation": "D:\\Epic\\Fortnite"
            }
            """;

        GameLocator.ParseEpicManifest(manifest).Should().BeNull();
    }

    [Fact]
    public void A_malformed_manifest_returns_null_rather_than_throwing()
    {
        GameLocator.ParseEpicManifest("{ not valid json").Should().BeNull();
    }

    // ===== End to end against a fake filesystem ===========================

    [Fact]
    public void Both_a_steam_and_an_epic_copy_are_returned()
    {
        // The whole point: someone with two copies chooses, rather than the app
        // silently picking.
        var fs = new FakeProbe
        {
            SteamPath = @"C:\Steam",
            SteamVdf = """
                "libraryfolders" { "0" { "path" "C:\\Steam" } }
                """,
            EpicManifestsPath = @"C:\ProgramData\Epic\Manifests",
            Manifests =
            [
                """{ "AppName": "GTA5", "InstallLocation": "D:\\Epic\\GTAV" }""",
            ],
        };
        fs.Directories.Add(@"C:\ProgramData\Epic\Manifests");
        fs.Directories.Add(@"C:\Steam\steamapps\common\Grand Theft Auto V");
        fs.Directories.Add(@"D:\Epic\GTAV");
        fs.Files.Add(@"C:\Steam\steamapps\common\Grand Theft Auto V\GTA5.exe");
        fs.Files.Add(@"D:\Epic\GTAV\GTA5.exe");

        var installs = new GameLocator(NullLogger<GameLocator>.Instance, fs).Locate();

        installs.Should().HaveCount(2);
        installs.Select(i => i.Platform).Should().Contain([GamePlatform.Steam, GamePlatform.Epic]);
    }

    [Fact]
    public void A_folder_without_the_executable_is_not_returned()
    {
        // A folder can exist as a leftover after an uninstall. Without the exe
        // it is not a usable install.
        var fs = new FakeProbe
        {
            SteamPath = @"C:\Steam",
            SteamVdf = """ "libraryfolders" { "0" { "path" "C:\\Steam" } } """,
        };
        fs.Directories.Add(@"C:\Steam\steamapps\common\Grand Theft Auto V");
        // No GTA5.exe added.

        new GameLocator(NullLogger<GameLocator>.Instance, fs).Locate().Should().BeEmpty();
    }

    [Fact]
    public void The_same_install_reached_two_ways_appears_once()
    {
        // A common path and a Steam library can point at the same folder.
        var shared = @"C:\Games\Grand Theft Auto V";
        var fs = new FakeProbe { CommonPaths = [shared] };
        fs.Directories.Add(shared);
        fs.Files.Add(Path.Combine(shared, "GTA5.exe"));

        // Also reachable via a Steam library at the same resolved path.
        fs.SteamPath = @"C:\Steam";
        fs.SteamVdf = """ "libraryfolders" { "0" { "path" "C:\\Games" } } """;
        fs.Directories.Add(@"C:\Games\steamapps\common\Grand Theft Auto V");

        var installs = new GameLocator(NullLogger<GameLocator>.Instance, fs).Locate();

        installs.Select(i => i.Path).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Nothing_installed_yields_an_empty_list_rather_than_throwing()
    {
        new GameLocator(NullLogger<GameLocator>.Instance, new FakeProbe()).Locate().Should().BeEmpty();
    }

    /// <summary>An in-memory probe, so detection is tested without a real machine.</summary>
    private sealed class FakeProbe : IFileSystemProbe
    {
        public string? SteamPath { get; set; }
        public string? SteamVdf { get; set; }
        public string? EpicManifestsPath { get; set; }
        public IReadOnlyList<string> CommonPaths { get; set; } = [];
        public IReadOnlyList<string> Manifests { get; set; } = [];

        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? ReadText(string path) =>
            path.EndsWith("libraryfolders.vdf", StringComparison.OrdinalIgnoreCase) ? SteamVdf : null;

        public IEnumerable<string> ReadAllManifests(string folder) => Manifests;

        // The fixtures are Windows-style paths (drive letters, backslashes) because
        // that is what Valve and Epic actually write. Comparisons and resolution
        // are made separator-neutral so the same fixtures exercise the real
        // Path.Combine logic on a case- and separator-sensitive Linux runner too —
        // Path.GetFullPath would otherwise mangle a "C:\..." path there.
        private static string Norm(string p) => p.Replace('\\', '/');

        public bool DirectoryExists(string path) =>
            Directories.Any(d => string.Equals(Norm(d), Norm(path), StringComparison.OrdinalIgnoreCase));

        public bool FileExists(string path) =>
            Files.Any(f => string.Equals(Norm(f), Norm(path), StringComparison.OrdinalIgnoreCase));

        public string ResolveRealPath(string path) => Norm(path);
    }
}
