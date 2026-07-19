using Avalonia.Media;
using Dispatch.UI.Imagery;

namespace Dispatch.UI.Wizard.Steps;

/// <summary>
/// Screen 1. Deliberately sparse: its only job is to make the app feel
/// considered before it asks for anything.
/// </summary>
public sealed class WelcomeStep : WizardStep
{
    /// <inheritdoc />
    public override string Title => "Welcome";

    /// <inheritdoc />
    public override string AdvanceLabel => "Get started";

    /// <summary>
    /// Photographic backdrop, heavily scrimmed. Null when no images are
    /// compiled in, in which case the screen falls back to vector.
    /// </summary>
    /// <remarks>
    /// Takes the last image in the pool rather than the first, so the welcome
    /// screen and the Standard Issue preset card are not showing the same
    /// photograph two clicks apart.
    /// </remarks>
    public IImage? Backdrop { get; } = ImageCatalog.For("welcome", -1);

    /// <summary>True when there is a photograph to show.</summary>
    public bool HasBackdrop => Backdrop is not null;
}
