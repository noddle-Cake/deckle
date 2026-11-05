using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deckle.App.Services;
using Deckle.Core.Export;
using Deckle.Core.Scanning;

namespace Deckle.App.ViewModels;

/// <summary>
/// The scan options pane (Scanner's left pane). Edits go straight into the active profile, which is saved
/// automatically, so the last-used options are always remembered.
/// </summary>
public sealed partial class ScanOptionsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IUiServices _ui;
    private CancellationTokenSource? _discoveryCts;
    private CancellationTokenSource? _capsCts;
    private bool _loading;
    private Timer? _saveTimer;

    public ScanOptionsViewModel(AppServices services, IUiServices ui)
    {
        _services = services;
        _ui = ui;
        Settings = services.Profiles.Get(services.Settings.LastProfile).Clone();
        ProfileNames = new ObservableCollection<string>(services.Profiles.Profiles.Select(p => p.Name));
        _selectedProfileName = Settings.Name;
        LoadFromSettings();
    }

    /// <summary>The settings being edited (a copy of the active profile).</summary>
    public ScanSettings Settings { get; private set; }

    public ObservableCollection<DiscoveredScanner> Scanners { get; } = [];
    public ObservableCollection<string> ProfileNames { get; }
    public ObservableCollection<int> Resolutions { get; } = [];

    public IReadOnlyList<Choice<OutputFormat>> Formats { get; } =
        OutputFormats.All.Select(f => new Choice<OutputFormat>(f, f.DisplayName(), f.Description())).ToList();

    public IReadOnlyList<Choice<PageSizePreset>> PageSizes { get; } =
    [
        new(PageSizePreset.Auto, "Full scan area"),
        new(PageSizePreset.A4, "A4 (210 × 297 mm)"),
        new(PageSizePreset.Letter, "Letter (8.5 × 11 in)"),
        new(PageSizePreset.Legal, "Legal (8.5 × 14 in)"),
        new(PageSizePreset.A5, "A5 (148 × 210 mm)"),
        new(PageSizePreset.A3, "A3 (297 × 420 mm)"),
        new(PageSizePreset.B5, "B5 (176 × 250 mm)"),
        new(PageSizePreset.B4, "B4 (250 × 353 mm)"),
        new(PageSizePreset.Custom, "Custom size"),
    ];

    [ObservableProperty] private string? _selectedProfileName;
    [ObservableProperty] private DiscoveredScanner? _selectedScanner;
    [ObservableProperty] private ScannerCapabilities _capabilities = ScannerCapabilities.Unknown;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isLoadingCapabilities;

    [ObservableProperty] private ScanSource _source;
    [ObservableProperty] private ColorMode _color;
    [ObservableProperty] private int _dpi;
    [ObservableProperty] private Choice<OutputFormat>? _format;
    [ObservableProperty] private Choice<PageSizePreset>? _pageSize;
    [ObservableProperty] private decimal _customWidthMm;
    [ObservableProperty] private decimal _customHeightMm;
    [ObservableProperty] private int _brightness;
    [ObservableProperty] private int _contrast;
    [ObservableProperty] private bool _autoDeskew;
    [ObservableProperty] private bool _excludeBlankPages;
    [ObservableProperty] private bool _hasRegion;

    public bool HasScanner => SelectedScanner is not null;
    public bool NoScannerFound => !IsSearching && Scanners.Count == 0;

    public bool IsAuto { get => Source == ScanSource.Auto; set { if (value) Source = ScanSource.Auto; } }
    public bool IsFlatbed { get => Source == ScanSource.Flatbed; set { if (value) Source = ScanSource.Flatbed; } }
    public bool IsFeeder { get => Source is ScanSource.Feeder or ScanSource.Duplex; set { if (value) Source = Duplex ? ScanSource.Duplex : ScanSource.Feeder; } }

    public bool Duplex
    {
        get => Source == ScanSource.Duplex;
        set
        {
            if (IsFeeder)
            {
                Source = value ? ScanSource.Duplex : ScanSource.Feeder;
            }
        }
    }

    public bool IsColor { get => Color == ColorMode.Color; set { if (value) Color = ColorMode.Color; } }
    public bool IsGrayscale { get => Color == ColorMode.Grayscale; set { if (value) Color = ColorMode.Grayscale; } }
    public bool IsBlackAndWhite { get => Color == ColorMode.BlackAndWhite; set { if (value) Color = ColorMode.BlackAndWhite; } }

    public bool FlatbedSupported => Capabilities.Flatbed;
    public bool FeederSupported => Capabilities.Feeder;
    public bool DuplexSupported => Capabilities.Duplex;
    public bool ColorSupported => Capabilities.Supports(Source, ColorMode.Color);
    public bool GrayscaleSupported => Capabilities.Supports(Source, ColorMode.Grayscale);
    public bool BlackAndWhiteSupported => Capabilities.Supports(Source, ColorMode.BlackAndWhite);
    public bool IsCustomPageSize => PageSize?.Value == PageSizePreset.Custom;
    public bool CanPreview => HasScanner && Source == ScanSource.Flatbed;
    public bool IsMultiPageFormat => Format?.Value.IsMultiPage() == true;

    /// <summary>
    /// What the resolution box binds to. Rebuilding <see cref="Resolutions"/> makes the ComboBox push a null
    /// selection back; that is ignored here instead of failing the int conversion and blanking the box.
    /// </summary>
    public int? SelectedResolution
    {
        get => Resolutions.Contains(Dpi) ? Dpi : null;
        set
        {
            if (value is int dpi)
            {
                Dpi = dpi;
            }
        }
    }

    public string ResolutionHint => Dpi switch
    {
        < 150 => "Fast, for previews and on-screen reading",
        < 300 => "Good for documents you only read on screen",
        300 => "Recommended for documents and text recognition",
        < 600 => "Sharper detail, larger files",
        _ => "For photos and fine print; slow and very large files",
    };

    public string BrightnessLabel => Brightness == 0 ? "Default" : Brightness.ToString("+0;-0");
    public string ContrastLabel => Contrast == 0 ? "Default" : Contrast.ToString("+0;-0");

    public async Task DiscoverAsync()
    {
        _discoveryCts?.Cancel();
        _discoveryCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = _discoveryCts.Token;
        IsSearching = true;
        var previous = SelectedScanner;
        Scanners.Clear();
        OnPropertyChanged(nameof(NoScannerFound));
        try
        {
            await foreach (var scanner in _services.Scanner.DiscoverAsync(token))
            {
                Scanners.Add(scanner);
                OnPropertyChanged(nameof(NoScannerFound));
                if (SelectedScanner is null
                    && (scanner.Matches(Settings.DeviceId, Settings.DeviceDriver) || (previous?.Id == scanner.Id) || Settings.DeviceId is null))
                {
                    SelectedScanner = scanner;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!token.IsCancellationRequested || _discoveryCts.Token == token)
            {
                IsSearching = false;
            }
            SelectedScanner ??= Scanners.FirstOrDefault();
            OnPropertyChanged(nameof(NoScannerFound));
        }
    }

    [RelayCommand]
    private Task RefreshScanners() => DiscoverAsync();

    [RelayCommand]
    private void ResetBrightness() => Brightness = 0;

    [RelayCommand]
    private void ResetContrast() => Contrast = 0;

    [RelayCommand]
    private void ClearRegion()
    {
        Settings.Region = null;
        HasRegion = false;
        ScheduleSave();
    }

    public void SetRegion(ScanRegion? region)
    {
        Settings.Region = region is { IsValid: true } ? region : null;
        HasRegion = Settings.Region is not null;
        ScheduleSave();
    }

    [RelayCommand]
    private async Task SaveProfileAs()
    {
        var name = await _ui.PromptAsync("Save as profile", "Profile name", _services.Profiles.UniqueName(Settings.Name == "Default" ? "My profile" : Settings.Name), "Save");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        name = name.Trim();
        if (_services.Profiles.Exists(name) && !await _ui.ConfirmAsync("Replace profile?", $"A profile named \"{name}\" already exists. Replace it?", "Replace"))
        {
            return;
        }
        Settings.Name = name;
        _services.Profiles.Save(Settings);
        RefreshProfileNames();
        SelectProfile(name);
    }

    [RelayCommand]
    private async Task RenameProfile()
    {
        var oldName = Settings.Name;
        var name = await _ui.PromptAsync("Rename profile", "New name", oldName, "Rename");
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == oldName)
        {
            return;
        }
        if (!_services.Profiles.Rename(oldName, name))
        {
            await _ui.ShowMessageAsync("Can't rename profile", $"A profile named \"{name.Trim()}\" already exists.");
            return;
        }
        Settings.Name = name.Trim();
        RefreshProfileNames();
        SelectProfile(Settings.Name);
    }

    [RelayCommand]
    private async Task DeleteProfile()
    {
        if (_services.Profiles.Profiles.Count <= 1)
        {
            await _ui.ShowMessageAsync("Can't delete profile", "There must be at least one profile.");
            return;
        }
        if (!await _ui.ConfirmAsync("Delete profile?", $"Delete the profile \"{Settings.Name}\"?", "Delete"))
        {
            return;
        }
        _services.Profiles.Delete(Settings.Name);
        RefreshProfileNames();
        SelectProfile(_services.Profiles.Profiles[0].Name);
    }

    /// <summary>Flushes a pending profile save (on shutdown).</summary>
    public void SaveNow()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
        _services.Profiles.Save(Settings);
        _services.Settings.LastProfile = Settings.Name;
    }

    private void RefreshProfileNames()
    {
        _loading = true;
        ProfileNames.Clear();
        foreach (var profile in _services.Profiles.Profiles)
        {
            ProfileNames.Add(profile.Name);
        }
        _loading = false;
    }

    private void SelectProfile(string name)
    {
        _loading = true;
        SelectedProfileName = name;
        _loading = false;
        LoadProfile(name);
    }

    partial void OnSelectedProfileNameChanged(string? value)
    {
        if (!_loading && value is not null && value != Settings.Name)
        {
            SaveNow();
            LoadProfile(value);
        }
    }

    private void LoadProfile(string name)
    {
        Settings = _services.Profiles.Get(name).Clone();
        _services.Settings.LastProfile = Settings.Name;
        _services.SaveSettings();
        LoadFromSettings();
        var match = Scanners.FirstOrDefault(s => s.Matches(Settings.DeviceId, Settings.DeviceDriver));
        if (match is not null)
        {
            SelectedScanner = match;
        }
        CoerceToCapabilities();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        Source = Settings.Source;
        Color = Settings.Color;
        Dpi = Settings.Dpi;
        Format = Formats.First(f => f.Value == Settings.Format);
        PageSize = PageSizes.First(p => p.Value == Settings.PageSize);
        CustomWidthMm = Settings.CustomWidthMm;
        CustomHeightMm = Settings.CustomHeightMm;
        Brightness = Settings.Brightness;
        Contrast = Settings.Contrast;
        AutoDeskew = Settings.AutoDeskew;
        ExcludeBlankPages = Settings.ExcludeBlankPages;
        HasRegion = Settings.Region is not null;
        RebuildResolutions();
        _loading = false;
        RaiseDerived();
    }

    async partial void OnSelectedScannerChanged(DiscoveredScanner? value)
    {
        OnPropertyChanged(nameof(HasScanner));
        OnPropertyChanged(nameof(CanPreview));
        if (value is null)
        {
            return;
        }
        if (!_loading)
        {
            Settings.DeviceId = value.Id;
            Settings.DeviceName = value.Name;
            Settings.DeviceDriver = value.Driver;
            ScheduleSave();
        }
        _capsCts?.Cancel();
        _capsCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        IsLoadingCapabilities = true;
        try
        {
            Capabilities = await _services.Scanner.GetCapabilitiesAsync(value, _capsCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            IsLoadingCapabilities = false;
        }
        CoerceToCapabilities();
    }

    partial void OnCapabilitiesChanged(ScannerCapabilities value) => RaiseDerived();

    /// <summary>Moves options that the selected device can't do to the nearest thing it can.</summary>
    private void CoerceToCapabilities()
    {
        var caps = Capabilities;
        if (!caps.Supports(Source))
        {
            Source = caps.Flatbed ? ScanSource.Flatbed : caps.Feeder ? ScanSource.Feeder : ScanSource.Auto;
        }
        if (!caps.Supports(Source, Color))
        {
            Color = new[] { ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackAndWhite }.FirstOrDefault(m => caps.Supports(Source, m));
        }
        RebuildResolutions();
        RaiseDerived();
    }

    private void RebuildResolutions()
    {
        var wanted = Dpi > 0 ? Dpi : 300;
        var values = Capabilities.ResolutionsFor(Source);
        var wasLoading = _loading;
        _loading = true;
        Resolutions.Clear();
        foreach (var value in values)
        {
            Resolutions.Add(value);
        }
        _loading = wasLoading;
        Dpi = Capabilities.NearestResolution(Source, wanted);
        // Dpi may be unchanged (no change notification), but the ComboBox lost its selection when the list was cleared.
        OnPropertyChanged(nameof(SelectedResolution));
    }

    partial void OnSourceChanged(ScanSource value)
    {
        Settings.Source = value;
        if (!_loading)
        {
            // The preview region is kept but only applied to flatbed scans (see ScannerService.ScanAsync).
            RebuildResolutions();
        }
        RaiseDerived();
        ScheduleSave();
    }

    partial void OnColorChanged(ColorMode value)
    {
        Settings.Color = value;
        OnPropertyChanged(nameof(IsColor));
        OnPropertyChanged(nameof(IsGrayscale));
        OnPropertyChanged(nameof(IsBlackAndWhite));
        ScheduleSave();
    }

    partial void OnDpiChanged(int value)
    {
        if (!_loading && Resolutions.Count > 0 && !Resolutions.Contains(value))
        {
            Dpi = Capabilities.NearestResolution(Source, value);
            return;
        }
        if (value > 0)
        {
            Settings.Dpi = value;
        }
        OnPropertyChanged(nameof(ResolutionHint));
        OnPropertyChanged(nameof(SelectedResolution));
        ScheduleSave();
    }

    partial void OnFormatChanged(Choice<OutputFormat>? value)
    {
        if (value is not null)
        {
            Settings.Format = value.Value;
        }
        OnPropertyChanged(nameof(IsMultiPageFormat));
        ScheduleSave();
    }

    partial void OnPageSizeChanged(Choice<PageSizePreset>? value)
    {
        if (value is not null)
        {
            Settings.PageSize = value.Value;
        }
        OnPropertyChanged(nameof(IsCustomPageSize));
        ScheduleSave();
    }

    partial void OnCustomWidthMmChanged(decimal value)
    {
        Settings.CustomWidthMm = Math.Clamp(value, 10, 2000);
        ScheduleSave();
    }

    partial void OnCustomHeightMmChanged(decimal value)
    {
        Settings.CustomHeightMm = Math.Clamp(value, 10, 2000);
        ScheduleSave();
    }

    partial void OnBrightnessChanged(int value)
    {
        Settings.Brightness = value;
        OnPropertyChanged(nameof(BrightnessLabel));
        ScheduleSave();
    }

    partial void OnContrastChanged(int value)
    {
        Settings.Contrast = value;
        OnPropertyChanged(nameof(ContrastLabel));
        ScheduleSave();
    }

    partial void OnAutoDeskewChanged(bool value)
    {
        Settings.AutoDeskew = value;
        ScheduleSave();
    }

    partial void OnExcludeBlankPagesChanged(bool value)
    {
        Settings.ExcludeBlankPages = value;
        ScheduleSave();
    }

    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(NoScannerFound));

    private void RaiseDerived()
    {
        foreach (var name in new[]
        {
            nameof(IsAuto), nameof(IsFlatbed), nameof(IsFeeder), nameof(Duplex), nameof(FlatbedSupported), nameof(FeederSupported),
            nameof(DuplexSupported), nameof(ColorSupported), nameof(GrayscaleSupported), nameof(BlackAndWhiteSupported),
            nameof(IsColor), nameof(IsGrayscale), nameof(IsBlackAndWhite), nameof(CanPreview), nameof(IsCustomPageSize),
        })
        {
            OnPropertyChanged(name);
        }
    }

    private void ScheduleSave()
    {
        if (_loading)
        {
            return;
        }
        _saveTimer?.Dispose();
        _saveTimer = new Timer(_ => Avalonia.Threading.Dispatcher.UIThread.Post(SaveNow), null, 800, Timeout.Infinite);
    }
}
