using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Dispatch.UI.Launcher;

/// <summary>
/// The cleaner modal. Code-behind wires the scroll-to-bottom gate, which is raw
/// scroll geometry rather than a bindable value.
/// </summary>
public partial class CleanerView : UserControl
{
    /// <summary>Raised when the user asks to close the modal.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Constructs the view.</summary>
    public CleanerView()
    {
        InitializeComponent();

        CloseButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        PreviewScroll.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not CleanerViewModel model || sender is not ScrollViewer viewer)
        {
            return;
        }

        // Bottom, within a few pixels — or the content is short enough that
        // there is nothing to scroll, in which case they have already seen it
        // all. Both count as "looked".
        var atBottom = viewer.Offset.Y >= viewer.Extent.Height - viewer.Viewport.Height - 4;
        var nothingToScroll = viewer.Extent.Height <= viewer.Viewport.Height;

        if (atBottom || nothingToScroll)
        {
            model.MarkScrolledToBottomCommand.Execute(null);
        }
    }
}
