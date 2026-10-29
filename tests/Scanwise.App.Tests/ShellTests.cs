using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Scanwise.App.Services;
using Scanwise.App.ViewModels;
using Scanwise.App.Views;
using Scanwise.Core.Export;
using Scanwise.Core.Scanning;
using Scanwise.Core.Settings;

namespace Scanwise.App.Tests;

public sealed class ShellTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scanwise-ui-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Where screenshots go; set SCANWISE_SCREENSHOTS to keep them.</summary>
    private static readonly string ScreenshotDir = Environment.GetEnvironmentVariable("SCANWISE_SCREENSHOTS") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetTempPath(), "scanwise-screenshots");

    private (MainWindow Window, MainViewModel Vm, AppServices Services, FakeUi Ui) Open(double width, double height, Action<AppSettings>? configure = null)
    {
        var services = new AppServices(new AppPaths(Path.Combine(_root, "config"), Path.Combine(_root, "data")));
        services.Settings.SaveFolder = Path.Combine(_root, "out");
        services.Settings.AutoRotate = false;
        configure?.Invoke(services.Settings);
        var ui = new FakeUi();
        var window = new MainWindow { Width = width, Height = height };
        var vm = new MainViewModel(services, ui);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, services, ui);
    }

    private static void Pump(int rounds = 5)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    /// <summary>Waits for async work that hops between the thread pool and the UI thread.</summary>
    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 60_000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs)
            {
                throw new TimeoutException("Condition not met in time");
            }
            Pump(1);
            await Task.Delay(20);
        }
        Pump();
    }

    private static string Screenshot(Window window, string name)
    {
        Directory.CreateDirectory(ScreenshotDir);
        Pump();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        var path = Path.Combine(ScreenshotDir, name + ".png");
        frame.Save(path);
        return path;
    }

    [AvaloniaFact]
    public void LayoutFollowsWindowWidth()
    {
        var (window, vm, services, _) = Open(1400, 860);
        using (services)
        {
            Assert.Equal(LayoutMode.Wide, vm.Layout);
            Assert.True(vm.ShowScanPane && vm.ShowPagesPane && vm.ShowEditorPane);
            Screenshot(window, "01-wide-empty");

            // The resolution box shows the saved/default dpi, also after its list is rebuilt (switching source).
            var resolution = window.GetVisualDescendants().OfType<ComboBox>().First(c => ReferenceEquals(c.ItemsSource, vm.ScanOptions.Resolutions));
            Assert.Equal(300, resolution.SelectedItem);
            vm.ScanOptions.IsFeeder = true;
            Pump();
            vm.ScanOptions.IsFlatbed = true;
            Pump();
            Assert.Equal(300, resolution.SelectedItem);
            Assert.Equal(300, vm.ScanOptions.Settings.Dpi);

            window.Width = 900;
            Pump();
            Assert.Equal(LayoutMode.Medium, vm.Layout);
            Assert.True(vm.ShowScanPane && vm.ShowPagesPane && !vm.ShowEditorPane);
            vm.Section = Section.Editor;
            Assert.True(vm.ShowScanPane && !vm.ShowPagesPane && vm.ShowEditorPane);

            window.Width = 500;
            Pump();
            Assert.Equal(LayoutMode.Narrow, vm.Layout);
            vm.Section = Section.Scan;
            Assert.True(vm.ShowScanPane && !vm.ShowPagesPane && !vm.ShowEditorPane);
            Screenshot(window, "02-narrow-scan");

            vm.Section = Section.Settings;
            Assert.False(vm.IsWorkspace);
            window.Width = 1100;
            Pump();
            Screenshot(window, "03-settings");
            vm.Section = Section.Help;
            Pump();
            Screenshot(window, "04-help");
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ScanWithSaneTestDeviceAddsPagesAndAutoSaves()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SANE_CONFIG_DIR")))
        {
            return; // SANE test backend not enabled (see scripts/test.sh)
        }
        var (window, vm, services, ui) = Open(1400, 860);
        using (services)
        {
            await vm.ScanOptions.DiscoverAsync();
            await WaitUntil(() => vm.ScanOptions.SelectedScanner is not null && !vm.ScanOptions.IsLoadingCapabilities);
            var test = vm.ScanOptions.Scanners.First(s => s.Id == "test:0");
            vm.ScanOptions.SelectedScanner = test;
            await WaitUntil(() => !vm.ScanOptions.IsLoadingCapabilities);
            Assert.True(vm.ScanOptions.FeederSupported);

            vm.ScanOptions.IsFlatbed = true;
            vm.ScanOptions.Dpi = 75;
            vm.ScanOptions.Format = vm.ScanOptions.Formats.First(f => f.Value == OutputFormat.Pdf);
            Assert.True(vm.CanScan);

            await vm.ScanCommand.ExecuteAsync(null);
            await WaitUntil(() => !vm.IsBusy);
            Assert.Empty(ui.Messages);
            Assert.Equal(1, vm.Pages.Count);
            Assert.NotNull(vm.LastSavedPath);
            Assert.True(File.Exists(vm.LastSavedPath));
            Assert.Equal(".pdf", Path.GetExtension(vm.LastSavedPath));

            // Second scan is appended to the same PDF (Scanner's "add to existing").
            await vm.ScanCommand.ExecuteAsync(null);
            await WaitUntil(() => !vm.IsBusy);
            Assert.Equal(2, vm.Pages.Count);
            Assert.Single(Directory.GetFiles(services.Settings.SaveFolder));

            await WaitUntil(() => vm.Pages.Items.All(i => i.Thumbnail is not null) && vm.Editor.Preview is not null);
            Screenshot(window, "05-wide-after-scan");
            var ed = vm.Editor;
            var box = window.GetVisualDescendants().OfType<Viewbox>().First(v => v.FindAncestorOfType<EditorView>() is not null);
            var scroller = box.FindAncestorOfType<ScrollViewer>()!;
            Assert.True(box.Bounds.Height <= scroller.Bounds.Height && box.Bounds.Width <= scroller.Bounds.Width,
                $"viewport={ed.Viewport} preview={ed.PreviewPixels} zoom={ed.EffectiveZoom} display={ed.DisplayWidth}x{ed.DisplayHeight} box={box.Bounds} scroller={scroller.Bounds}");

            vm.Editor.StartCropCommand.Execute(null);
            vm.Editor.CropLeft = 0.1;
            vm.Editor.CropTop = 0.1;
            vm.Editor.CropRight = 0.7;
            vm.Editor.CropBottom = 0.6;
            Pump();
            Screenshot(window, "06-crop");
            await vm.Editor.ApplyCropCommand.ExecuteAsync(null);
            Assert.False(vm.Editor.IsCropping);
            Assert.True(vm.HasUnsavedChanges);

            vm.Editor.StartAdjustCommand.Execute(null);
            vm.Editor.Grayscale = true;
            vm.Editor.Contrast = 30;
            await Task.Delay(300);
            Pump();
            Screenshot(window, "07-adjust");
            vm.Editor.ApplyAdjustCommand.Execute(null);

            // Draw (Scanner's ink tools): strokes are burned in and can be undone as one step.
            vm.Editor.StartDrawCommand.Execute(null);
            vm.Editor.Strokes.Add(new Scanwise.Core.Processing.InkStroke([(0.2, 0.3), (0.5, 0.35), (0.8, 0.3)], 0xFFE01B24, 0.01, false));
            vm.Editor.IsHighlighter = true;
            vm.Editor.Strokes.Add(new Scanwise.Core.Processing.InkStroke([(0.15, 0.6), (0.85, 0.6)], 0x6EF6D32D, 0.04, true));
            Pump();
            Screenshot(window, "07b-draw");
            var before = vm.Editor.Current!.Page.Image;
            await vm.Editor.ApplyDrawCommand.ExecuteAsync(null);
            await WaitUntil(() => !ReferenceEquals(vm.Editor.Current!.Page.Image, before));
            Assert.True(vm.Editor.Current!.Page.Image.TransformState.IsEmpty);
            vm.UndoCommand.Execute(null);
            Assert.Same(before, vm.Editor.Current!.Page.Image);
            vm.RedoCommand.Execute(null);

            await vm.SaveCommand.ExecuteAsync(null);
            await WaitUntil(() => !vm.IsBusy);
            Assert.False(vm.HasUnsavedChanges);
            Assert.Single(Directory.GetFiles(services.Settings.SaveFolder));

            // Feeder into JPG: one file per page.
            vm.ScanOptions.IsFeeder = true;
            vm.ScanOptions.Dpi = 50;
            vm.ScanOptions.Format = vm.ScanOptions.Formats.First(f => f.Value == OutputFormat.Jpeg);
            await vm.ScanStartFreshCommand.ExecuteAsync(null);
            await WaitUntil(() => !vm.IsBusy);
            Assert.True(vm.Pages.Count > 1);
            var jpgs = Directory.GetFiles(services.Settings.SaveFolder, "*.jpg");
            Assert.True(vm.Pages.Count == jpgs.Length, $"pages={vm.Pages.Count} doc={services.Document.Count} files={string.Join(", ", Directory.GetFiles(services.Settings.SaveFolder).Select(Path.GetFileName))} msgs={string.Join(" | ", ui.Messages)} status={vm.StatusTitle}: {vm.StatusMessage}");

            vm.Pages.Select([vm.Pages.Items[0].Id, vm.Pages.Items[1].Id]);
            vm.DeleteSelectedCommand.Execute(null);
            Pump();
            var afterDelete = vm.Pages.Count;
            vm.UndoCommand.Execute(null);
            Assert.Equal(afterDelete + 2, vm.Pages.Count);

            window.Width = 900;
            await WaitUntil(() => vm.Pages.Items.All(i => i.Thumbnail is not null));
            Screenshot(window, "08-medium-pages");
            window.Width = 520;
            vm.Section = Section.Pages;
            Pump();
            Screenshot(window, "09-narrow-pages");
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SessionIsRestoredOnNextStart()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SANE_CONFIG_DIR")))
        {
            return; // SANE test backend not enabled (see scripts/test.sh)
        }
        var (window, vm, services, _) = Open(1200, 800, s => s.AutoSave = false);
        await vm.ScanOptions.DiscoverAsync();
        vm.ScanOptions.SelectedScanner = vm.ScanOptions.Scanners.First(s => s.Id == "test:0");
        await WaitUntil(() => !vm.ScanOptions.IsLoadingCapabilities);
        vm.ScanOptions.IsFlatbed = true;
        vm.ScanOptions.Dpi = 50;
        await vm.ScanCommand.ExecuteAsync(null);
        await WaitUntil(() => !vm.IsBusy);
        vm.Pages.RotateRightCommand.Execute(null);
        Assert.True(vm.HasUnsavedChanges);
        vm.OnShutdown();
        window.Close();
        services.Dispose();

        var (window2, vm2, services2, ui2) = Open(1200, 800);
        using (services2)
        {
            await vm2.InitializeAsync();
            Assert.Contains(ui2.Messages, m => m.Contains("Restore pages"));
            Assert.Equal(1, vm2.Pages.Count);
            Assert.False(vm2.Pages.Items[0].Page.Image.TransformState.IsEmpty);
            window2.Close();
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
