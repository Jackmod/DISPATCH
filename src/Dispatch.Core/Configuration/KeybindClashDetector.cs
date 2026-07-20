namespace Dispatch.Core.Configuration;

/// <summary>One key bound to more than one action across the config set.</summary>
/// <param name="Binding">The key combination, e.g. "Left Shift + X".</param>
/// <param name="Actions">The settings that bind it.</param>
public sealed record KeybindClash(string Binding, IReadOnlyList<string> Actions);

/// <summary>
/// Checks that the guide's keybinds do not collide — the whole reason its values
/// are so specific.
/// </summary>
/// <remarks>
/// A "binding" is a key plus its modifier, paired within a mod: a
/// <c>… Key</c> or <c>… Button</c> setting joined to the <c>… Modifier …</c>
/// setting that shares its prefix. Two different actions landing on the same
/// binding is a clash. Some keys are shared on purpose — the LSPDFR interaction
/// family all sits on <c>I</c>, and pressing it does the one contextually right
/// thing — so those are allow-listed rather than flagged.
/// </remarks>
public static class KeybindClashDetector
{
    // Keys the guide deliberately shares across actions; not clashes.
    private static readonly HashSet<string> IntentionallyShared = new(StringComparer.OrdinalIgnoreCase)
    {
        "I",   // arrest / stop ped / traffic stop interact — one contextual action
        "None", "NONE", "0",
    };

    /// <summary>Finds every binding used by more than one action.</summary>
    public static IReadOnlyList<KeybindClash> Detect(IEnumerable<ModConfig> configs)
    {
        ArgumentNullException.ThrowIfNull(configs);

        var bindings = new Dictionary<string, List<(string Mod, string Label)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var config in configs)
        {
            foreach (var setting in config.Settings)
            {
                if (!IsKeybind(setting) || IntentionallyShared.Contains(setting.Value.Trim()))
                {
                    continue;
                }

                var modifier = FindModifier(config, setting);
                var binding = string.IsNullOrEmpty(modifier)
                    ? setting.Value.Trim()
                    : $"{modifier} + {setting.Value.Trim()}";

                if (!bindings.TryGetValue(binding, out var list))
                {
                    bindings[binding] = list = [];
                }

                list.Add((config.ModId, $"{config.ModId}:{setting.Name}"));
            }
        }

        // A clash is a binding used by two different mods. Two bindings within one
        // mod — a spotlight toggled by keyboard or mouse, say — are one action the
        // author put on two inputs, not a collision.
        return bindings
            .Where(b => b.Value.Select(v => v.Mod).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(b => new KeybindClash(b.Key, b.Value.Select(v => v.Label).ToList()))
            .ToList();
    }

    private static bool IsKeybind(ConfigSetting setting)
    {
        var name = setting.Name;
        var isKeyName = name.EndsWith("Key", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith("Button", StringComparison.OrdinalIgnoreCase);

        // A modifier is part of a binding, not a binding of its own.
        var isModifier = name.Contains("Modifier", StringComparison.OrdinalIgnoreCase);

        return isKeyName && !isModifier && !string.IsNullOrWhiteSpace(setting.Value);
    }

    /// <summary>
    /// The modifier paired with a key setting: the same mod's "… Modifier …"
    /// setting whose remaining words match the key's, e.g. "Give Citation Key"
    /// pairs with "Give Citation Modifier Key".
    /// </summary>
    private static string? FindModifier(ModConfig config, ConfigSetting key)
    {
        var prefix = IniConfigWriter.Normalise(
            key.Name.Replace("Key", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("Button", string.Empty, StringComparison.OrdinalIgnoreCase));

        var modifier = config.Settings.FirstOrDefault(s =>
            s.Name.Contains("Modifier", StringComparison.OrdinalIgnoreCase)
            && IniConfigWriter.Normalise(
                    s.Name.Replace("Modifier", string.Empty, StringComparison.OrdinalIgnoreCase)
                          .Replace("Key", string.Empty, StringComparison.OrdinalIgnoreCase)
                          .Replace("Button", string.Empty, StringComparison.OrdinalIgnoreCase))
                == prefix);

        return modifier is null || IntentionallyShared.Contains(modifier.Value.Trim())
            ? null
            : modifier.Value.Trim();
    }
}
