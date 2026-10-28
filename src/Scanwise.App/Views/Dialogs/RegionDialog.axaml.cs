using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Scanwise.Core.Scanning;

namespace Scanwise.App.Views.Dialogs;

public partial class RegionDialog : UserControl
{
    public RegionDialog()
    {
        InitializeComponent();
    }

    public RegionDialog(Bitmap preview, ScanRegion? region)
        : this()
    {
        PreviewImage.Source = preview;
        Surface.Width = preview.PixelSize.Width;
        Surface.Height = preview.PixelSize.Height;
        Overlay.DisplayScale = Math.Min(560.0 / preview.PixelSize.Width, 560.0 / preview.PixelSize.Height);
        if (region is { IsValid: true })
        {
            Overlay.LeftEdge = region.Left;
            Overlay.TopEdge = region.Top;
            Overlay.RightEdge = region.Right;
            Overlay.BottomEdge = region.Bottom;
        }
    }

    /// <summary>The selection, or null when it covers (almost) the whole page.</summary>
    public ScanRegion? Region
    {
        get
        {
            var region = new ScanRegion(Overlay.LeftEdge, Overlay.TopEdge, Overlay.RightEdge, Overlay.BottomEdge);
            var whole = region.Left < 0.005 && region.Top < 0.005 && region.Right > 0.995 && region.Bottom > 0.995;
            return whole || !region.IsValid ? null : region;
        }
    }
}
