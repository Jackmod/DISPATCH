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
}
