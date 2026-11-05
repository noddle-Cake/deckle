# Security notes

## ImageSharp is pinned to 3.1.12

ImageSharp 3.1.12 is the version that `NAPS2.Images.ImageSharp` 1.6.0 depends on. It has six published
advisories. Each one's exposure in Deckle:

| Advisory | Issue | Exposure in Deckle |
|---|---|---|
| GHSA-v76p-62qx-wwq2 | Tiled-fax TIFF decode heap OOB write | **Mitigated:** imported TIFFs are decoded with LibTiff.NET (`Import/TiffReader.cs`) and never reach ImageSharp's TIFF decoder |
| GHSA-wmxv-xphr-5c9g | BigTIFF IFD loop (DoS) | **Mitigated:** same as above |
| GHSA-j9gm-c75j-xc9q | CCITT T4 encoder overflow | Not reachable: TIFF is written with LibTiff.NET |
| GHSA-jjfr-hcj7-qf5w | CCITT T6 encoder overflow | Not reachable: as above |
| GHSA-j3p4-wp97-rph4 | HistogramEqualization index | Not reachable: the processor is never used |
| GHSA-gwg2-r3hj-4w44 | ICC CLUT allocation (memory DoS) | **Open:** a crafted JPEG/PNG with an ICC profile can make an import use excessive memory |

The fixed versions (3.2.0+, 4.1.2+) add a **build-time license check**: Release builds fail unless a Six Labors
license key is configured. Six Labors offers free keys for open-source and small projects. To upgrade:

1. Get a key from https://sixlabors.com/pricing/.
2. Put it in `sixlabors.lic` at the repository root (gitignored), or pass `-p:SixLaborsLicenseKey=...`.
3. Set `SixLabors.ImageSharp` to `3.2.0` or later in `src/Deckle.Core/Deckle.Core.csproj`. The tests need
   `SixLabors.ImageSharp.Drawing` 2.2.0 or later.
4. Remove `NU1902;NU1903` from `WarningsNotAsErrors`.

The NuGet audit warnings are left visible on purpose.

## Lookalike NAPS2 repository

`github.com/naps2-software/naps2-scanner` is not the NAPS2 project. Its `404.html` loads a remote endpoint and
redirects visitors to it. Use https://github.com/cyanfish/naps2 and https://www.naps2.com only. Deckle uses NAPS2
only through the official `NAPS2.*` NuGet packages.
