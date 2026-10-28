using Avalonia.Controls;
using Scanwise.Core.Batch;

namespace Scanwise.App.Views.Dialogs;

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
