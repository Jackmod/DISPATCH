using Avalonia.Controls;

namespace Dispatch.UI.Launcher;

/// <summary>
/// The cleaner modal. Code-behind only bridges the two close paths — the Cancel
/// and empty-state buttons, and the view model's own <c>CloseRequested</c> that
/// the Done button raises once verify is ticked — onto the single event the host
/// listens to.
/// </summary>
public partial class CleanerView : UserControl
{
    private CleanerViewModel? _model;

    /// <summary>Raised when the modal should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Constructs the view.</summary>
    public CleanerView()
    {
        InitializeComponent();

        CancelButton.Click += RequestClose;
        CloseWhenEmptyButton.Click += RequestClose;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_model is not null)
        {
            _model.CloseRequested -= RequestClose;
        }

        _model = DataContext as CleanerViewModel;
        if (_model is not null)
        {
            _model.CloseRequested += RequestClose;
        }
    }

    private void RequestClose(object? sender, EventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
