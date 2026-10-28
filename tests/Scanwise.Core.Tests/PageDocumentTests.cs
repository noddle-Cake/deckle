using NAPS2.Images.Transforms;
using Scanwise.Core.Pages;

namespace Scanwise.Core.Tests;

public class PageDocumentTests
{
    [Fact]
    public void InterleaveFamilyMatchesNaps2()
    {
        int[] scanned = [1, 3, 5, 2, 4, 6];
        Assert.Equal([1, 2, 3, 4, 5, 6], PageOrder.Interleave(scanned));
        Assert.Equal(scanned, PageOrder.Deinterleave([1, 2, 3, 4, 5, 6]));
        Assert.Equal([1, 2, 3, 4, 5, 6], PageOrder.AltInterleave([1, 3, 5, 6, 4, 2]));
        Assert.Equal([1, 3, 5, 6, 4, 2], PageOrder.AltDeinterleave([1, 2, 3, 4, 5, 6]));
        Assert.Equal([1, 2, 3, 4, 5], PageOrder.Interleave([1, 3, 5, 2, 4]));
    }

    [Theory]
    [InlineData(0, new[] { 1, 10, 2, 11, 3, 12 })]
    [InlineData(1, new[] { 1, 2, 10, 3, 11, 12 })]
    [InlineData(-1, new[] { 10, 1, 11, 2, 12, 3 })]
    [InlineData(2, new[] { 1, 2, 3, 10, 11, 12 })]
    public void MergeInsertsOneNewPageAfterEachExisting(int afterIndex, int[] expected)
    {
        Assert.Equal(expected, PageOrder.Merge([1, 2, 3], [10, 11, 12], afterIndex));
    }

    [Fact]
    public void OperationsAreUndoable()
    {
        using var env = new TestEnv();
        using var doc = new PageDocument();
        var a = env.CreatePage();
        var b = env.CreatePage();
        var c = env.CreatePage();
        using (doc.BeginGroup())
        {
            doc.Add(a);
            doc.Add(b);
            doc.Add(c);
        }
        Assert.Equal(3, doc.Count);

        doc.Move([c.Id], 0);
        Assert.Equal([c.Id, a.Id, b.Id], doc.Pages.Select(p => p.Id));

        doc.MoveDown([c.Id]);
        Assert.Equal([a.Id, c.Id, b.Id], doc.Pages.Select(p => p.Id));

        doc.Rotate([a.Id], 90);
        Assert.Equal(90, ((RotationTransform)doc.Find(a.Id)!.Image.TransformState.Transforms.Single()).Angle);

        doc.Remove([b.Id]);
        doc.Duplicate([a.Id]);
        Assert.Equal(3, doc.Count);
        Assert.NotEqual(doc.Pages[0].Id, doc.Pages[1].Id);

        doc.Undo(); // duplicate
        doc.Undo(); // remove
        Assert.Equal([a.Id, c.Id, b.Id], doc.Pages.Select(p => p.Id));
        doc.Undo(); // rotate
        Assert.True(doc.Find(a.Id)!.Image.TransformState.IsEmpty);
        doc.Redo();
        Assert.False(doc.Find(a.Id)!.Image.TransformState.IsEmpty);

        doc.Undo();
        doc.Undo();
        doc.Undo();
        doc.Undo(); // the grouped add is one step
        Assert.Equal(0, doc.Count);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void ReverseSelectionOnly()
    {
        using var env = new TestEnv();
        using var doc = new PageDocument();
        var pages = Enumerable.Range(0, 4).Select(_ => env.CreatePage()).ToList();
        doc.AddRange(pages);
        doc.Reverse([pages[1].Id, pages[3].Id]);
        Assert.Equal([pages[0].Id, pages[3].Id, pages[2].Id, pages[1].Id], doc.Pages.Select(p => p.Id));
    }

    [Fact]
    public void ChangedFiresAndVersionIncrements()
    {
        using var env = new TestEnv();
        using var doc = new PageDocument();
        var fired = 0;
        doc.Changed += (_, _) => fired++;
        doc.Add(env.CreatePage());
        doc.Rotate([], 90); // nothing selected: no change
        Assert.Equal(1, fired);
        Assert.Equal(1, doc.Version);
    }
}
