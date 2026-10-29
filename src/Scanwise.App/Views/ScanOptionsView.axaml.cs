using Avalonia.Controls;
using Avalonia.Layout;
using Scanwise.App.Services;

namespace Scanwise.App.Views;

public partial class ScanOptionsView : UserControl
{
    public ScanOptionsView()
    {
        InitializeComponent();
        if (PlatformLook.IsToolbarLook)
        {
            // Scan moved to the toolbar / header bar, so Preview has the action bar to itself.
            Grid.SetColumnSpan(PreviewButton, 2);
            PreviewButton.Margin = default;
            PreviewButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            PreviewButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
    }
}
