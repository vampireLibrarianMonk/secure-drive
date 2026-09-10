using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace EmergencyArchive.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => ViewModel.Lock();
        Opened += (_, _) => FocusPasswordBox();

        // When the archive locks (button or window close), put the caret back
        // into the password box so the next attempt can start immediately.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainWindowViewModel.IsUnlocked) && !vm.IsUnlocked)
                    {
                        Dispatcher.UIThread.Post(FocusPasswordBox);
                    }
                };
            }
        };
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    private void FocusPasswordBox() => PasswordBox.Focus();

    private void OnOpenClicked(object? sender, RoutedEventArgs e) => OpenSelectedDocument();

    private void OnDocumentDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (ViewModel.SelectedDocument is not null)
        {
            OpenSelectedDocument();
        }
    }

    private void OpenSelectedDocument()
    {
        DocumentItemViewModel? document = ViewModel.SelectedDocument;
        if (document is null)
        {
            return;
        }

        try
        {
            string tempDirectory = ViewModel.EnsureOpenTempDirectory();
            string targetPath = Path.Combine(tempDirectory, document.Name);
            ViewModel.ExportDocumentTo(document.RelativePath, targetPath);
            ViewModel.NoteExternalOpen(document.Name);

            Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Could not open '{document.Name}': {ex.Message}";
        }
    }

    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        DocumentItemViewModel? document = ViewModel.SelectedDocument;
        if (document is null)
        {
            return;
        }

        IStorageProvider? storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export document",
            SuggestedFileName = document.Name,
        });

        if (file is null)
        {
            return;
        }

        try
        {
            await using var target = await file.OpenWriteAsync();
            ViewModel.ExportDocumentTo(document.RelativePath, target);
            ViewModel.NoteExport(document.Name);
            ViewModel.StatusMessage = $"Exported '{document.Name}'. Remember where you saved it — the copy is NOT encrypted.";
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Could not export '{document.Name}': {ex.Message}";
        }
    }

    private async void OnAddSourceClicked(object? sender, RoutedEventArgs e)
    {
        var setup = ViewModel.Setup;
        if (setup is null)
        {
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a source directory",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } localPath)
        {
            setup.AddSourceFromPath(localPath);
        }
    }

    private async void OnAddFilesClicked(object? sender, RoutedEventArgs e)
    {
        var setup = ViewModel.Setup;
        if (setup is null)
        {
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add document(s) to the archive",
            AllowMultiple = true,
        });

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        if (paths.Count > 0)
        {
            setup.AddFilesFromPaths(paths);
        }
    }

    private async void OnReplaceDocumentClicked(object? sender, RoutedEventArgs e)
    {
        var setup = ViewModel.Setup;
        if (setup is null)
        {
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the replacement file",
            AllowMultiple = false,
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } localPath)
        {
            setup.ReplaceDocumentFromPath(localPath);
        }
    }

    private void OnSetupDoneClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ExitSetup();
    }
}
