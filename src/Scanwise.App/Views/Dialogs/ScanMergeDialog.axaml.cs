using Avalonia.Controls;
using Scanwise.Core.Pages;

namespace Scanwise.App.Views.Dialogs;

public partial class ScanMergeDialog : UserControl
{
    private readonly int _pageCount;

    public ScanMergeDialog()
        : this(4)
    {
    }

    public ScanMergeDialog(int pageCount)
    {
        _pageCount = pageCount;
        InitializeComponent();
        var items = new List<string> { "Before page 1" };
        items.AddRange(Enumerable.Range(1, pageCount).Select(i => $"Page {i}"));
        StartBox.ItemsSource = items;
        StartBox.SelectedIndex = 1;
        StartBox.SelectionChanged += (_, _) => UpdatePreview();
        ReverseBox.IsCheckedChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    /// <summary>Existing page index after which the first new page goes (-1 = before page 1).</summary>
    public int AfterIndex => StartBox.SelectedIndex - 1;
    public bool Reverse => ReverseBox.IsChecked == true;

    private void UpdatePreview()
    {
        var existing = Enumerable.Range(1, _pageCount).Select(i => i.ToString()).ToList();
        var inserted = Enumerable.Range(1, _pageCount).Select(i => "N" + i).ToList();
        if (Reverse)
        {
            inserted.Reverse();
        }
        var merged = PageOrder.Merge(existing, inserted, AfterIndex);
        PreviewText.Text = string.Join(" ", merged) + "\n(numbers = existing pages, N = new pages in scan order)";
    }
}
