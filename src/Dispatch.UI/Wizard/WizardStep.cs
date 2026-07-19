using CommunityToolkit.Mvvm.ComponentModel;

namespace Dispatch.UI.Wizard;

/// <summary>
/// Base for the six wizard screens.
/// </summary>
/// <remarks>
/// Each screen owns its own presentation state but never the wizard's data —
/// that lives on <see cref="WizardViewModel"/> and is passed down, so Back
/// never destroys anything the user entered.
/// </remarks>
public abstract partial class WizardStep : ObservableObject
{
    /// <summary>Short label shown against the progress rail.</summary>
    public abstract string Title { get; }

    /// <summary>
    /// Whether the wizard may advance from this screen. Screens that require a
    /// choice override this and raise a change when the choice is made.
    /// </summary>
    public virtual bool CanAdvance => true;

    /// <summary>Label for the forward button. Most screens just say Continue.</summary>
    public virtual string AdvanceLabel => "Continue";

    /// <summary>
    /// Whether the footer navigation is shown at all. The install screen hides
    /// it: there is nothing to go back to mid-run, and the run advances itself.
    /// </summary>
    public virtual bool ShowNavigation => true;

    /// <summary>Called each time the screen becomes current.</summary>
    public virtual void OnEntered()
    {
    }
}
