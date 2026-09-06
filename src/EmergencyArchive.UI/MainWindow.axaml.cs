using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace EmergencyArchive.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => ViewModel.Lock();
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        DocumentItemViewModel? document = ViewModel.SelectedDocument;
        if (document is null)
        {
            return;
        }

        string tempDirectory = ViewModel.EnsureOpenTempDirectory();
        string targetPath = Path.Combine(tempDirectory, document.Name);
        ViewModel.ExportDocumentTo(document.RelativePath, targetPath);
        ViewModel.NoteExternalOpen(document.Name);

        Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
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

        await using var target = await file.OpenWriteAsync();
        ViewModel.ExportDocumentTo(document.RelativePath, target);
        ViewModel.StatusMessage = $"Exported '{document.Name}'. Remember where you saved it — the copy is NOT encrypted.";
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

    private void OnSetupDoneClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel.ExitSetup();
    }
}
