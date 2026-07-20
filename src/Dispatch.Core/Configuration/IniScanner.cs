using System.Globalization;
using System.Text.RegularExpressions;
using Dispatch.Core.Controls;

namespace Dispatch.Core.Configuration;

/// <summary>A keybind found by scanning a config file, ready for the controls screen.</summary>
/// <param name="Action">The discovered action, with its file, key, device and dialect.</param>
/// <param name="Binding">Its current binding, as the file holds it.</param>
public sealed record DiscoveredKeybind(GameAction Action, KeyBinding Binding);

/// <summary>Everything a scan found, split by what it is.</summary>
/// <param name="Settings">Plain plugin settings — toggles, numbers, text.</param>
/// <param name="Keybinds">Keyboard and controller binds, for the controls screen.</param>
public sealed record ConfigScan(
    IReadOnlyList<ModSetting> Settings,
    IReadOnlyList<DiscoveredKeybind> Keybinds);

/// <summary>Finds editable settings and keybinds in whatever config files a game folder holds.</summary>
public interface IIniScanner
{
    /// <summary>
    /// Walks the mod folders, parses every config file, and returns each key as a
    /// setting the launcher can show and edit — including mods not in the catalogue.
    /// Keybinds are not included here; they belong to the controls screen and come
    /// back from <see cref="ScanAllAsync"/>.
    /// </summary>
    Task<IReadOnlyList<ModSetting>> ScanAsync(string gamePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks the mod folders and classifies every key it finds: a keyboard bind, a
    /// controller bind, or a plain setting. This is what lets one ini file feed the
    /// keyboard tab, the controller tab and the plugin-settings screen at once.
    /// </summary>
    Task<ConfigScan> ScanAllAsync(string gamePath, CancellationToken cancellationToken = default);
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
    private static readonly string[] Roots = ["plugins", "scripts", "lspdfr", "ragepluginhook"];
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
    public async Task<IReadOnlyList<ModSetting>> ScanAsync(string gamePath, CancellationToken cancellationToken = default) =>
        (await ScanAllAsync(gamePath, cancellationToken).ConfigureAwait(false)).Settings;

    /// <inheritdoc />
    public async Task<ConfigScan> ScanAllAsync(string gamePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        if (!Directory.Exists(gamePath))
        {
            return new ConfigScan([], []);
        }

        var settings = new List<ModSetting>();
        var keybinds = new List<DiscoveredKeybind>();

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

            ClassifyFile(document, relative, plugin, settings, keybinds);
        }

        return new ConfigScan(settings, keybinds);
    }

    /// <summary>
    /// Splits one file's keys into settings and binds. A key is read section by
    /// section so a modifier companion is found beside its bind, and each key is
    /// routed by <see cref="Classify"/> to the keyboard tab, the controller tab, or
    /// the settings list.
    /// </summary>
    private static void ClassifyFile(
        IniDocument document, string relative, string plugin,
        List<ModSetting> settings, List<DiscoveredKeybind> keybinds)
    {
        foreach (var section in new[] { string.Empty }.Concat(document.Sections))
        {
            var keys = document.KeysIn(section);

            // The section's raw values up front, so "<bind>Modifier" can be folded
            // into its bind rather than listed as a setting of its own.
            var raws = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                raws[key] = document.Get(section, key) ?? string.Empty;
            }

            foreach (var key in keys)
            {
                var raw = raws[key];

                if (IsModifierCompanion(key, raws))
                {
                    continue;
                }

                switch (Classify(key, raw))
                {
                    case KeyClass.ControllerKeybind:
                        keybinds.Add(BuildKeybind(
                            relative, section, key, raw, plugin, InputDevice.Controller, KeyDialect.Controller, raws));
                        break;

                    case KeyClass.KeyboardKeybind:
                        keybinds.Add(BuildKeybind(
                            relative, section, key, raw, plugin, InputDevice.Keyboard, InferKeyboardDialect(raw), raws));
                        break;

                    default:
                        settings.Add(Build(relative, section, key, raw, plugin));
                        break;
                }
            }
        }
    }

    // ===== Keybind classification ========================================

    private enum KeyClass
    {
        Setting,
        KeyboardKeybind,
        ControllerKeybind,
    }

    // Controller inputs unambiguous enough to identify a bind by value alone, even
    // when the key name gives no hint. The face buttons (A/B/X/Y) and Start/Back are
    // left out on purpose — they double as letters and words a setting might hold.
    private static readonly HashSet<string> StrongControllerTokens = new(StringComparer.Ordinal)
    {
        "DPadUp", "DPadDown", "DPadLeft", "DPadRight",
        "LeftShoulder", "RightShoulder", "LeftTrigger", "RightTrigger", "LeftThumb", "RightThumb",
    };

    // Every canonical keyboard token the display table knows, so a WinForms name
    // like "LShiftKey" reads as a plausible bind while an arbitrary string does not.
    private static readonly HashSet<string> KeyboardDisplayTokens =
        KeyTokens.KnownTokens.Where(k => !new KeyToken(k).IsControllerInput).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Decides whether a key is a keyboard bind, a controller bind, or a plain
    /// setting. Conservative by design: a key becomes a bind only when both its name
    /// and its value agree, or the value alone is unmistakably a controller input, so
    /// an ordinary setting is never mistaken for a bind.
    /// </summary>
    private static KeyClass Classify(string key, string raw)
    {
        var value = ModSetting.Unquote(raw).Trim();

        if (EndsWithAny(key, "Button", "Btn") && IsPlausibleControllerValue(value))
        {
            return KeyClass.ControllerKeybind;
        }

        if (StrongControllerTokens.Contains(KeyTokens.Parse(value, KeyDialect.Controller).Canonical))
        {
            return KeyClass.ControllerKeybind;
        }

        if (EndsWithAny(key, "Key", "Hotkey", "Keybind", "KeyBind") && IsPlausibleKeyboardValue(value))
        {
            return KeyClass.KeyboardKeybind;
        }

        return KeyClass.Setting;
    }

    /// <summary>
    /// Whether a key is the <c>&lt;bind&gt;Modifier</c> companion of a keyboard bind
    /// in the same section, in which case it is folded into that bind, not listed.
    /// </summary>
    private static bool IsModifierCompanion(string key, IReadOnlyDictionary<string, string> raws)
    {
        const string suffix = "Modifier";
        if (!key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || key.Length <= suffix.Length)
        {
            return false;
        }

        var baseKey = key[..^suffix.Length];
        return raws.TryGetValue(baseKey, out var baseRaw)
            && Classify(baseKey, baseRaw) == KeyClass.KeyboardKeybind;
    }

    private static DiscoveredKeybind BuildKeybind(
        string relative, string section, string key, string raw, string plugin,
        InputDevice device, KeyDialect dialect, IReadOnlyDictionary<string, string> raws)
    {
        var action = new GameAction(
            Id: $"scan:{relative}:{section}:{key}",
            Name: Humanize(key, stripSuffix: true),
            Description: $"Found in {relative}. Key: {key}.",
            Plugin: plugin,
            Category: section.Length == 0 ? "Discovered" : Humanize(section, stripSuffix: false),
            Device: device,
            ConfigFile: relative,
            ConfigKey: key,
            Dialect: dialect);

        var token = KeyTokens.Parse(raw, dialect);

        // Only a keyboard bind carries a modifier field, and only when its file has
        // one; a controller bind is a single button.
        var modifier = device == InputDevice.Keyboard && raws.TryGetValue(key + "Modifier", out var modRaw)
            ? ControlWriter.ParseModifier(modRaw)
            : KeyModifier.None;

        return new DiscoveredKeybind(action, new KeyBinding(token, modifier));
    }

    /// <summary>
    /// The dialect a discovered keyboard value is written in — which only matters for
    /// number-row digits, where a bare <c>9</c> must not be rewritten as <c>D9</c>.
    /// </summary>
    private static KeyDialect InferKeyboardDialect(string raw)
    {
        var value = ModSetting.Unquote(raw).Trim();
        return value.Length == 1 && char.IsAsciiDigit(value[0]) ? KeyDialect.Bare : KeyDialect.WinForms;
    }

    private static bool IsPlausibleKeyboardValue(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var token = KeyTokens.Parse(value);
        if (token.IsUnbound)
        {
            return true;
        }

        var canonical = token.Canonical;

        if (canonical.Length == 1 && char.IsAsciiLetter(canonical[0]))
        {
            return true;
        }

        if (canonical.Length == 2 && canonical[0] == 'D' && char.IsAsciiDigit(canonical[1]))
        {
            return true;
        }

        if (canonical.StartsWith("NumPad", StringComparison.Ordinal))
        {
            return true;
        }

        if (canonical.Length is >= 2 and <= 3 && canonical[0] == 'F'
            && int.TryParse(canonical.AsSpan(1), out var function) && function is >= 1 and <= 24)
        {
            return true;
        }

        return KeyboardDisplayTokens.Contains(canonical);
    }

    private static bool IsPlausibleControllerValue(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var token = KeyTokens.Parse(value, KeyDialect.Controller);
        return token.IsUnbound || token.IsControllerInput;
    }

    private static bool EndsWithAny(string text, params string[] suffixes) =>
        suffixes.Any(suffix => text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

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
