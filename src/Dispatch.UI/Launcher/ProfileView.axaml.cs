using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Dispatch.UI.Launcher;

/// <summary>
/// The profile section. Loads the career record when shown, and owns the file
/// picker for the portrait — raw platform storage, which belongs at the view.
/// </summary>
public partial class ProfileView : UserControl
{
    /// <summary>Constructs the view.</summary>
    public ProfileView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfileViewModel model)
        {
            await model.EnsureLoadedAsync();
        }
    }

    private async void OnChangePhoto(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfileViewModel model)
        {
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a profile picture",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (!string.IsNullOrWhiteSpace(path))
        {
            await model.SetAvatarAsync(path);
        }
    }
}
