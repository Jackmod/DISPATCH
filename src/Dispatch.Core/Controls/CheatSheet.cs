using System.Globalization;
using System.Text;
using Dispatch.Core.Profiles;

namespace Dispatch.Core.Controls;

/// <summary>
/// Turns a control scheme into a printable reference: every bound action in plain
/// words, grouped by what it is for, keyboard and controller apart.
/// </summary>
/// <remarks>
/// Forty-odd binds is more than anyone holds in their head, and the manual
/// alternative — reading them back out of thirty config files in four token
/// dialects — is the exact problem the app exists to remove. The sheet is built
/// from the same catalogue the controls screen renders, so it can never drift
/// from what is actually bound.
///
/// <para>
/// The output is Markdown rather than a bitmap: it reads fine as text, prints
/// cleanly, and the UI can render it to PDF or PNG when it exports. Unbound
/// actions are left out — a reference is for what you can do, not a census of
/// what you cannot.
/// </para>
/// </remarks>
public static class CheatSheet
{
    /// <summary>Builds the cheat sheet as Markdown for the given scheme and officer.</summary>
    /// <param name="bindings">The bound actions, typically <see cref="ControlCatalogue.Bind"/>.</param>
    /// <param name="officer">The officer, for the header. Null omits the identity line.</param>
    public static string BuildMarkdown(IReadOnlyList<BoundAction> bindings, OfficerProfile? officer = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var builder = new StringBuilder();
        builder.Append("# Dispatch — Control Cheat Sheet\n\n");

        if (officer is not null)
        {
            builder.Append(CultureInfo.InvariantCulture, $"**{officer.Callsign}** · {officer.Name} · {officer.AgencyCode}\n\n");
        }

        builder.Append("Every key you have bound, in plain words. Print it, tape it to the monitor.\n");

        AppendDevice(builder, bindings, InputDevice.Keyboard, "Keyboard");
        AppendDevice(builder, bindings, InputDevice.Controller, "Controller");

        builder.Append("\n---\n");
        builder.Append("_F4 opens the RagePluginHook console and is never rebound. Numpad binds need Num Lock on._\n");

        return builder.ToString();
    }

    /// <summary>Builds the cheat sheet as plain text, for a clipboard or a log.</summary>
    public static string BuildPlainText(IReadOnlyList<BoundAction> bindings, OfficerProfile? officer = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        // Markdown with the table pipes and heading marks stripped reads perfectly
        // well as plain text, and keeps a single source for the content.
        var lines = BuildMarkdown(bindings, officer)
            .Replace("# ", string.Empty, StringComparison.Ordinal)
            .Replace("## ", string.Empty, StringComparison.Ordinal)
            .Replace("### ", string.Empty, StringComparison.Ordinal)
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        return lines;
    }

    private static void AppendDevice(
        StringBuilder builder,
        IReadOnlyList<BoundAction> bindings,
        InputDevice device,
        string heading)
    {
        var bound = bindings
            .Where(b => b.Action.Device == device && !b.Binding.IsUnbound)
            .ToList();

        if (bound.Count == 0)
        {
            return;
        }

        builder.Append(CultureInfo.InvariantCulture, $"\n## {heading}\n");

        foreach (var category in bound
                     .Select(b => b.Action.Category)
                     .Distinct()
                     .OrderBy(c => c, StringComparer.Ordinal))
        {
            builder.Append(CultureInfo.InvariantCulture, $"\n### {category}\n\n");
            builder.Append("| Action | Key |\n|---|---|\n");

            foreach (var entry in bound
                         .Where(b => b.Action.Category == category)
                         .OrderBy(b => b.Action.Name, StringComparer.Ordinal))
            {
                builder.Append(CultureInfo.InvariantCulture, $"| {entry.Action.Name} | {entry.Binding.Display} |\n");
            }
        }
    }
}
