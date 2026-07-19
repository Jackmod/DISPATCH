namespace Dispatch.Core.Controls;

/// <summary>Which device an action is bound on. Separate namespaces entirely.</summary>
public enum InputDevice
{
    /// <summary>Keyboard and mouse.</summary>
    Keyboard,

    /// <summary>Gamepad.</summary>
    Controller,
}

/// <summary>
/// One thing a player can do, and where its binding lives.
/// </summary>
/// <param name="Id">Stable identifier, unique across all mods.</param>
/// <param name="Name">What it does, in plain words: <c>Arrest suspect</c>.</param>
/// <param name="Description">A sentence explaining it, shown on hover.</param>
/// <param name="Plugin">Which mod owns it.</param>
/// <param name="Category">Grouping for the filter chips.</param>
/// <param name="Device">Keyboard or controller.</param>
/// <param name="ConfigFile">Path, relative to the game folder, of the file holding it.</param>
/// <param name="ConfigKey">The key within that file.</param>
/// <param name="Dialect">Which token format that file uses.</param>
public sealed record GameAction(
    string Id,
    string Name,
    string Description,
    string Plugin,
    string Category,
    InputDevice Device,
    string ConfigFile,
    string ConfigKey,
    KeyDialect Dialect = KeyDialect.WinForms)
{
    /// <summary>
    /// Bindings that are reserved and cannot simply be reassigned.
    /// </summary>
    /// <remarks>
    /// F4 is the RagePluginHook console. It is not a mod's bind to give away —
    /// rebinding it removes the only way into the console, which is where
    /// people go when something has gone wrong.
    /// </remarks>
    public static bool IsReserved(KeyBinding binding) =>
        binding.Modifier == KeyModifier.None && binding.Key.Canonical == "F4";
}

/// <summary>An action together with what it is currently bound to.</summary>
/// <param name="Action">The action.</param>
/// <param name="Binding">Its current binding.</param>
public sealed record BoundAction(GameAction Action, KeyBinding Binding);

/// <summary>Two or more actions competing for the same input.</summary>
/// <param name="Binding">The contested binding.</param>
/// <param name="Actions">Everything bound to it, in catalogue order.</param>
public sealed record Conflict(KeyBinding Binding, IReadOnlyList<GameAction> Actions)
{
    /// <summary>How many actions are competing.</summary>
    public int Count => Actions.Count;

    /// <summary>A one-line summary, for the conflict chip and the report.</summary>
    public string Summary =>
        $"{Binding.Display} is bound to {string.Join(", ", Actions.Select(a => a.Name))}";
}

/// <summary>
/// Finds bindings that more than one action claims.
/// </summary>
/// <remarks>
/// Three rules do the work, and each exists because getting it wrong produces
/// a specific wrong answer:
///
/// <list type="number">
/// <item>Unbound never conflicts. Thirty actions set to None are not a
/// thirty-way conflict, they are thirty things nobody has bound.</item>
/// <item>Keyboard and controller are separate namespaces. <c>X</c> on the
/// keyboard and <c>X</c> on the gamepad are different inputs, and reporting
/// them as one is how a working scheme gets flagged as broken.</item>
/// <item>Modifiers are part of the identity. <c>Left Shift + X</c> does not
/// conflict with a bare <c>X</c>, which is the whole reason the keyboard map
/// has layers.</item>
/// </list>
/// </remarks>
public static class ConflictDetector
{
    /// <summary>Finds every conflict in a set of bound actions.</summary>
    public static IReadOnlyList<Conflict> Detect(IEnumerable<BoundAction> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        return bindings
            .Where(bound => !bound.Binding.IsUnbound)
            .GroupBy(bound => (bound.Action.Device, bound.Binding))
            .Where(group => group.Count() > 1)
            .Select(group => new Conflict(
                group.Key.Binding,
                group.Select(bound => bound.Action).ToList()))
            .OrderBy(conflict => conflict.Binding.Display, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>True when this action's binding collides with any other.</summary>
    public static bool IsInConflict(BoundAction candidate, IEnumerable<BoundAction> all)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(all);

        if (candidate.Binding.IsUnbound)
        {
            return false;
        }

        return all.Any(other =>
            !ReferenceEquals(other, candidate) &&
            other.Action.Id != candidate.Action.Id &&
            other.Action.Device == candidate.Action.Device &&
            other.Binding == candidate.Binding);
    }

    /// <summary>
    /// Suggests a free key on the same device, preferring ones that are easy to
    /// reach and unlikely to be wanted by something else.
    /// </summary>
    /// <returns>A free binding, or unbound when the candidate pool is exhausted.</returns>
    public static KeyBinding SuggestFree(
        IEnumerable<BoundAction> all,
        InputDevice device = InputDevice.Keyboard)
    {
        ArgumentNullException.ThrowIfNull(all);

        var taken = all
            .Where(bound => bound.Action.Device == device && !bound.Binding.IsUnbound)
            .Select(bound => bound.Binding)
            .ToHashSet();

        foreach (var candidate in CandidatePool(device))
        {
            // F4 is never offered; it belongs to the RagePluginHook console.
            if (!taken.Contains(candidate) && !GameAction.IsReserved(candidate))
            {
                return candidate;
            }
        }

        return KeyBinding.Unbound;
    }

    /// <summary>
    /// Keys offered as replacements, in preference order.
    /// </summary>
    /// <remarks>
    /// Function keys first because they are unshared and easy to hit, then the
    /// numpad, then Shift chords on letters. Bare letters are deliberately last
    /// — most of them are already doing something in the base game.
    /// </remarks>
    private static IEnumerable<KeyBinding> CandidatePool(InputDevice device)
    {
        if (device == InputDevice.Controller)
        {
            foreach (var input in new[]
                     {
                         "DPadUp", "DPadDown", "DPadLeft", "DPadRight",
                         "LeftShoulder", "RightShoulder", "LeftThumb", "RightThumb",
                     })
            {
                yield return new KeyBinding(new KeyToken(input));
            }

            yield break;
        }

        for (var i = 5; i <= 12; i++)
        {
            yield return new KeyBinding(new KeyToken($"F{i}"));
        }

        for (var i = 0; i <= 9; i++)
        {
            yield return new KeyBinding(new KeyToken($"NumPad{i}"));
        }

        foreach (var letter in "QWERTYUIOPASDFGHJKLZXCVBNM")
        {
            yield return new KeyBinding(new KeyToken(letter.ToString()), KeyModifier.Shift);
        }

        foreach (var letter in "QWERTYUIOPASDFGHJKLZXCVBNM")
        {
            yield return new KeyBinding(new KeyToken(letter.ToString()));
        }
    }
}
