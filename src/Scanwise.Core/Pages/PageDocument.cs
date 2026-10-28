using NAPS2.Images;
using NAPS2.Images.Transforms;

namespace Scanwise.Core.Pages;

/// <summary>
/// The ordered list of pages with undo/redo. Every mutation records a snapshot (a list of immutable
/// <see cref="Page"/> references), so undo is cheap. Images are disposed once neither the current list nor any
/// snapshot refers to them. Not thread-safe: call from one thread (the UI thread).
/// </summary>
public sealed class PageDocument : IDisposable
{
    public const int MaxUndo = 50;

    private readonly LinkedList<IReadOnlyList<Page>> _undo = new();
    private readonly Stack<IReadOnlyList<Page>> _redo = new();
    private readonly HashSet<ProcessedImage> _owned = new(ReferenceEqualityComparer.Instance);
    private List<Page> _pages = [];
    private int _groupDepth;
    private bool _groupSnapshotTaken;

    public event EventHandler? Changed;

    public IReadOnlyList<Page> Pages => _pages;
    public int Count => _pages.Count;

    /// <summary>Incremented on every change; compare with a saved value to detect unsaved edits.</summary>
    public int Version { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public Page? Find(Guid id) => _pages.FirstOrDefault(p => p.Id == id);
    public int IndexOf(Guid id) => _pages.FindIndex(p => p.Id == id);

    /// <summary>Groups several mutations into one undo step (e.g. all pages of one scan).</summary>
    public IDisposable BeginGroup()
    {
        if (_groupDepth++ == 0)
        {
            _groupSnapshotTaken = false;
        }
        return new Group(this);
    }

    public void Add(Page page) => Insert(_pages.Count, [page]);

    public void AddRange(IEnumerable<Page> pages) => Insert(_pages.Count, pages);

    public void Insert(int index, IEnumerable<Page> pages)
    {
        var list = pages.ToList();
        if (list.Count == 0)
        {
            return;
        }
        Mutate(current =>
        {
            current.InsertRange(Math.Clamp(index, 0, current.Count), list);
            return current;
        });
    }

    public void Remove(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        if (!_pages.Any(p => set.Contains(p.Id)))
        {
            return;
        }
        Mutate(current =>
        {
            current.RemoveAll(p => set.Contains(p.Id));
            return current;
        });
    }

    public void Clear()
    {
        if (_pages.Count == 0)
        {
            return;
        }
        Mutate(_ => []);
    }

    /// <summary>Moves the given pages (kept in their current relative order) so the first lands at <paramref name="targetIndex"/>.</summary>
    public void Move(IEnumerable<Guid> ids, int targetIndex)
    {
        var set = ids.ToHashSet();
        Mutate(current =>
        {
            var moving = current.Where(p => set.Contains(p.Id)).ToList();
            if (moving.Count == 0)
            {
                return null;
            }
            // Target index is in terms of the list before removal; adjust for removed items in front of it.
            var before = current.Take(Math.Clamp(targetIndex, 0, current.Count)).Count(p => set.Contains(p.Id));
            current.RemoveAll(p => set.Contains(p.Id));
            current.InsertRange(Math.Clamp(targetIndex - before, 0, current.Count), moving);
            return current;
        });
    }

    public void MoveUp(IEnumerable<Guid> ids) => Shift(ids, -1);
    public void MoveDown(IEnumerable<Guid> ids) => Shift(ids, +1);

    /// <summary>Inserts a copy of each page right after it.</summary>
    public void Duplicate(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        Mutate(current =>
        {
            var result = new List<Page>(current.Count + set.Count);
            foreach (var page in current)
            {
                result.Add(page);
                if (set.Contains(page.Id))
                {
                    result.Add(new Page(page.Image.Clone(), page.Info));
                }
            }
            return result.Count == current.Count ? null : result;
        });
    }

    /// <summary>Reverses the selected pages in place (or the whole list when <paramref name="ids"/> is null).</summary>
    public void Reverse(IEnumerable<Guid>? ids = null)
    {
        var set = ids?.ToHashSet();
        Mutate(current =>
        {
            var positions = Enumerable.Range(0, current.Count).Where(i => set is null || set.Contains(current[i].Id)).ToList();
            if (positions.Count < 2)
            {
                return null;
            }
            var reversed = positions.Select(i => current[i]).Reverse().ToList();
            for (var k = 0; k < positions.Count; k++)
            {
                current[positions[k]] = reversed[k];
            }
            return current;
        });
    }

    /// <summary>Fronts then backs (1,3,5,2,4,6) → 1,2,3,4,5,6. For duplex scanned as two single-sided passes.</summary>
    public void Interleave() => Reorder(PageOrder.Interleave);

    public void Deinterleave() => Reorder(PageOrder.Deinterleave);

    /// <summary>Like <see cref="Interleave"/> but the backs were scanned in reverse (1,3,5,6,4,2).</summary>
    public void AltInterleave() => Reorder(PageOrder.AltInterleave);

    public void AltDeinterleave() => Reorder(PageOrder.AltDeinterleave);

    /// <summary>
    /// Scanner's "scan merge": places one new page after each existing page, starting after the existing page at
    /// <paramref name="afterIndex"/> (-1 = before the first page). Extra new pages go to the end.
    /// </summary>
    public void MergeInto(IReadOnlyList<Page> newPages, int afterIndex, bool reverseNewPages)
    {
        if (newPages.Count == 0)
        {
            return;
        }
        Mutate(current => PageOrder.Merge(current, reverseNewPages ? newPages.Reverse().ToList() : newPages, afterIndex));
    }

    public void ApplyTransform(IEnumerable<Guid> ids, Transform transform) =>
        Update(ids, page => page.Image.WithTransform(transform, disposeSelf: false));

    public void ApplyTransform(IEnumerable<Guid> ids, Func<Page, Transform?> transformFor) =>
        Update(ids, page => transformFor(page) is { } t ? page.Image.WithTransform(t, disposeSelf: false) : null);

    /// <summary>Rotates clockwise by <paramref name="degrees"/>.</summary>
    public void Rotate(IEnumerable<Guid> ids, double degrees) => ApplyTransform(ids, new RotationTransform(degrees));

    public void ResetTransforms(IEnumerable<Guid> ids) =>
        Update(ids, page => page.Image.TransformState.IsEmpty ? null : page.Image.WithNoTransforms(disposeSelf: false));

    /// <summary>Replaces images. <paramref name="newImage"/> returns null to leave a page unchanged.</summary>
    public void Update(IEnumerable<Guid> ids, Func<Page, ProcessedImage?> newImage)
    {
        var set = ids.ToHashSet();
        Mutate(current =>
        {
            var changed = false;
            for (var i = 0; i < current.Count; i++)
            {
                if (set.Contains(current[i].Id) && newImage(current[i]) is { } image)
                {
                    current[i] = current[i].WithImage(image);
                    changed = true;
                }
            }
            return changed ? current : null;
        });
    }

    public void Undo()
    {
        if (_undo.Last is not { } last)
        {
            return;
        }
        _undo.RemoveLast();
        _redo.Push(_pages.ToList());
        _pages = last.Value.ToList();
        AfterChange();
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var next))
        {
            return;
        }
        _undo.AddLast(_pages.ToList());
        _pages = next.ToList();
        AfterChange();
    }

    /// <summary>Replaces everything without an undo step (session restore).</summary>
    public void Load(IEnumerable<Page> pages)
    {
        _undo.Clear();
        _redo.Clear();
        _pages = pages.ToList();
        AfterChange();
    }

    public void Dispose()
    {
        foreach (var image in _owned)
        {
            image.Dispose();
        }
        _owned.Clear();
        _pages.Clear();
        _undo.Clear();
        _redo.Clear();
    }

    private void Shift(IEnumerable<Guid> ids, int direction)
    {
        var set = ids.ToHashSet();
        Mutate(current =>
        {
            var changed = false;
            var range = direction < 0 ? Enumerable.Range(1, Math.Max(0, current.Count - 1)) : Enumerable.Range(0, Math.Max(0, current.Count - 1)).Reverse();
            foreach (var i in range)
            {
                var j = i + direction;
                if (set.Contains(current[i].Id) && !set.Contains(current[j].Id))
                {
                    (current[i], current[j]) = (current[j], current[i]);
                    changed = true;
                }
            }
            return changed ? current : null;
        });
    }

    private void Reorder(Func<IReadOnlyList<Page>, List<Page>> order)
    {
        if (_pages.Count < 2)
        {
            return;
        }
        Mutate(current => order(current));
    }

    /// <summary>Applies <paramref name="change"/> to a copy of the page list. Returning null means "no change".</summary>
    private void Mutate(Func<List<Page>, List<Page>?> change)
    {
        var before = _pages;
        var after = change(_pages.ToList());
        if (after is null)
        {
            return;
        }
        if (_groupDepth == 0 || !_groupSnapshotTaken)
        {
            _undo.AddLast(before);
            while (_undo.Count > MaxUndo)
            {
                _undo.RemoveFirst();
            }
            _groupSnapshotTaken = true;
        }
        _redo.Clear();
        _pages = after;
        AfterChange();
    }

    private void AfterChange()
    {
        Version++;
        foreach (var page in _pages)
        {
            _owned.Add(page.Image);
        }
        CollectGarbage();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void CollectGarbage()
    {
        var live = new HashSet<ProcessedImage>(ReferenceEqualityComparer.Instance);
        foreach (var page in _pages.Concat(_undo.SelectMany(s => s)).Concat(_redo.SelectMany(s => s)))
        {
            live.Add(page.Image);
        }
        foreach (var dead in _owned.Where(i => !live.Contains(i)).ToList())
        {
            dead.Dispose();
            _owned.Remove(dead);
        }
    }

    private sealed class Group(PageDocument owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner._groupDepth--;
            }
        }
    }
}

/// <summary>Pure page-order algorithms (NAPS2's interleave family and Scanner's merge).</summary>
public static class PageOrder
{
    public static List<T> Interleave<T>(IReadOnlyList<T> pages)
    {
        var fronts = (pages.Count + 1) / 2;
        var result = new List<T>(pages.Count);
        for (var i = 0; i < fronts; i++)
        {
            result.Add(pages[i]);
            if (fronts + i < pages.Count)
            {
                result.Add(pages[fronts + i]);
            }
        }
        return result;
    }

    public static List<T> Deinterleave<T>(IReadOnlyList<T> pages) =>
        pages.Where((_, i) => i % 2 == 0).Concat(pages.Where((_, i) => i % 2 == 1)).ToList();

    public static List<T> AltInterleave<T>(IReadOnlyList<T> pages)
    {
        var fronts = (pages.Count + 1) / 2;
        var reordered = pages.Take(fronts).Concat(pages.Skip(fronts).Reverse()).ToList();
        return Interleave(reordered);
    }

    public static List<T> AltDeinterleave<T>(IReadOnlyList<T> pages)
    {
        var fronts = pages.Where((_, i) => i % 2 == 0);
        var backs = pages.Where((_, i) => i % 2 == 1).Reverse();
        return fronts.Concat(backs).ToList();
    }

    public static List<T> Merge<T>(IReadOnlyList<T> existing, IReadOnlyList<T> inserted, int afterIndex)
    {
        var result = new List<T>(existing.Count + inserted.Count);
        var next = 0;
        if (afterIndex < 0 && next < inserted.Count)
        {
            result.Add(inserted[next++]);
        }
        for (var i = 0; i < existing.Count; i++)
        {
            result.Add(existing[i]);
            if (i >= afterIndex && afterIndex >= 0 && next < inserted.Count)
            {
                result.Add(inserted[next++]);
            }
            else if (afterIndex < 0 && next < inserted.Count)
            {
                result.Add(inserted[next++]);
            }
        }
        while (next < inserted.Count)
        {
            result.Add(inserted[next++]);
        }
        return result;
    }
}
