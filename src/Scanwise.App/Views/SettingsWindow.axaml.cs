using Avalonia;
using Avalonia.Controls;
using Scanwise.App.Services;

namespace Scanwise.App.Views;

/// <summary>
/// Settings in their own window: macOS look = "Settings" with tabs (fixed size); GNOME look = "Preferences" with the
/// settings cards, which read as libadwaita boxed lists with switches.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        if (PlatformLook.IsGnome)
        {
            Title = "Preferences";
            Content = new SettingsView();
            CanResize = true;
            Width = 720;
            Height = 680;
            this[!BackgroundProperty] = this.GetResourceObservable("PaneBackground").ToBinding();
        }
    }
}
