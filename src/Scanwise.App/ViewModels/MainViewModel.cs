using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Scanwise.App.Services;
using Scanwise.Core.Batch;
using Scanwise.Core.Export;
using Scanwise.Core.Pages;
using Scanwise.Core.Scanning;
using Scanwise.Core.Settings;

namespace Scanwise.App.ViewModels;

public enum Section { Scan, Pages, Editor, Settings, Help }

public enum LayoutMode { Narrow, Medium, Wide }

/// <summary>How a scan is added to the page list (Scanner's split Scan button).</summary>
public enum ScanMode { Default, AddToDocument, StartFresh, Merge }

public sealed partial class MainViewModel : ObservableObject
{
    // Scanner's breakpoints, widened a little for the thumbnail column.
    public const double MediumWidth = 775;
    public const double WideWidth = 1180;

    private readonly AppServices _services;
    private readonly IUiServices _ui;
    private CancellationTokenSource? _operationCts;
    private Timer? _sessionTimer;
    private bool _shuttingDown;

    public MainViewModel(AppServices services, IUiServices ui)
    {
        _services = services;
        _ui = ui;
        ScanOptions = new ScanOptionsViewModel(services, ui);
        Pages = new PageListViewModel(services);
        Editor = new EditorViewModel(services, Pages);
        SettingsPage = new SettingsViewModel(services, ui);
        services.Saver.AskForPath = (name, format) => _ui.PickSaveFileAsync(name, format, services.Settings.SaveFolder);
        services.Import.PasswordPrompt = (file, attempt) => null;
        services.Document.Changed += (_, _) => OnDocumentChanged();
    }

    public ScanOptionsViewModel ScanOptions { get; }
    public PageListViewModel Pages { get; }
    public EditorViewModel Editor { get; }
    public SettingsViewModel SettingsPage { get; }
    public AppSettings Settings => _services.Settings;
    public PageDocument Document => _services.Document;

    [ObservableProperty] private Section _section = Section.Scan;
    [ObservableProperty] private LayoutMode _layout = LayoutMode.Wide;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isProgressIndeterminate = true;
    [ObservableProperty] private bool _canCancel;

    [ObservableProperty] private bool _isStatusOpen;
    [ObservableProperty] private string _statusTitle = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;
    [ObservableProperty] private string? _lastSavedPath;
    /// <summary>True while the status bar is reporting a save, so Open / Show in folder make sense.</summary>
    [ObservableProperty] private bool _statusHasFileActions;

    /// <summary>
    /// macOS titles the window with the document's name ("Untitled" until saved) and shows unsaved changes as the
    /// dot in the close button (see <see cref="DocumentPath"/>); elsewhere the title carries the page count and "•".
    /// </summary>
    public string WindowTitle => AppShortcuts.IsMac
        ? Document.Count == 0 ? "Scanwise" : Path.GetFileName(DocumentPath) ?? "Untitled"
        : Document.Count == 0 ? "Scanwise" : $"Scanwise · {Document.Count} page{(Document.Count == 1 ? "" : "s")}{(HasUnsavedChanges ? " •" : "")}";

    /// <summary>The PDF/TIFF the pages are being saved into, if there is one.</summary>
    public string? DocumentPath => Document.Count == 0 ? null : _services.Saver.CurrentDocumentPath;
    public bool HasUnsavedChanges => _services.Saver.HasUnsavedChanges(Document);
    public bool HasPages => Document.Count > 0;
    public bool CanUndo => Document.CanUndo;
    public bool CanRedo => Document.CanRedo;
    public bool CanScan => !IsBusy && ScanOptions.HasScanner;
    public bool CanShowLastSaved => LastSavedPath is not null;

    public bool IsWorkspace => Section is Section.Scan or Section.Pages or Section.Editor;
    public bool ShowSettings => Section == Section.Settings;
    public bool ShowHelp => Section == Section.Help;
    public bool ShowScanPane => IsWorkspace && (Layout == LayoutMode.Narrow ? Section == Section.Scan : !SidebarHidden);

    /// <summary>macOS View ▸ Hide Sidebar: the scan options column is hidden in wide and medium windows.</summary>
    [ObservableProperty] private bool _sidebarHidden;

    public bool SidebarVisible => ShowScanPane;
    public string SidebarMenuHeader => SidebarVisible ? "Hide Sidebar" : "Show Sidebar";

    /// <summary>
    /// Shows or hides the sidebar. A narrow window has room for one pane, so there it swaps between the scan
    /// options and the pages (Sidebars HIG: collapse the sidebar rather than switch to other navigation).
    /// </summary>
    [RelayCommand]
    private void ToggleSidebar()
    {
        if (Layout == LayoutMode.Narrow)
        {
            Section = Section == Section.Scan ? Section.Pages : Section.Scan;
        }
        else
        {
            SidebarHidden = !SidebarHidden;
        }
    }

    partial void OnSidebarHiddenChanged(bool value) => RaiseLayout();

    // Editor tools from the macOS toolbar and menu bar; they bring the editor on screen first.
    [RelayCommand]
    private void CropPage() => InEditor(Editor.StartCropCommand);

    [RelayCommand]
    private void AdjustPage() => InEditor(Editor.StartAdjustCommand);

    [RelayCommand]
    private void DrawOnPage() => InEditor(Editor.StartDrawCommand);

    private void InEditor(System.Windows.Input.ICommand tool)
    {
        if (!ShowEditorPane)
        {
            Section = Section.Editor;
        }
        tool.Execute(null);
    }
    public bool ShowPagesPane => IsWorkspace && Layout switch
    {
        LayoutMode.Wide => true,
        LayoutMode.Medium => Section != Section.Editor,
        _ => Section == Section.Pages,
    };
    public bool ShowEditorPane => IsWorkspace && Layout switch
    {
        LayoutMode.Wide => true,
        _ => Section == Section.Editor,
    };
    /// <summary>The page list fills the remaining width unless the editor is beside it.</summary>
    public bool PagesPaneIsColumn => Layout == LayoutMode.Wide;
    public bool ShowEditorNavItem => Layout != LayoutMode.Wide;

    // The macOS toolbar's Pages | Editor switch, used when the window is too narrow for both.
    public bool IsPagesShown => ShowPagesPane && !ShowEditorPane;
    public bool IsEditorShown => ShowEditorPane && !ShowPagesPane;

    /// <summary>The toolbar's centre group collapses in a narrow window (its commands stay in the menu bar).</summary>
    public bool ShowToolbarActions => Layout != LayoutMode.Narrow;

    private readonly TaskCompletionSource _discoveryStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task InitializeAsync()
    {
        try
        {
            await RestoreSessionAsync();
        }
        finally
        {
            // Continuations run after DiscoverAsync has started searching (asynchronous continuations).
            _discoveryStarted.TrySetResult();
        }
        await ScanOptions.DiscoverAsync();
        OnPropertyChanged(nameof(CanScan));
    }

    /// <summary>
    /// "Scanwise --scan", the jump list / launcher / Dock menu entry or the shortcut key: scans with the current
    /// options as soon as the remembered scanner has been found, without waiting for the whole device search.
    /// </summary>
    public async Task ScanFromShortcutAsync()
    {
        await _discoveryStarted.Task;
        if (!IsWorkspace)
        {
            Section = Section.Scan;
        }
        var start = Environment.TickCount64;
        while (ScanOptions.SelectedScanner is null || ScanOptions.IsLoadingCapabilities)
        {
            if ((ScanOptions.SelectedScanner is null && !ScanOptions.IsSearching) || Environment.TickCount64 - start > 60_000)
            {
                ShowStatus("No scanner found", "Connect or turn on your scanner, then scan again.", InfoBarSeverity.Warning);
                return;
            }
            await Task.Delay(100);
        }
        if (!IsBusy)
        {
            await ScanCommand.ExecuteAsync(null);
        }
    }

    public void UpdateLayout(double width)
    {
        Layout = width >= WideWidth ? LayoutMode.Wide : width >= MediumWidth ? LayoutMode.Medium : LayoutMode.Narrow;
    }

    partial void OnSectionChanged(Section value) => RaiseLayout();
    partial void OnLayoutChanged(LayoutMode value) => RaiseLayout();
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanScan));
    partial void OnLastSavedPathChanged(string? value) => OnPropertyChanged(nameof(CanShowLastSaved));

    private void RaiseLayout()
    {
        foreach (var name in new[]
        {
            nameof(IsWorkspace), nameof(ShowSettings), nameof(ShowHelp), nameof(ShowScanPane), nameof(ShowPagesPane),
            nameof(ShowEditorPane), nameof(PagesPaneIsColumn), nameof(ShowEditorNavItem), nameof(SidebarVisible),
            nameof(SidebarMenuHeader), nameof(IsPagesShown), nameof(IsEditorShown), nameof(ShowToolbarActions),
        })
        {
            OnPropertyChanged(name);
        }
    }

    [RelayCommand]
    private void Navigate(Section section) => Section = section;

    /// <summary>Scanwise ▸ About Scanwise in the macOS menu bar.</summary>
    [RelayCommand]
    private Task ShowAbout() => _ui.ShowMessageAsync("Scanwise",
        $"Version {SettingsPage.Version}\n\nA simple document scanner built on NAPS2 and the ideas of Scanner by Simon Knuth.\nMozilla Public License 2.0.");

    /// <summary>Opens a page in the editor (double-click in the page list).</summary>
    public void OpenInEditor(PageItemViewModel item)
    {
        Pages.Select([item.Id]);
        if (Layout != LayoutMode.Wide)
        {
            Section = Section.Editor;
        }
    }

    // ---------------------------------------------------------------- Scanning

    [RelayCommand]
    private Task Scan() => RunScanAsync(ScanMode.Default);

    [RelayCommand]
    private Task ScanAddToDocument() => RunScanAsync(ScanMode.AddToDocument);

    [RelayCommand]
    private Task ScanStartFresh() => RunScanAsync(ScanMode.StartFresh);

    [RelayCommand]
    private Task ScanMerge() => RunScanAsync(ScanMode.Merge);

    private async Task RunScanAsync(ScanMode mode)
    {
        if (IsBusy || ScanOptions.SelectedScanner is not { } scanner)
        {
            return;
        }

        (int AfterIndex, bool Reverse)? merge = null;
        if (mode == ScanMode.Merge)
        {
            if (Document.Count == 0)
            {
                mode = ScanMode.AddToDocument;
            }
            else
            {
                merge = await _ui.AskMergeOptionsAsync(Document.Count);
                if (merge is null)
                {
                    return;
                }
            }
        }

        var startFresh = mode == ScanMode.StartFresh || (mode == ScanMode.Default && Settings.ScanAction == ScanAction.StartFresh);
        if (startFresh && Document.Count > 0)
        {
            if (HasUnsavedChanges && !Settings.AutoSave
                && !await _ui.ConfirmAsync("Start a new document?", "The current pages have not been saved. Start a new document anyway?", "Start new"))
            {
                return;
            }
            Document.Clear();
        }
        if (startFresh || Document.Count == 0)
        {
            _services.Saver.StartNewDocument();
        }

        var settings = ScanOptions.Settings.Clone();
        var newPages = await ScanPagesAsync(scanner, settings, addToDocument: merge is null);
        if (merge is { } m && newPages.Count > 0)
        {
            Document.MergeInto(newPages, m.AfterIndex, m.Reverse);
        }
        if (newPages.Count > 0)
        {
            Pages.Select([newPages[^1].Id]);
            if (Settings.AutoSave)
            {
                await SaveScanAsync(newPages, settings, scanner);
            }
        }
    }

    /// <summary>Scans, auto-rotates and (optionally) appends pages as they arrive. Errors are shown, partial results kept.</summary>
    private async Task<List<Page>> ScanPagesAsync(DiscoveredScanner scanner, ScanSettings settings, bool addToDocument)
    {
        var newPages = new List<Page>();
        using var cts = BeginOperation("Scanning…", cancellable: true);
        var progress = new Progress<ScanProgress>(p =>
        {
            BusyText = $"Scanning page {p.PageNumber}…";
            IsProgressIndeterminate = p.PageFraction <= 0;
            Progress = p.PageFraction * 100;
        });
        var autoRotate = Settings.AutoRotate && _services.AutoRotate.IsAvailable;
        // Reversed: each page goes in front of the ones from this scan, so pages still show up as they arrive.
        var reverse = Settings.ReverseScanOrder;
        var insertAt = Document.Count;
        var time = DateTime.Now;
        try
        {
            using (Document.BeginGroup())
            {
                await foreach (var image in _services.Scanner.ScanAsync(scanner, settings, ScanOptions.Capabilities, progress, cts.Token))
                {
                    var finalImage = image;
                    if (autoRotate)
                    {
                        BusyText = $"Straightening page {newPages.Count + 1}…";
                        finalImage = await _services.AutoRotate.AutoRotateAsync(image, Settings.OcrLanguage, cts.Token);
                    }
                    var page = new Page(finalImage, PageInfo.FromScan(settings, scanner.Name, time));
                    newPages.Insert(reverse ? 0 : newPages.Count, page);
                    if (addToDocument)
                    {
                        Document.Insert(reverse ? Math.Min(insertAt, Document.Count) : Document.Count, [page]);
                    }
                }
            }
            if (newPages.Count == 0)
            {
                ShowStatus("Nothing scanned", settings.Source is ScanSource.Feeder or ScanSource.Duplex
                    ? "The feeder returned no pages. Check that paper is loaded."
                    : "The scanner returned no pages.", InfoBarSeverity.Warning);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || cts.IsCancellationRequested)
        {
            ShowStatus("Scan cancelled", newPages.Count > 0 ? $"Kept {newPages.Count} page(s) scanned before cancelling." : "", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            var kept = newPages.Count > 0 ? $"\n\n{newPages.Count} page(s) scanned before the error were kept." : "";
            await _ui.ShowMessageAsync("Scanning failed", ScanErrors.Describe(ex) + kept);
        }
        finally
        {
            EndOperation();
        }
        return newPages;
    }

    private async Task SaveScanAsync(IReadOnlyList<Page> newPages, ScanSettings settings, DiscoveredScanner? scanner, AppSettings? overrideSettings = null)
    {
        using var cts = BeginOperation("Saving…", cancellable: true);
        try
        {
            var result = await _services.Saver.SaveScanAsync(
                Document, newPages, settings.Format, overrideSettings ?? Settings, Naming(settings, scanner),
                SaveProgress(), cts.Token);
            if (result is { Paths.Count: > 0 })
            {
                ReportSaved(result);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _ui.ShowMessageAsync("Could not save the scan", $"{ex.Message}\n\nThe pages are still in the list; use Save as to try another location.");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            EndOperation();
            RaiseDocumentState();
        }
    }

    [RelayCommand]
    private async Task Preview()
    {
        if (IsBusy || ScanOptions.SelectedScanner is not { } scanner)
        {
            return;
        }
        var settings = ScanOptions.Settings.Clone();
        settings.Source = ScanSource.Flatbed;
        settings.Region = null;
        settings.Format = OutputFormat.Png;
        settings.AutoDeskew = false;
        settings.ExcludeBlankPages = false;
        settings.Dpi = ScanOptions.Capabilities.NearestResolution(ScanSource.Flatbed, 75);
        Avalonia.Media.Imaging.Bitmap? bitmap = null;
        using (var cts = BeginOperation("Scanning preview…", cancellable: true))
        {
            try
            {
                await foreach (var image in _services.Scanner.ScanAsync(scanner, settings, ScanOptions.Capabilities, null, cts.Token))
                {
                    bitmap = await _services.Renderer.RenderAsync(image, 1400);
                    image.Dispose();
                    break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await _ui.ShowMessageAsync("Preview failed", ScanErrors.Describe(ex));
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                EndOperation();
            }
        }
        if (bitmap is null)
        {
            return;
        }
        var region = await _ui.AskRegionAsync(bitmap, ScanOptions.Settings.Region);
        bitmap.Dispose();
        if (region is not null)
        {
            ScanOptions.SetRegion(region.Value.Region);
        }
    }

    [RelayCommand]
    private async Task BatchScan()
    {
        if (IsBusy || ScanOptions.SelectedScanner is not { } scanner)
        {
            return;
        }
        var options = await _ui.AskBatchOptionsAsync();
        if (options is null)
        {
            return;
        }
        var settings = ScanOptions.Settings.Clone();
        var batchPages = new List<Page>();
        var scanPages = new List<Page>();
        var saveSettings = JsonClone(Settings);
        saveSettings.ScanAction = ScanAction.StartFresh;

        var batch = new BatchScanner
        {
            ScanOnce = async _ =>
            {
                scanPages = await ScanPagesAsync(scanner, settings, addToDocument: true);
                batchPages.AddRange(scanPages);
                return scanPages.Count;
            },
            Save = async _ =>
            {
                var pages = options.Output == BatchOutput.FilePerScan ? scanPages : batchPages;
                _services.Saver.StartNewDocument();
                await SaveScanAsync(pages, settings, scanner, saveSettings);
            },
            Prompt = async n => await _ui.ConfirmAsync("Next scan", $"Ready for scan {n} of {options.ScanCount}? Load the next document, then continue.", "Scan", "Stop"),
        };
        await batch.RunAsync(options, new Progress<BatchStatus>(s => ShowStatus("Batch scan", s.Message, InfoBarSeverity.Informational)), CancellationToken.None);
    }

    [RelayCommand]
    private void CancelOperation() => _operationCts?.Cancel();

    // ---------------------------------------------------------------- Files

    [RelayCommand]
    private async Task Save()
    {
        if (Document.Count == 0 || IsBusy)
        {
            return;
        }
        using var cts = BeginOperation("Saving…", cancellable: true);
        try
        {
            var result = await _services.Saver.SaveAsync(Document, ScanOptions.Settings.Format, Settings, Naming(ScanOptions.Settings, ScanOptions.SelectedScanner), SaveProgress(), cts.Token);
            if (result is { Paths.Count: > 0 })
            {
                ReportSaved(result);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _ui.ShowMessageAsync("Could not save", ex.Message);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            EndOperation();
            RaiseDocumentState();
        }
    }

    [RelayCommand]
    private Task SaveAs() => SaveAsInternal(Document.Pages);

    [RelayCommand]
    private Task SaveSelectionAs() => SaveAsInternal(Pages.SelectedIds.Select(id => Document.Find(id)).OfType<Page>().ToList());

    private async Task SaveAsInternal(IReadOnlyList<Page> pages)
    {
        if (pages.Count == 0 || IsBusy)
        {
            return;
        }
        var format = ScanOptions.Settings.Format;
        var suggested = FileNamer.Render(Settings.FileNamePattern, Naming(ScanOptions.Settings, ScanOptions.SelectedScanner)) + format.Extension();
        var path = await _ui.PickSaveFileAsync(suggested, format, Settings.SaveFolder);
        if (path is null)
        {
            return;
        }
        format = OutputFormats.FromPath(path) ?? format;
        using var cts = BeginOperation("Saving…", cancellable: true);
        try
        {
            var result = await _services.Saver.SaveAsAsync(Document, pages, path, format, Settings, SaveProgress(), cts.Token);
            ReportSaved(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _ui.ShowMessageAsync("Could not save", ex.Message);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            EndOperation();
            RaiseDocumentState();
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        var files = await _ui.PickFilesToImportAsync();
        await ImportFilesAsync(files);
    }

    public async Task ImportFilesAsync(IReadOnlyList<string> files, int? insertAt = null)
    {
        files = files.Where(Core.Import.ImportService.IsSupported).ToList();
        if (files.Count == 0 || IsBusy)
        {
            return;
        }
        var added = new List<Page>();
        using var cts = BeginOperation("Importing…", cancellable: true);
        try
        {
            foreach (var file in files)
            {
                BusyText = $"Importing {Path.GetFileName(file)}…";
                var fromFile = new List<Page>();
                await foreach (var page in _services.Import.ImportAsync(file, null, cts.Token))
                {
                    fromFile.Add(page);
                }
                added.AddRange(fromFile);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _ui.ShowMessageAsync("Import failed", ex.Message);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            EndOperation();
        }
        if (added.Count > 0)
        {
            Document.Insert(insertAt ?? Document.Count, added);
            Pages.Select([added[0].Id]);
            ShowStatus("Imported", $"Added {added.Count} page(s).", InfoBarSeverity.Success);
        }
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        var ids = Pages.SelectedIds;
        if (ids.Count == 0)
        {
            return;
        }
        Document.Remove(ids);
        ShowStatus("Deleted", $"Removed {ids.Count} page(s). Press {AppShortcuts.Label(AppShortcuts.Undo)} to undo.", InfoBarSeverity.Informational);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ClearAll()
    {
        if (Document.Count == 0)
        {
            return;
        }
        var message = HasUnsavedChanges
            ? "Some changes have not been saved. Remove all pages from the list? Files already saved are not deleted."
            : "Remove all pages from the list? Saved files are not deleted.";
        if (!await _ui.ConfirmAsync("Start a new document?", message, "Clear pages"))
        {
            return;
        }
        Document.Clear();
        _services.Saver.StartNewDocument();
    }

    [RelayCommand]
    private void Undo() => Document.Undo();

    [RelayCommand]
    private void Redo() => Document.Redo();

    [RelayCommand]
    private async Task OpenSelectedExternally()
    {
        var page = Pages.SelectedItem?.Page;
        if (page is null)
        {
            return;
        }
        var path = Path.Combine(_services.Paths.TempDir, $"Scanwise page {Pages.SelectedItem!.Number} {DateTime.Now:HHmmss}.png");
        try
        {
            await _services.Export.ExportPagesAsync([page.Image], [path], OutputFormat.Png, Settings);
            await _ui.OpenFileAsync(path);
        }
        catch (Exception ex)
        {
            await _ui.ShowMessageAsync("Could not open the page", ex.Message);
        }
    }

    /// <summary>Copies the selected (or shown) page to the clipboard as an image, without saving it anywhere.</summary>
    [RelayCommand]
    private async Task CopyPage()
    {
        var item = Pages.SelectedItem ?? Editor.Current;
        if (item is null || IsBusy)
        {
            return;
        }
        var path = Path.Combine(_services.Paths.TempDir, $"Scanwise page {item.Number} {DateTime.Now:HHmmssfff}.png");
        try
        {
            await _services.Export.ExportPagesAsync([item.Page.Image], [path], OutputFormat.Png, Settings);
            await _ui.CopyImageAsync(path);
            ShowStatus("Copied", $"Page {item.Number} is on the clipboard.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            await _ui.ShowMessageAsync("Could not copy the page", ex.Message);
        }
    }

    /// <summary>
    /// Shares the selected pages (all pages when none are selected) without saving them first:
    /// one page as a JPEG, several as one PDF.
    /// </summary>
    [RelayCommand]
    private async Task Share()
    {
        var pages = Pages.SelectedItems.Count > 0
            ? Pages.SelectedItems.OrderBy(i => i.Number).Select(i => i.Page).ToList()
            : Document.Pages.ToList();
        if (pages.Count == 0 || IsBusy)
        {
            return;
        }
        var folder = Path.Combine(_services.Paths.TempDir, "share-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"));
        var name = $"Scan {DateTime.Now:yyyy-MM-dd HH.mm}";
        var format = pages.Count == 1 ? OutputFormat.Jpeg : OutputFormat.Pdf;
        var path = Path.Combine(folder, name + format.Extension());
        using (var cts = BeginOperation("Preparing to share…", cancellable: true))
        {
            try
            {
                Directory.CreateDirectory(folder);
                var images = pages.Select(p => p.Image).ToList();
                if (format == OutputFormat.Pdf)
                {
                    await _services.Export.ExportDocumentAsync(images, path, format, Settings, SaveProgress(), cts.Token);
                }
                else
                {
                    await _services.Export.ExportPagesAsync(images, [path], format, Settings, SaveProgress(), cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                await _ui.ShowMessageAsync("Could not share", ex.Message);
                return;
            }
            finally
            {
                EndOperation();
            }
        }
        try
        {
            await _ui.ShareFilesAsync([path], name);
        }
        catch (Exception ex)
        {
            await _ui.ShowMessageAsync("Could not share", ex.Message);
        }
    }

    [RelayCommand]
    private Task OpenSaveFolder() => _ui.OpenFolderAsync(Settings.SaveFolder, LastSavedPath);

    [RelayCommand]
    private async Task OpenLastSaved()
    {
        if (LastSavedPath is not null && File.Exists(LastSavedPath))
        {
            await _ui.OpenFileAsync(LastSavedPath);
        }
    }

    [RelayCommand]
    private async Task ShowLastSavedInFolder()
    {
        if (LastSavedPath is not null)
        {
            await _ui.OpenFolderAsync(Path.GetDirectoryName(LastSavedPath)!, LastSavedPath);
        }
    }

    [RelayCommand]
    private Task OpenUrl(string url) => _ui.OpenUrlAsync(url);

    // ---------------------------------------------------------------- Session

    private async Task RestoreSessionAsync()
    {
        if (!Settings.RestoreSession || !_services.Session.HasSavedSession)
        {
            _services.Session.Clear();
            return;
        }
        var count = _services.Session.SavedPageCount;
        if (!await _ui.ConfirmAsync("Restore pages?", $"{count} page(s) from your last session were not cleared. Restore them?", "Restore", "Discard"))
        {
            _services.Session.Clear();
            return;
        }
        using var cts = BeginOperation("Restoring pages…", cancellable: false);
        try
        {
            var pages = await _services.Session.RestoreAsync();
            Document.Load(pages);
            Pages.Select(pages.Take(1).Select(p => p.Id));
        }
        finally
        {
            EndOperation();
        }
    }

    private void OnDocumentChanged()
    {
        RaiseDocumentState();
        if (_shuttingDown)
        {
            return;
        }
        _sessionTimer?.Dispose();
        _sessionTimer = new Timer(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = SaveSessionAsync()), null, 1500, Timeout.Infinite);
    }

    private async Task SaveSessionAsync()
    {
        try
        {
            if (Document.Count == 0)
            {
                _services.Session.Clear();
            }
            else
            {
                await _services.Session.SaveAsync(Document.Pages);
            }
        }
        catch (Exception)
        {
            // Recovery is best effort; never interrupt the user for it.
        }
    }

    public void OnShutdown()
    {
        _shuttingDown = true;
        _sessionTimer?.Dispose();
        ScanOptions.SaveNow();
        _services.SaveSettings();
        try
        {
            if (Document.Count == 0)
            {
                _services.Session.Clear();
            }
            else
            {
                _services.Session.SaveAsync(Document.Pages).GetAwaiter().GetResult();
            }
        }
        catch (Exception)
        {
        }
    }

    // ---------------------------------------------------------------- Helpers

    private void RaiseDocumentState()
    {
        OnPropertyChanged(nameof(DocumentPath));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private CancellationTokenSource BeginOperation(string text, bool cancellable)
    {
        _operationCts = new CancellationTokenSource();
        BusyText = text;
        IsProgressIndeterminate = true;
        Progress = 0;
        CanCancel = cancellable;
        IsBusy = true;
        return _operationCts;
    }

    private void EndOperation()
    {
        IsBusy = false;
        CanCancel = false;
    }

    private IProgress<ExportProgress> SaveProgress() => new Progress<ExportProgress>(p =>
    {
        BusyText = $"{p.Stage}… ({p.Done}/{p.Total})";
        IsProgressIndeterminate = p.Total == 0;
        Progress = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
    });

    private static NamingContext Naming(ScanSettings settings, DiscoveredScanner? scanner) =>
        new(DateTime.Now, settings.Format, scanner?.Name ?? settings.DeviceName, settings.Dpi, settings.Color, settings.Source, settings.Brightness, settings.Contrast);

    private void ReportSaved(SaveResult result)
    {
        LastSavedPath = result.Paths[^1];
        var what = result.Paths.Count == 1 ? Path.GetFileName(result.Paths[0]) : $"{result.Paths.Count} files";
        ShowStatus("Saved", $"{what} in {result.Folder}", InfoBarSeverity.Success);
        StatusHasFileActions = true;
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusHasFileActions = false;
        StatusTitle = title;
        StatusMessage = message;
        StatusSeverity = severity;
        IsStatusOpen = true;
    }

    private static AppSettings JsonClone(AppSettings settings) =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings, JsonStore.Options), JsonStore.Options)!;
}
