using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using Scanwise.App.Views.Dialogs;
using Scanwise.Core.Batch;
using Scanwise.Core.Export;
using Scanwise.Core.Import;
using Scanwise.Core.Scanning;

namespace Scanwise.App.Services;

/// <summary>Everything the view models need from the window: pickers, dialogs, launching files.</summary>
public interface IUiServices
{
    Task<string?> PickSaveFileAsync(string suggestedName, OutputFormat format, string? startFolder);
    Task<string?> PickFolderAsync(string? startFolder);
    Task<IReadOnlyList<string>> PickFilesToImportAsync();
    Task ShowMessageAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string confirm, string cancel = "Cancel");
    Task<string?> PromptAsync(string title, string label, string initial, string confirm, bool password = false);
    Task<FAContentDialogResult> ShowDialogAsync(FAContentDialog dialog);
    Task OpenFileAsync(string path);
    Task OpenFolderAsync(string folder, string? selectFile = null);
    Task OpenUrlAsync(string url);
    Task CopyTextAsync(string text);

    /// <summary>Puts an image (a PNG file) on the clipboard.</summary>
    Task CopyImageAsync(string pngPath);

    /// <summary>Opens the system share sheet (Windows), the share menu (macOS) or a new email (Linux) with these files.</summary>
    Task ShareFilesAsync(IReadOnlyList<string> paths, string title);

    /// <summary>Scanner's scan-merge options; null when cancelled.</summary>
    Task<(int AfterIndex, bool Reverse)?> AskMergeOptionsAsync(int pageCount);

    /// <summary>Lets the user pick a flatbed area on a preview. Null = cancelled; a null Region = whole page.</summary>
    Task<(ScanRegion? Region, bool Ok)?> AskRegionAsync(Avalonia.Media.Imaging.Bitmap preview, ScanRegion? current);

    Task<BatchOptions?> AskBatchOptionsAsync();
}

public sealed class UiServices : IUiServices
{
    private readonly Window _window;

    public UiServices(Window window)
    {
        _window = window;
    }

    private IStorageProvider Storage => _window.StorageProvider;

    public async Task<string?> PickSaveFileAsync(string suggestedName, OutputFormat format, string? startFolder)
    {
        var type = new FilePickerFileType($"{format.DisplayName()} file")
        {
            Patterns = format.AllExtensions().Select(e => "*" + e).ToList(),
        };
        var file = await Storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save scan",
            SuggestedFileName = suggestedName,
            DefaultExtension = format.Extension().TrimStart('.'),
            FileTypeChoices = [type],
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await TryFolder(startFolder),
        });
        var path = file?.TryGetLocalPath();
        if (path is not null && !format.AllExtensions().Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            path += format.Extension();
        }
        return path;
    }

    public async Task<string?> PickFolderAsync(string? startFolder)
    {
        var folders = await Storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where scans are saved",
            AllowMultiple = false,
            SuggestedStartLocation = await TryFolder(startFolder),
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> PickFilesToImportAsync()
    {
        var files = await Storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import images or PDFs",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Images and PDFs") { Patterns = ImportService.SupportedExtensions.Select(e => "*" + e).ToList() },
                FilePickerFileTypes.All,
            ],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public Task ShowMessageAsync(string title, string message) =>
        ShowDialogAsync(new FAContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420 },
            CloseButtonText = "OK",
            DefaultButton = FAContentDialogButton.Close,
        });

    public async Task<bool> ConfirmAsync(string title, string message, string confirm, string cancel = "Cancel")
    {
        var result = await ShowDialogAsync(new FAContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420 },
            PrimaryButtonText = confirm,
            CloseButtonText = cancel,
            DefaultButton = FAContentDialogButton.Primary,
        });
        return result == FAContentDialogResult.Primary;
    }

    public async Task<string?> PromptAsync(string title, string label, string initial, string confirm, bool password = false)
    {
        var box = new TextBox { Text = initial, PlaceholderText = label, MinWidth = 320 };
        if (password)
        {
            box.PasswordChar = '•';
            box.RevealPassword = false;
        }
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = label }, box } },
            PrimaryButtonText = confirm,
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary,
        };
        dialog.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        var result = await ShowDialogAsync(dialog);
        return result == FAContentDialogResult.Primary ? box.Text ?? "" : null;
    }

    public Task<FAContentDialogResult> ShowDialogAsync(FAContentDialog dialog)
    {
        if (AppShortcuts.IsMac)
        {
            // Esc already cancels; macOS also uses ⌘. (HIG, Alerts).
            dialog.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.OemPeriod, KeyModifiers.Meta),
                Command = new CommunityToolkit.Mvvm.Input.RelayCommand(dialog.Hide),
            });
        }
        return dialog.ShowAsync(_window);
    }

    public async Task OpenFileAsync(string path)
    {
        await _window.Launcher.LaunchFileInfoAsync(new FileInfo(path));
    }

    public async Task OpenFolderAsync(string folder, string? selectFile = null)
    {
        Directory.CreateDirectory(folder);
        if (selectFile is not null && OperatingSystem.IsWindows() && File.Exists(selectFile))
        {
            // Explorer can highlight the file; other platforms just open the folder.
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{selectFile}\"");
            return;
        }
        await _window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
    }

    public async Task OpenUrlAsync(string url)
    {
        await _window.Launcher.LaunchUriAsync(new Uri(url));
    }

    public async Task CopyTextAsync(string text)
    {
        if (_window.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public Task CopyImageAsync(string pngPath) => PlatformShell.CopyImageAsync(pngPath);

    public Task ShareFilesAsync(IReadOnlyList<string> paths, string title) =>
        PlatformShell.ShareAsync(paths, title, _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, ShareAnchor());

    /// <summary>
    /// Where the macOS share menu points: the button that was just clicked (it has focus), or the top centre of
    /// the window when Share came from the menu bar or a shortcut.
    /// </summary>
    private Rect ShareAnchor()
    {
        if (_window.FocusManager?.GetFocusedElement() is Button button
            && button.TranslatePoint(default, _window) is { } topLeft)
        {
            return new Rect(topLeft, button.Bounds.Size);
        }
        return new Rect(_window.ClientSize.Width / 2, 0, 1, 1);
    }

    public async Task<(int AfterIndex, bool Reverse)?> AskMergeOptionsAsync(int pageCount)
    {
        var content = new ScanMergeDialog(pageCount);
        var result = await ShowDialogAsync(new FAContentDialog
        {
            Title = "Insert scan between pages",
            Content = content,
            PrimaryButtonText = "Scan",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary,
        });
        return result == FAContentDialogResult.Primary ? (content.AfterIndex, content.Reverse) : null;
    }

    public async Task<(ScanRegion? Region, bool Ok)?> AskRegionAsync(Avalonia.Media.Imaging.Bitmap preview, ScanRegion? current)
    {
        var content = new RegionDialog(preview, current);
        var result = await ShowDialogAsync(new FAContentDialog
        {
            Title = "Choose scan area",
            Content = content,
            PrimaryButtonText = "Use this area",
            SecondaryButtonText = "Whole page",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary,
        });
        return result switch
        {
            FAContentDialogResult.Primary => (content.Region, true),
            FAContentDialogResult.Secondary => (null, true),
            _ => null,
        };
    }

    public async Task<BatchOptions?> AskBatchOptionsAsync()
    {
        var content = new BatchScanDialog();
        var result = await ShowDialogAsync(new FAContentDialog
        {
            Title = "Batch scan",
            Content = content,
            PrimaryButtonText = "Start",
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary,
        });
        return result == FAContentDialogResult.Primary ? content.Options : null;
    }

    private async Task<IStorageFolder?> TryFolder(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return null;
        }
        return await Storage.TryGetFolderFromPathAsync(path);
    }
}
