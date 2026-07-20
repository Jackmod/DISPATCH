using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Dispatch.Core.Maintenance;
using Dispatch.UI.Controls;
using Dispatch.UI.Launcher;

namespace Dispatch.UI;

/// <summary>
/// Maps a <see cref="StatusTone"/> to its theme brush.
/// </summary>
/// <remarks>
/// A converter rather than a per-tone template because tone drives colour in
/// several places — status pills, dashboard tiles, the audit report — and the
/// mapping has to be the same in all of them. Resolving from theme resources
/// keeps it swapping with the palette rather than hard-coding hex.
/// </remarks>
public sealed class ToneToBrushConverter : IValueConverter
{
    /// <summary>The shared instance referenced from XAML.</summary>
    public static readonly ToneToBrushConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            StatusTone.Good => "Green",
            StatusTone.Warning => "Amber",
            StatusTone.Bad => "Red",
            StatusTone.Active => "Gold",
            _ => "TextMuted",
        };

        return Application.Current?.TryGetResource(key, null, out var brush) == true
            ? brush as IBrush
            : Brushes.Gray;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps an <see cref="AuditSeverity"/> to its theme brush, for the repair report.</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    /// <summary>The shared instance referenced from XAML.</summary>
    public static readonly SeverityToBrushConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            AuditSeverity.Problem => "Red",
            AuditSeverity.Warning => "Amber",
            _ => "Green",
        };

        return Application.Current?.TryGetResource(key, null, out var brush) == true
            ? brush as IBrush
            : Brushes.Gray;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
