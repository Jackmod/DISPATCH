using Avalonia.Data.Converters;

namespace Dispatch.UI.Launcher;

/// <summary>
/// Stage comparisons for the cleaner modal, so each pane shows only in its
/// stage.
/// </summary>
/// <remarks>
/// One converter per stage rather than a parameterised one, because XAML
/// bindings read more clearly as <c>IsScanning</c> than as a converter with a
/// magic parameter, and the set is small and fixed.
/// </remarks>
public static class CleanerConverters
{
    /// <summary>True while scanning.</summary>
    public static readonly IValueConverter IsScanning =
        new FuncValueConverter<CleanerStage, bool>(s => s == CleanerStage.Scanning);

    /// <summary>True while showing the preview.</summary>
    public static readonly IValueConverter IsPreview =
        new FuncValueConverter<CleanerStage, bool>(s => s == CleanerStage.Preview);

    /// <summary>True while moving files.</summary>
    public static readonly IValueConverter IsCleaning =
        new FuncValueConverter<CleanerStage, bool>(s => s == CleanerStage.Cleaning);

    /// <summary>True on the done summary.</summary>
    public static readonly IValueConverter IsDone =
        new FuncValueConverter<CleanerStage, bool>(s => s == CleanerStage.Done);
}
