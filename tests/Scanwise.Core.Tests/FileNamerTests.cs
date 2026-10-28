using Scanwise.Core.Export;
using Scanwise.Core.Scanning;

namespace Scanwise.Core.Tests;

public class FileNamerTests
{
    private static readonly NamingContext Ctx = new(
        new DateTime(2026, 10, 28, 15, 4, 9), OutputFormat.Pdf, "Canon: LiDE/300", 300, ColorMode.Grayscale, ScanSource.Feeder, -10, 20);

    [Theory]
    [InlineData(FileNamer.DateTimePattern, "Scan 2026-10-28 15.04.09")]
    [InlineData(FileNamer.DatePattern, "Scan 2026-10-28")]
    [InlineData("{hour12}{ampm} {dpi}dpi {color} {source}", "03PM 300dpi Grayscale Feeder")]
    [InlineData("{brightness}_{contrast}_{format}", "-10_20_PDF")]
    [InlineData("{nnn}-{n}", "007-7")]
    [InlineData("{unknown} x", "{unknown} x")]
    public void RendersTokens(string pattern, string expected)
    {
        Assert.Equal(expected, FileNamer.Render(pattern, Ctx, counter: 7));
    }

    [Fact]
    public void SanitizesInvalidCharacters()
    {
        Assert.Equal("Canon_ LiDE_300", FileNamer.Render("{scanner}", Ctx));
        Assert.Equal("a_b", FileNamer.Sanitize("a?b. "));
    }

    [Fact]
    public void EmptyPatternFallsBackToDateTime()
    {
        Assert.Equal("Scan 2026-10-28 15.04.09", FileNamer.Render("  ", Ctx));
    }

    [Fact]
    public void AppendsSuffixOnCollision()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var first = FileNamer.ResolvePath(dir, FileNamer.DatePattern, Ctx);
            Assert.Equal(Path.Combine(dir, "Scan 2026-10-28.pdf"), first);
            File.WriteAllText(first, "");
            Assert.Equal(Path.Combine(dir, "Scan 2026-10-28 (2).pdf"), FileNamer.ResolvePath(dir, FileNamer.DatePattern, Ctx));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CounterSkipsTakenNumbers()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Doc 001.pdf"), "");
            File.WriteAllText(Path.Combine(dir, "Doc 002.pdf"), "");
            Assert.Equal(Path.Combine(dir, "Doc 003.pdf"), FileNamer.ResolvePath(dir, "Doc {nnn}", Ctx));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void MultiplePagesGetPageSuffixes()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var paths = FileNamer.ResolvePaths(dir, "Scan", Ctx with { Format = OutputFormat.Jpeg }, 3);
            Assert.Equal(["Scan (page 01).jpg", "Scan (page 02).jpg", "Scan (page 03).jpg"], paths.Select(Path.GetFileName));
            File.WriteAllText(paths[1], "");
            var next = FileNamer.ResolvePaths(dir, "Scan", Ctx with { Format = OutputFormat.Jpeg }, 3);
            Assert.Equal("Scan (2) (page 01).jpg", Path.GetFileName(next[0]));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
