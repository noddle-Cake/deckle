using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scanwise.App.Services;
using Scanwise.Core.Pages;

namespace Scanwise.App.ViewModels;

/// <summary>Thumbnail grid of all pages, kept in sync with the <see cref="PageDocument"/> by page id.</summary>
public sealed partial class PageListViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Dictionary<Guid, PageItemViewModel> _byId = [];

    public PageListViewModel(AppServices services)
    {
        _services = services;
        Document.Changed += (_, _) => Sync();
        SelectedItems.CollectionChanged += OnSelectionChanged;
    }

    public PageDocument Document => _services.Document;

    public ObservableCollection<PageItemViewModel> Items { get; } = [];
    public ObservableCollection<PageItemViewModel> SelectedItems { get; } = [];

    /// <summary>
    /// The page shown in the editor: the most recently selected page. Not bound to the ListBox, because a
    /// ListBox's SelectedItem setter collapses a multi-selection to one item.
    /// </summary>
    [ObservableProperty] private PageItemViewModel? _selectedItem;

    public event EventHandler? SelectionChanged;

    public bool HasPages => Items.Count > 0;
    public bool HasSelection => SelectedItems.Count > 0;
    public int Count => Items.Count;

    /// <summary>Selected pages, or all pages when nothing is selected.</summary>
    public IReadOnlyList<Guid> TargetIds => HasSelection ? SelectedIds : Items.Select(i => i.Id).ToList();

    public IReadOnlyList<Guid> SelectedIds =>
        SelectedItems.OrderBy(i => Items.IndexOf(i)).Select(i => i.Id).ToList();

    public string SelectionSummary => SelectedItems.Count switch
    {
        0 => Items.Count == 1 ? "1 page" : $"{Items.Count} pages",
        1 => $"Page {SelectedItems[0].Number} of {Items.Count}",
        var n => $"{n} of {Items.Count} pages selected",
    };

    public void Select(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        SelectedItems.Clear();
        foreach (var item in Items.Where(i => set.Contains(i.Id)))
        {
            SelectedItems.Add(item);
        }
        if (SelectedItems.Count > 0)
        {
            SelectedItem = SelectedItems[^1];
        }
    }

    [RelayCommand]
    private void SelectAll() => Select(Items.Select(i => i.Id));

    [RelayCommand]
    private void ClearSelection() => Select([]);

    [RelayCommand]
    private void RotateLeft() => Document.Rotate(TargetIds, -90);

    [RelayCommand]
    private void RotateRight() => Document.Rotate(TargetIds, 90);

    [RelayCommand]
    private void Rotate180() => Document.Rotate(TargetIds, 180);

    [RelayCommand]
    private void Duplicate()
    {
        if (HasSelection)
        {
            Document.Duplicate(SelectedIds);
        }
    }

    [RelayCommand]
    private void MoveUp() => Document.MoveUp(SelectedIds);

    [RelayCommand]
    private void MoveDown() => Document.MoveDown(SelectedIds);

    [RelayCommand]
    private void Reverse() => Document.Reverse(SelectedItems.Count > 1 ? SelectedIds : null);

    [RelayCommand]
    private void Interleave() => Document.Interleave();

    [RelayCommand]
    private void Deinterleave() => Document.Deinterleave();

    [RelayCommand]
    private void AltInterleave() => Document.AltInterleave();

    [RelayCommand]
    private void AltDeinterleave() => Document.AltDeinterleave();

    [RelayCommand]
    private void ResetEdits() => Document.ResetTransforms(TargetIds);

    /// <summary>Moves pages to a drop position (index in the list before the move).</summary>
    public void MoveTo(IReadOnlyList<Guid> ids, int index)
    {
        Document.Move(ids, index);
        Select(ids);
    }

    private void Sync()
    {
        var pages = Document.Pages;
        var live = pages.Select(p => p.Id).ToHashSet();

        // Remove pages that are gone.
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!live.Contains(Items[i].Id))
            {
                var item = Items[i];
                SelectedItems.Remove(item);
                Items.RemoveAt(i);
                _byId.Remove(item.Id);
                item.Release();
            }
        }

        // Insert, reorder and update in one pass.
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            if (!_byId.TryGetValue(page.Id, out var item))
            {
                item = new PageItemViewModel(page, _services.Renderer);
                _byId[page.Id] = item;
                Items.Insert(i, item);
            }
            else
            {
                var current = Items.IndexOf(item);
                if (current != i)
                {
                    Items.Move(current, i);
                }
                item.Page = page;
            }
            item.Number = i + 1;
        }

        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(SelectionSummary));
        if (SelectedItem is not null && !Items.Contains(SelectedItem))
        {
            SelectedItem = Items.LastOrDefault();
        }
    }

    private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is { Count: > 0 } added)
        {
            SelectedItem = (PageItemViewModel?)added[added.Count - 1];
        }
        else if (SelectedItem is not null && !SelectedItems.Contains(SelectedItem) && SelectedItems.Count > 0)
        {
            SelectedItem = SelectedItems[^1];
        }
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummary));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedItemChanged(PageItemViewModel? value) => SelectionChanged?.Invoke(this, EventArgs.Empty);
}
