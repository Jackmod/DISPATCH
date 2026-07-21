using Microsoft.Extensions.Logging;

namespace Dispatch.Core.Configuration;

/// <summary>What a config write did to one setting.</summary>
/// <param name="Setting">The setting name from the guide.</param>
/// <param name="Applied">True when at least one key was written.</param>
/// <param name="KeysWritten">The real keys that were changed.</param>
public sealed record SettingOutcome(string Setting, bool Applied, IReadOnlyList<string> KeysWritten);

/// <summary>The result of applying a set of settings to one config file.</summary>
/// <param name="Changed">Whether the file's bytes actually changed.</param>
/// <param name="Outcomes">One entry per setting attempted.</param>
public sealed record ConfigResult(bool Changed, IReadOnlyList<SettingOutcome> Outcomes)
{
    /// <summary>Settings that matched no key in the file.</summary>
    public IReadOnlyList<string> Unmatched =>
        Outcomes.Where(o => !o.Applied).Select(o => o.Setting).ToList();
}

/// <summary>
/// Applies the guide's settings to an ini file by matching each setting name to
/// the file's real key, whatever its exact spelling.
/// </summary>
/// <remarks>
/// The match is by normalised name — case, spaces and punctuation stripped from
/// both sides — because mod authors name their keys after the same setting the
/// guide does, just with different spacing or casing ("Open Computer Key" versus
/// <c>OpenComputerKey</c>). Nothing is invented: a setting that matches no key is
/// reported and skipped, never inserted, so an unfamiliar file is left valid.
/// Every write goes through <see cref="IniDocument"/>, so comments, ordering and
/// encoding survive untouched.
/// </remarks>
public sealed class IniConfigWriter
{
    private readonly ILogger<IniConfigWriter> _logger;

    /// <summary>Constructs the writer.</summary>
    public IniConfigWriter(ILogger<IniConfigWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Applies settings to a document, filling officer placeholders.</summary>
    public ConfigResult Apply(IniDocument document, IEnumerable<ConfigSetting> settings, OfficerValues officer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(officer);

        // Index every key once: normalised name -> the (section, real key) pairs
        // that carry it, across the root section and every named section.
        var index = BuildIndex(document);

        var outcomes = new List<SettingOutcome>();
        var changed = false;

        foreach (var setting in settings)
        {
            var value = officer.Fill(setting.Value);
            var target = Normalise(setting.Name);
            var written = new List<string>();

            // A section-scoped setting only sees keys in that section, so a name that
            // repeats across sections (Spotlight's Toggle) resolves to the right one.
            var scope = setting.Section is { } wanted
                ? index.Where(e => string.Equals(e.Section, wanted, StringComparison.OrdinalIgnoreCase)).ToList()
                : index;

            foreach (var (section, key) in Resolve(scope, target, setting.Match))
            {
                if (document.Set(section, key, value))
                {
                    changed = true;
                }

                written.Add(key);
            }

            if (written.Count == 0)
            {
                _logger.LogDebug("Config setting '{Setting}' matched no key", setting.Name);
            }

            outcomes.Add(new SettingOutcome(setting.Name, written.Count > 0, written));
        }

        return new ConfigResult(changed, outcomes);
    }

    private static IEnumerable<(string Section, string Key)> Resolve(
        IReadOnlyList<(string Normalised, string Section, string Key)> index,
        string target,
        ConfigMatch match)
    {
        return match switch
        {
            ConfigMatch.Exact => index
                .Where(e => e.Normalised == target)
                .Select(e => (e.Section, e.Key))
                // A single deterministic key: the first in file order.
                .Take(1),

            ConfigMatch.Contains => index
                .Where(e => e.Normalised.Contains(target, StringComparison.Ordinal))
                .Select(e => (e.Section, e.Key)),

            _ => [],
        };
    }

    private static List<(string Normalised, string Section, string Key)> BuildIndex(IniDocument document)
    {
        var index = new List<(string Normalised, string Section, string Key)>();

        // The root section is the empty string; include it alongside the named ones.
        var sections = new List<string> { string.Empty };
        sections.AddRange(document.Sections);

        foreach (var section in sections.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var key in document.KeysIn(section))
            {
                index.Add((Normalise(key), section, key));
            }
        }

        return index;
    }

    /// <summary>Reduces a name to letters and digits, lower-cased, for matching.</summary>
    internal static string Normalise(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
            }
        }

        return new string(buffer[..length]);
    }
}
