using System.Globalization;
using System.Text.RegularExpressions;

namespace Dispatch.Core.Configuration;

/// <summary>Finds editable settings in whatever config files a game folder holds.</summary>
public interface IIniScanner
{
    /// <summary>
    /// Walks the mod folders, parses every config file, and returns each key as a
    /// setting the launcher can show and edit — including mods not in the catalogue.
    /// </summary>
    Task<IReadOnlyList<ModSetting>> ScanAsync(string gamePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Discovers settings by reading the config files a mod folder actually contains,
/// so a plugin the app has never heard of is still editable the moment it is added.
/// </summary>
/// <remarks>
/// The curated catalogue makes the common mods pleasant — real descriptions,
/// named choices, sensible ranges. This is the catch-all beneath it: point it at
/// the game folder and it turns every <c>.ini</c> under <c>plugins</c>,
/// <c>scripts</c> and <c>lspdfr</c>, plus the loose ones in the root, into named,
/// typed settings. Keys become readable names (<c>InitialSpeedThreshold</c> reads
/// as "Initial speed threshold"), and the value's shape picks the editor — a
/// <c>true</c>/<c>false</c> becomes a switch, a number a stepper, everything else a
/// text box. Nothing is guessed about meaning, only about shape, so the worst case
/// is a plain text field, never a wrong edit.
/// </remarks>
public sealed class IniScanner : IIniScanner
{
    private static readonly string[] Roots = ["plugins", "scripts", "lspdfr"];
    // File and folder names that say nothing about which mod owns the file, so the
    // plugin name is taken from a more meaningful ancestor folder instead.
    private static readonly HashSet<string> GenericFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "config", "settings", "setting", "ini", "options", "custom", "default",
        };

    // Guardrails so a stray large or binary ".ini" cannot stall or bloat a scan.
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxFiles = 800;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModSetting>> ScanAsync(string gamePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        if (!Directory.Exists(gamePath))
        {
            return [];
        }

        var settings = new List<ModSetting>();

        foreach (var file in CollectIniFiles(gamePath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            IniDocument document;
            try
            {
                document = await IniDocument.LoadAsync(file, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                continue;
            }

            var relative = Path.GetRelativePath(gamePath, file).Replace('\\', '/');
            var plugin = PluginName(relative);

            AddKeys(settings, document, relative, plugin, section: string.Empty);
            foreach (var section in document.Sections)
            {
                AddKeys(settings, document, relative, plugin, section);
            }
        }

        return settings;
    }

    private static void AddKeys(
        List<ModSetting> settings, IniDocument document, string relative, string plugin, string section)
    {
        foreach (var key in document.KeysIn(section))
        {
            var raw = (section.Length == 0 ? document.GetAnywhere(key) : document.Get(section, key)) ?? string.Empty;
            settings.Add(Build(relative, section, key, raw, plugin));
        }
    }

    private static IEnumerable<string> CollectIniFiles(string gamePath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;

        IEnumerable<string> FromRoot()
        {
            // Loose config files in the game root (TrainerV.ini and friends), then
            // everything under the mod folders.
            foreach (var file in SafeEnumerate(gamePath, SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }

            foreach (var root in Roots)
            {
                var dir = Path.Combine(gamePath, root);
                if (Directory.Exists(dir))
                {
                    foreach (var file in SafeEnumerate(dir, SearchOption.AllDirectories))
                    {
                        yield return file;
                    }
                }
            }
        }

        foreach (var file in FromRoot())
        {
            if (count >= MaxFiles)
            {
                yield break;
            }

            // Skip our own backups and anything oversized or unreadable.
            if (file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || !seen.Add(file))
            {
                continue;
            }

            long length;
            try
            {
                length = new FileInfo(file).Length;
            }
            catch (IOException)
            {
                continue;
            }

            if (length > MaxFileBytes)
            {
                continue;
            }

            count++;
            yield return file;
        }
    }

    private static IEnumerable<string> SafeEnumerate(string directory, SearchOption option)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.ini", option);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static ModSetting Build(string relative, string section, string key, string raw, string plugin)
    {
        var value = raw.Trim();
        var name = Humanize(key, stripSuffix: true);
        var category = section.Length == 0 ? "General" : Humanize(section, stripSuffix: false);

        var common = new
        {
            Id = $"scan:{relative}:{section}:{key}",
            Name = name,
            Description = $"Found in {relative}. Key: {key}.",
            Plugin = plugin,
            Category = category,
            File = relative,
            Section = section,
        };

        if (TryToggle(value, out var on, out var off))
        {
            return new ModSetting
            {
                Id = common.Id, Name = common.Name, Description = common.Description,
                Plugin = common.Plugin, Category = common.Category,
                ConfigFile = common.File, ConfigKey = key, Section = common.Section, Discovered = true,
                Kind = SettingKind.Toggle, OnLiteral = on, OffLiteral = off, Default = value,
            };
        }

        if (TryNumber(value, out var min, out var max, out var step))
        {
            return new ModSetting
            {
                Id = common.Id, Name = common.Name, Description = common.Description,
                Plugin = common.Plugin, Category = common.Category,
                ConfigFile = common.File, ConfigKey = key, Section = common.Section, Discovered = true,
                Kind = SettingKind.Number, Min = min, Max = max, Step = step, Default = value,
            };
        }

        var quoted = value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''));

        return new ModSetting
        {
            Id = common.Id, Name = common.Name, Description = common.Description,
            Plugin = common.Plugin, Category = common.Category,
            ConfigFile = common.File, ConfigKey = key, Section = common.Section, Discovered = true,
            Kind = SettingKind.Text, Quoted = quoted, Default = ModSetting.Unquote(value),
        };
    }

    private static readonly (string On, string Off)[] TogglePairs =
    [
        ("true", "false"),
        ("yes", "no"),
        ("on", "off"),
        ("enabled", "disabled"),
    ];

    private static bool TryToggle(string value, out string on, out string off)
    {
        on = off = string.Empty;
        var lower = ModSetting.Unquote(value).Trim().ToLowerInvariant();

        foreach (var (t, f) in TogglePairs)
        {
            if (lower == t)
            {
                on = MatchCase(value, t);
                off = MatchCase(value, f);
                return true;
            }

            if (lower == f)
            {
                on = MatchCase(value, t);
                off = MatchCase(value, f);
                return true;
            }
        }

        return false;
    }

    // Renders a lower-case word in the same case style as the value seen in the
    // file, so writing the opposite keeps YES/NO looking like YES/NO.
    private static string MatchCase(string sample, string lowerWord)
    {
        var trimmed = ModSetting.Unquote(sample).Trim();
        if (trimmed.All(char.IsUpper))
        {
            return lowerWord.ToUpperInvariant();
        }

        if (trimmed.Length > 0 && char.IsUpper(trimmed[0]) && trimmed.Skip(1).All(c => !char.IsUpper(c)))
        {
            return char.ToUpperInvariant(lowerWord[0]) + lowerWord[1..];
        }

        return lowerWord;
    }

    private static bool TryNumber(string value, out double min, out double max, out double step)
    {
        min = 0;
        max = 100;
        step = 1;

        if (!double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        var magnitude = Math.Abs(number);
        var cap = magnitude switch
        {
            <= 1 => 10d,
            <= 100 => 100d,
            <= 1000 => 2000d,
            <= 10000 => 20000d,
            _ => magnitude * 4,
        };

        min = number < 0 ? number * 2 : 0;
        max = Math.Max(cap, number + 1);
        step = value.Contains('.', StringComparison.Ordinal) ? 0.1 : 1;
        return true;
    }

    private static string PluginName(string relative)
    {
        var parts = relative.Split('/');
        var fileName = Path.GetFileNameWithoutExtension(parts[^1]);

        // A generic file name like "config.ini" says nothing; the parent folder is
        // the mod (GrammarPolice/custom/config.ini -> Grammar Police).
        if (GenericFileNames.Contains(fileName) && parts.Length > 1)
        {
            var folder = parts[^2];
            if (GenericFileNames.Contains(folder) && parts.Length > 2)
            {
                folder = parts[^3];
            }

            fileName = folder;
        }

        return Humanize(fileName, stripSuffix: false, title: true);
    }

    private static string Humanize(string raw, bool stripSuffix, bool title = false)
    {
        var text = raw.Trim();

        if (stripSuffix)
        {
            foreach (var suffix in new[] { "_Key", "_Button", "Key", "Button" })
            {
                if (text.Length > suffix.Length &&
                    text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    text = text[..^suffix.Length];
                    break;
                }
            }
        }

        text = text.Replace('_', ' ').Replace('-', ' ');
        text = Regex.Replace(text, "(\\p{Ll})(\\p{Lu})", "$1 $2");
        text = Regex.Replace(text, "(\\p{Lu}+)(\\p{Lu}\\p{Ll})", "$1 $2");
        text = Regex.Replace(text, "(\\p{L})([0-9])", "$1 $2");
        text = Regex.Replace(text, "([0-9])(\\p{L})", "$1 $2");
        text = Regex.Replace(text, "\\s+", " ").Trim();

        if (text.Length == 0)
        {
            return raw;
        }

        // Keep acronyms (MDT) and single letters (X) as they are. Title case caps
        // every ordinary word (a plugin name); sentence case lowers them and caps
        // only the first (a setting name).
        var result = string.Join(' ',
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => CaseWord(w, title)));

        return result.Length == 0 ? raw : char.ToUpperInvariant(result[0]) + result[1..];
    }

    private static string CaseWord(string word, bool title)
    {
        if (word.Length == 1)
        {
            return word;
        }

        var letters = word.Where(char.IsLetter).ToList();
        var isAcronym = letters.Count > 0 && letters.All(char.IsUpper);
        if (isAcronym)
        {
            return word;
        }

        var lower = word.ToLowerInvariant();
        return title ? char.ToUpperInvariant(lower[0]) + lower[1..] : lower;
    }
}
