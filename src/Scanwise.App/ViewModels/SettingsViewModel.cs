using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scanwise.App.Services;
using Scanwise.Core.Export;
using Scanwise.Core.Settings;
using PdfStandardKind = Scanwise.Core.Settings.PdfStandard;
using TiffCompressionKind = Scanwise.Core.Settings.TiffCompression;

namespace Scanwise.App.ViewModels;

/// <summary>Settings page. Every change is written to settings.json immediately.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IUiServices _ui;

    public SettingsViewModel(AppServices services, IUiServices ui)
    {
        _services = services;
        _ui = ui;
        var s = services.Settings;
        _theme = Themes.First(t => t.Value == s.Theme);
        _saveFolder = s.SaveFolder;
        _askEveryTime = s.SaveLocation == SaveLocationMode.AskEveryTime;
        _autoSave = s.AutoSave;
        _addToExisting = s.ScanAction == ScanAction.AddToExisting;
        _namingPreset = NamingPresets.FirstOrDefault(p => p.Value == s.FileNamePattern) ?? NamingPresets[^1];
        _customPattern = s.FileNamePattern;
        _autoRotate = s.AutoRotate;
        _reverseScanOrder = s.ReverseScanOrder;
        _ocrLanguage = s.OcrLanguage;
        _pdfOcr = s.Pdf.Ocr;
        _pdfStandard = PdfStandards.First(p => p.Value == s.Pdf.Standard);
        _pdfTitle = s.Pdf.Title ?? "";
        _pdfAuthor = s.Pdf.Author ?? "";
        _pdfEncrypt = s.Pdf.Encrypt;
        _pdfPassword = s.Pdf.UserPassword ?? "";
        _jpegQuality = s.Images.JpegQuality;
        _tiffCompression = TiffCompressions.First(t => t.Value == s.Images.TiffCompression);
        _restoreSession = s.RestoreSession;
        _scanHotkey = s.ScanHotkey;
        _scanHotkeyKey = HotkeyLetters.Contains(s.ScanHotkeyKey.ToUpperInvariant()) ? s.ScanHotkeyKey.ToUpperInvariant() : "S";
        RefreshOcr();
    }

    public IReadOnlyList<Choice<AppTheme>> Themes { get; } =
    [
        new(AppTheme.System, "Use system setting"),
        new(AppTheme.Light, "Light"),
        new(AppTheme.Dark, "Dark"),
    ];

    public IReadOnlyList<Choice<string>> NamingPresets { get; } =
    [
        new(FileNamer.DateTimePattern, "Date and time"),
        new(FileNamer.DatePattern, "Date only"),
        new("", "Custom"),
    ];

    public IReadOnlyList<Choice<PdfStandardKind>> PdfStandards { get; } =
    [
        new(PdfStandardKind.Default, "Standard PDF"),
        new(PdfStandardKind.PdfA1B, "PDF/A-1b (archival)"),
        new(PdfStandardKind.PdfA2B, "PDF/A-2b (archival)"),
        new(PdfStandardKind.PdfA3B, "PDF/A-3b (archival)"),
        new(PdfStandardKind.PdfA3U, "PDF/A-3u (archival, Unicode)"),
    ];

    public IReadOnlyList<Choice<TiffCompressionKind>> TiffCompressions { get; } =
    [
        new(TiffCompressionKind.Auto, "Automatic (CCITT G4 for black & white, LZW otherwise)"),
        new(TiffCompressionKind.Lzw, "LZW"),
        new(TiffCompressionKind.Ccitt4, "CCITT Group 4 (black & white only)"),
        new(TiffCompressionKind.None, "None"),
    ];

    public IReadOnlyList<Models.NamingTokenChip> Tokens { get; } =
        FileNamer.Tokens.Select(t => new Models.NamingTokenChip(t.Token, t.Label)).ToList();

    public ObservableCollection<string> OcrLanguages { get; } = [];

    [ObservableProperty] private Choice<AppTheme> _theme;
    [ObservableProperty] private string _saveFolder;
    [ObservableProperty] private bool _askEveryTime;
    [ObservableProperty] private bool _autoSave;
    [ObservableProperty] private bool _addToExisting;
    [ObservableProperty] private Choice<string> _namingPreset;
    [ObservableProperty] private string _customPattern;
    [ObservableProperty] private bool _autoRotate;
    [ObservableProperty] private bool _reverseScanOrder;
    [ObservableProperty] private bool _scanHotkey;
    [ObservableProperty] private string _scanHotkeyKey;
    [ObservableProperty] private string _ocrLanguage;
    [ObservableProperty] private bool _pdfOcr;
    [ObservableProperty] private Choice<PdfStandardKind> _pdfStandard;
    [ObservableProperty] private string _pdfTitle;
    [ObservableProperty] private string _pdfAuthor;
    [ObservableProperty] private bool _pdfEncrypt;
    [ObservableProperty] private string _pdfPassword;
    [ObservableProperty] private int _jpegQuality;
    [ObservableProperty] private Choice<TiffCompressionKind> _tiffCompression;
    [ObservableProperty] private bool _restoreSession;
    [ObservableProperty] private string _ocrStatus = "";
    [ObservableProperty] private bool _ocrAvailable;
    [ObservableProperty] private bool _hasOsd;
    [ObservableProperty] private string _downloadLanguage = "";
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;

    public bool IsCustomNaming => NamingPreset.Value.Length == 0;
    public bool CanEncrypt => PdfStandard.Value == PdfStandardKind.Default;
    public string NamingPreview => FileNamer.Preview(_services.Settings.FileNamePattern, OutputFormat.Pdf);
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "";

    private void Save(Action<AppSettings> change)
    {
        change(_services.Settings);
        _services.SaveSettings();
    }

    partial void OnThemeChanged(Choice<AppTheme> value)
    {
        Save(s => s.Theme = value.Value);
        App.ApplyTheme(value.Value);
    }

    partial void OnAskEveryTimeChanged(bool value) => Save(s => s.SaveLocation = value ? SaveLocationMode.AskEveryTime : SaveLocationMode.Folder);
    partial void OnAutoSaveChanged(bool value) => Save(s => s.AutoSave = value);
    partial void OnAddToExistingChanged(bool value) => Save(s => s.ScanAction = value ? ScanAction.AddToExisting : ScanAction.StartFresh);
    partial void OnAutoRotateChanged(bool value) => Save(s => s.AutoRotate = value);
    partial void OnReverseScanOrderChanged(bool value) => Save(s => s.ReverseScanOrder = value);

    // ---------------------------------------------------------------- Scan shortcut

    public IReadOnlyList<string> HotkeyLetters { get; } = Enumerable.Range('A', 26).Select(c => ((char)c).ToString()).ToList();
    public bool HotkeyIsAutomatic => ScanShortcuts.HotkeyKind != ScanHotkeyKind.Manual;
    public bool HotkeyIsManual => !HotkeyIsAutomatic;
    public string ScanCommandLine => ScanShortcuts.ScanCommand;

    public string HotkeyDescription
    {
        get
        {
            var label = ScanShortcuts.KeyLabel(ScanHotkeyKey[0]);
            return ScanShortcuts.HotkeyKind switch
            {
                ScanHotkeyKind.Global when OperatingSystem.IsWindows() =>
                    $"{label} scans from anywhere, even when Scanwise is closed. Adds a \"Scanwise Scan\" shortcut to the Start menu.",
                ScanHotkeyKind.Global => $"{label} scans from anywhere, even when Scanwise is closed. Added to GNOME's keyboard shortcuts.",
                ScanHotkeyKind.WhileRunning => $"{label} scans from any app while Scanwise is open.",
                _ => "Add a custom shortcut in your desktop's keyboard settings that runs this command:",
            };
        }
    }

    public string LauncherHint =>
        OperatingSystem.IsWindows() ? "Right-click Scanwise in the taskbar and choose Scan. Pin Scanwise to the taskbar to keep it there."
        : OperatingSystem.IsMacOS() ? "Right-click Scanwise in the Dock and choose Scan."
        : "Right-click Scanwise in the dock or app list and choose Scan.";

    private bool _revertingHotkey;

    partial void OnScanHotkeyChanged(bool value) => _ = ApplyHotkeyAsync();

    partial void OnScanHotkeyKeyChanged(string value)
    {
        OnPropertyChanged(nameof(HotkeyDescription));
        if (ScanHotkey)
        {
            _ = ApplyHotkeyAsync();
        }
        else if (!_revertingHotkey)
        {
            Save(s => s.ScanHotkeyKey = value);
        }
    }

    private async Task ApplyHotkeyAsync()
    {
        if (_revertingHotkey)
        {
            return;
        }
        var enabled = ScanHotkey;
        var key = ScanHotkeyKey;
        try
        {
            await _services.Shortcuts.ApplyHotkeyAsync(enabled, key[0]);
            Save(s =>
            {
                s.ScanHotkey = enabled;
                s.ScanHotkeyKey = key;
            });
        }
        catch (InvalidOperationException ex)
        {
            _revertingHotkey = true;
            ScanHotkey = _services.Settings.ScanHotkey;
            ScanHotkeyKey = _services.Settings.ScanHotkeyKey;
            _revertingHotkey = false;
            await _ui.ShowMessageAsync("Shortcut not set", ex.Message);
        }
    }

    [RelayCommand]
    private Task CopyScanCommandLine() => _ui.CopyTextAsync(ScanCommandLine);
    partial void OnPdfOcrChanged(bool value) => Save(s => s.Pdf.Ocr = value);
    partial void OnPdfTitleChanged(string value) => Save(s => s.Pdf.Title = string.IsNullOrWhiteSpace(value) ? null : value);
    partial void OnPdfAuthorChanged(string value) => Save(s => s.Pdf.Author = string.IsNullOrWhiteSpace(value) ? null : value);
    partial void OnPdfEncryptChanged(bool value) => Save(s => s.Pdf.Encrypt = value);
    partial void OnPdfPasswordChanged(string value) => Save(s => s.Pdf.UserPassword = string.IsNullOrEmpty(value) ? null : value);
    partial void OnJpegQualityChanged(int value) => Save(s => s.Images.JpegQuality = Math.Clamp(value, 1, 100));
    partial void OnTiffCompressionChanged(Choice<TiffCompressionKind> value) => Save(s => s.Images.TiffCompression = value.Value);
    partial void OnRestoreSessionChanged(bool value) => Save(s => s.RestoreSession = value);

    partial void OnOcrLanguageChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Save(s => s.OcrLanguage = value);
        }
    }

    partial void OnPdfStandardChanged(Choice<PdfStandardKind> value)
    {
        Save(s => s.Pdf.Standard = value.Value);
        OnPropertyChanged(nameof(CanEncrypt));
    }

    partial void OnNamingPresetChanged(Choice<string> value)
    {
        if (value.Value.Length > 0)
        {
            Save(s => s.FileNamePattern = value.Value);
            CustomPattern = value.Value;
        }
        OnPropertyChanged(nameof(IsCustomNaming));
        OnPropertyChanged(nameof(NamingPreview));
    }

    partial void OnCustomPatternChanged(string value)
    {
        if (IsCustomNaming && !string.IsNullOrWhiteSpace(value))
        {
            Save(s => s.FileNamePattern = value);
        }
        OnPropertyChanged(nameof(NamingPreview));
    }

    [RelayCommand]
    private void InsertToken(string token)
    {
        if (!IsCustomNaming)
        {
            NamingPreset = NamingPresets[^1];
        }
        CustomPattern = (CustomPattern ?? "").TrimEnd() + (string.IsNullOrEmpty(CustomPattern) ? "" : " ") + token;
    }

    [RelayCommand]
    private async Task ChooseFolder()
    {
        var folder = await _ui.PickFolderAsync(SaveFolder);
        if (folder is not null)
        {
            SaveFolder = folder;
            Save(s => s.SaveFolder = folder);
        }
    }

    [RelayCommand]
    private Task OpenFolder() => _ui.OpenFolderAsync(SaveFolder);

    [RelayCommand]
    private async Task DownloadOcrLanguage(string? language)
    {
        language = (language ?? DownloadLanguage).Trim().ToLowerInvariant();
        if (language.Length == 0)
        {
            return;
        }
        IsDownloading = true;
        DownloadProgress = 0;
        try
        {
            await _services.Tesseract.DownloadLanguageAsync(language, new Progress<double>(p => DownloadProgress = p * 100), CancellationToken.None);
            DownloadLanguage = "";
            RefreshOcr();
            if (language != "osd")
            {
                OcrLanguage = language;
            }
        }
        catch (Exception ex)
        {
            await _ui.ShowMessageAsync("Download failed", $"Could not download \"{language}\": {ex.Message}\n\nLanguage codes are three letters, for example eng, deu, fra, spa, chi_sim.");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private Task OpenUrl(string url) => _ui.OpenUrlAsync(url);

    public void RefreshOcr()
    {
        _services.Tesseract.Refresh();
        var install = _services.Tesseract.Find();
        OcrLanguages.Clear();
        if (install is null)
        {
            OcrAvailable = false;
            HasOsd = false;
            OcrStatus = "Tesseract OCR was not found. Install it (for example \"sudo dnf install tesseract\" or \"brew install tesseract\") to enable auto-rotation and searchable PDFs.";
            return;
        }
        foreach (var language in install.Languages)
        {
            OcrLanguages.Add(language);
        }
        OcrAvailable = true;
        HasOsd = install.HasOsd;
        var source = install.IsBundled ? "bundled Tesseract" : install.ExePath;
        OcrStatus = install.Languages.Count == 0
            ? $"Using {source}. No languages installed yet; download one below."
            : $"Using {source} with {install.Languages.Count} language(s).";
        if (!OcrLanguages.Contains(OcrLanguage) && OcrLanguages.Count > 0)
        {
            OcrLanguage = OcrLanguages.Contains("eng") ? "eng" : OcrLanguages[0];
        }
    }
}
