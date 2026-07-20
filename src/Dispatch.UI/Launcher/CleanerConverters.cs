using Avalonia.Data.Converters;

namespace Dispatch.UI.Launcher;

/// <summary>
/// Stage comparisons for the cleaner modal, so each pane shows only in its
/// stage.
/// </summary>
/// <remarks>
/// One converter per stage rather than a parameterised one, because XAML
/// bindings read more clearly as <c>IsWarning</c> than as a converter with a
/// magic parameter, and the set is small and fixed.
/// </remarks>
public static class CleanerConverters
{
    /// <summary>True while scanning.</summary>
    public static readonly IValueConverter IsScanning =
        new FuncValueConverter<CleanerStage, bool>(s => s == CleanerStage.Scanning);

    /// <summary>True on the warning step, before anything moves.</summary>
    public static readonly IValueConverter IsWarning =
        new FuncValueConverter<CleanerStage, bool>(s => s == CleanerStage.Warning);

    /// <summary>True while moving files.</summary>
    public static readonly IValueConverter IsCleaning =
        new FuncValueConverter<CleanerStage, bool>(s => s == CleanerStage.Cleaning);

    /// <summary>True on the final verify step.</summary>
    public static readonly IValueConverter IsVerify =
        new FuncValueConverter<CleanerStage, bool>(s => s == CleanerStage.Verify);
}
