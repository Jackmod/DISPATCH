using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Dispatch.UI.Wizard.Steps;

namespace Dispatch.UI.Wizard.Views;

/// <summary>Screen 3. Detected installs, validated on selection.</summary>
public partial class LocateGameView : UserControl
{
    /// <summary>Constructs the view.</summary>
    public LocateGameView()
    {
        InitializeComponent();
        CleanerOverlay.CloseRequested += (_, _) => (DataContext as LocateGameStep)?.CloseCleaner();
        DefenderButton.Click += async (_, _) =>
        {
            if (DataContext is LocateGameStep step)
            {
                await step.AddDefenderExclusionAsync();
            }
        };
    }

    /// <summary>Opens a folder picker and adds the chosen folder as a candidate.</summary>
    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LocateGameStep step)
        {
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select your Grand Theft Auto V folder",
            AllowMultiple = false,
        });

        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            step.AddFolder(path);
        }
    }

    /// <summary>Opens the cleaner against the selected folder.</summary>
    private async void OnClean(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LocateGameStep step)
        {
            await step.OpenCleanerAsync();
        }
    }
}
