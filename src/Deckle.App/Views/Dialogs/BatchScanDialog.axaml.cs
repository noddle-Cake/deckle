using Avalonia.Controls;
using Deckle.Core.Batch;

namespace Deckle.App.Views.Dialogs;

public partial class BatchScanDialog : UserControl
{
    public BatchScanDialog()
    {
        InitializeComponent();
    }

    public BatchOptions Options => new BatchOptions(
        (int)(double.IsNaN(CountBox.Value) ? 1 : CountBox.Value),
        TimeSpan.FromSeconds(double.IsNaN(DelayBox.Value) ? 0 : DelayBox.Value),
        PromptRadio.IsChecked == true,
        PerScanRadio.IsChecked == true ? BatchOutput.FilePerScan : BatchOutput.SingleFile).Normalize();
}
