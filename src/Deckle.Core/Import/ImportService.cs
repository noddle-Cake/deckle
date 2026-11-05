using System.Runtime.CompilerServices;
using NAPS2.Images;
using NAPS2.ImportExport;
using NAPS2.Pdf;
using NAPS2.Scan;
using Deckle.Core.Pages;

namespace Deckle.Core.Import;

/// <summary>Imports images (including multi-page TIFF) and PDFs as pages.</summary>
public sealed class ImportService
{
    public static IReadOnlyList<string> SupportedExtensions { get; } = [".pdf", ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"];

    private readonly ScanningContext _scanning;
    private readonly string _tempDir;

    public ImportService(ScanningContext scanning, string tempDir)
    {
        _scanning = scanning;
        _tempDir = tempDir;
    }

    /// <summary>Asked for the password of an encrypted PDF; return null to skip the file.</summary>
    public Func<string, int, string?>? PasswordPrompt { get; set; }

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public async IAsyncEnumerable<Page> ImportAsync(
        string path,
        IProgress<(int Done, int Total)>? progress = null,
        [EnumeratorCancellation] CancellationToken cancelToken = default)
    {
        var importer = new FileImporter(
            new PdfImporter(_scanning, new PasswordProvider(this)),
            new ImageImporter(_scanning));
        var handler = new NAPS2.Util.ProgressHandler((done, total) => progress?.Report((done, total)), cancelToken);
        var info = PageInfo.FromImport(path);

        if (TiffReader.IsTiff(path))
        {
            var frames = await Task.Run(() => TiffReader.SplitToPng(path, _tempDir, cancelToken), cancelToken).ConfigureAwait(false);
            try
            {
                for (var i = 0; i < frames.Count; i++)
                {
                    await foreach (var image in importer.Import(frames[i], new ImportParams(), cancelToken).ConfigureAwait(false))
                    {
                        yield return new Page(image, info);
                    }
                    progress?.Report((i + 1, frames.Count));
                }
            }
            finally
            {
                foreach (var frame in frames)
                {
                    try
                    {
                        File.Delete(frame);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
            yield break;
        }

        await foreach (var image in importer.Import(path, new ImportParams(), handler).WithCancellation(cancelToken).ConfigureAwait(false))
        {
            yield return new Page(image, info);
        }
    }

    private sealed class PasswordProvider(ImportService owner) : IPdfPasswordProvider
    {
        public bool ProvidePassword(string fileName, int attemptCount, out string password)
        {
            password = owner.PasswordPrompt?.Invoke(fileName, attemptCount) ?? "";
            return password.Length > 0;
        }
    }
}
