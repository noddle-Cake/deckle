using Avalonia.Media.Imaging;
using FluentAvalonia.UI.Controls;
using Deckle.App.Services;
using Deckle.Core.Batch;
using Deckle.Core.Export;
using Deckle.Core.Scanning;

namespace Deckle.App.Tests;

/// <summary>Records dialogs instead of showing them, and answers with preset values.</summary>
public sealed class FakeUi : IUiServices
{
    public List<string> Messages { get; } = [];
    public bool ConfirmResult { get; set; } = true;
    public string? SavePath { get; set; }
    public (int, bool)? MergeAnswer { get; set; }
    public List<string> Opened { get; } = [];
    public bool IsDialogOpen { get; set; }

    public Task<string?> PickSaveFileAsync(string suggestedName, OutputFormat format, string? startFolder) => Task.FromResult(SavePath);
    public Task<string?> PickFolderAsync(string? startFolder) => Task.FromResult<string?>(null);
    public Task<IReadOnlyList<string>> PickFilesToImportAsync() => Task.FromResult<IReadOnlyList<string>>([]);

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add($"{title}: {message}");
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirm, string cancel = "Cancel")
    {
        Messages.Add($"? {title}");
        return Task.FromResult(ConfirmResult);
    }

    public Task<string?> PromptAsync(string title, string label, string initial, string confirm, bool password = false) => Task.FromResult<string?>(initial);
    public Task<FAContentDialogResult> ShowDialogAsync(FAContentDialog dialog) => Task.FromResult(FAContentDialogResult.None);

    public Task OpenFileAsync(string path)
    {
        Opened.Add(path);
        return Task.CompletedTask;
    }

    public Task OpenFolderAsync(string folder, string? selectFile = null)
    {
        Opened.Add(folder);
        return Task.CompletedTask;
    }

    public Task OpenUrlAsync(string url) => Task.CompletedTask;
    public Task CopyTextAsync(string text) => Task.CompletedTask;

    public List<string> CopiedImages { get; } = [];
    public List<IReadOnlyList<string>> Shared { get; } = [];

    public Task CopyImageAsync(string pngPath)
    {
        CopiedImages.Add(pngPath);
        return Task.CompletedTask;
    }

    public Task ShareFilesAsync(IReadOnlyList<string> paths, string title)
    {
        Shared.Add(paths);
        return Task.CompletedTask;
    }
    public Task<(int AfterIndex, bool Reverse)?> AskMergeOptionsAsync(int pageCount) => Task.FromResult<(int, bool)?>(MergeAnswer);
    public Task<(ScanRegion? Region, bool Ok)?> AskRegionAsync(Bitmap preview, ScanRegion? current) => Task.FromResult<(ScanRegion?, bool)?>(null);
    public Task<BatchOptions?> AskBatchOptionsAsync() => Task.FromResult<BatchOptions?>(null);
}
