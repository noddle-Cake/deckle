# Third-party notices

Deckle's own code is under the Mozilla Public License 2.0 (`LICENSE`). Its release builds also contain the components
below, each under its own license. License texts are in this folder.

## How the copyleft components are used

- **NAPS2.Sdk and related packages (LGPL-2.1-or-later).** Used as separate, unmodified .NET libraries. Release builds
  keep them as individual DLLs next to the app, so you can replace them with your own build. Source:
  https://github.com/cyanfish/naps2
- **SANE, in macOS builds only (GPL-2.0, most of it with the SANE linking exception).** The `_mac` and `_macarm` folders
  contain unmodified binaries from the `NAPS2.Sane.Binaries` 1.2.3 package; Windows and Linux builds do not include
  them. Some backends do not carry the exception. See `sane/sane-backends-license.txt` and `sane/GPL-2.0.txt`.
  Source: https://gitlab.com/sane-project/backends and, for the build scripts,
  https://github.com/cyanfish/naps2-sane-binaries

### Written offer for GPL source code

For at least three years after each Deckle release, anyone may get the complete corresponding source code of the GPL
components in that release, for no more than the cost of physically providing it, by opening an issue at
https://github.com/noddle-Cake/deckle/issues.

## Other bundled data

- **Tesseract language data** (`tessdata`): Apache-2.0, https://github.com/tesseract-ocr/tessdata_fast
- **Lucide icons**: ISC, `src/Deckle.App/Assets/Fonts/LICENSE-lucide.txt`
- **.NET runtime**: MIT, https://github.com/dotnet/runtime
- **ImageSharp, ImageSharp.Drawing, Fonts**: Six Labors Split License, which grants Apache-2.0 to open-source software
  such as Deckle.

## NuGet packages

| Package | Version | License | Project |
|---|---|---|---|
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | see packages/Avalonia.Angle.Windows.Natives.txt | https://avaloniaui.net/ |
| Avalonia.BuildServices | 11.3.2 | MIT | https://avaloniaui.net/ |
| Avalonia.Controls.ColorPicker | 12.1.0 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| Avalonia.Controls.DataGrid | 12.1.0 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| Avalonia.Desktop | 12.1.4 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| Avalonia.Fonts.Inter | 12.1.4 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| Avalonia.FreeDesktop.AtSpi | 12.1.4 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| Avalonia.FreeDesktop | 12.1.4 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| Avalonia.HarfBuzz | 12.1.4 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| Avalonia.Native | 12.1.4 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| Avalonia.Remote.Protocol | 12.1.4 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| Avalonia.Skia | 12.1.4 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| Avalonia.Win32 | 12.1.4 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| Avalonia.X11 | 12.1.4 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| Avalonia | 12.1.4 | MIT | https://avaloniaui.net/?utm_source=nuget&amp;utm_medium=referral&amp;utm_content=project_homepage_link |
| BitMiracle.LibTiff.NET | 2.4.660 | https://bitmiracle.github.io/libtiff.net/help/articles/license.html | https://bitmiracle.com/libtiff/ |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet |
| FluentAvaloniaUI | 3.1.0 | MIT |  |
| Google.Protobuf | 3.29.3 | BSD-3-Clause | https://github.com/protocolbuffers/protobuf |
| Grpc.Core.Api | 2.67.0 | Apache-2.0 | https://github.com/grpc/grpc-dotnet |
| GrpcDotNetNamedPipes | 3.1.0 | see packages/GrpcDotNetNamedPipes.txt | https://github.com/cyanfish/grpc-dotnet-namedpipes |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.3 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| HarfBuzzSharp.NativeAssets.macOS | 8.3.1.3 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| HarfBuzzSharp.NativeAssets.WebAssembly | 8.3.1.3 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.3 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| HarfBuzzSharp | 8.3.1.3 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| Makaretu.Dns | 2.0.1 | see project | https://github.com/richardschneider/net-dns |
| MicroCom.Runtime | 0.11.6 | MIT |  |
| NAPS2.Escl | 1.6.0 | see packages/NAPS2.Escl.txt | https://www.naps2.com/sdk |
| NAPS2.Images.ImageSharp | 1.6.0 | see packages/NAPS2.Images.ImageSharp.txt | https://www.naps2.com/sdk |
| NAPS2.Images | 1.6.0 | see packages/NAPS2.Images.txt | https://www.naps2.com/sdk |
| NAPS2.Internals | 1.6.0 | see packages/NAPS2.Internals.txt | https://www.naps2.com/sdk |
| NAPS2.Mdns | 1.0.1 | MIT | https://github.com/cyanfish/net-mdns |
| NAPS2.NTwain | 1.0.1 | MIT | https://github.com/cyanfish/ntwain |
| NAPS2.Pdfium.Binaries | 1.3.0 | Apache-2.0 (packages/NAPS2.Pdfium.Binaries.txt) | https://github.com/cyanfish/naps2-pdfium |
| NAPS2.PdfSharp | 1.0.2 | see packages/NAPS2.PdfSharp.txt | https://github.com/cyanfish/naps2-pdfsharp |
| NAPS2.Sane.Binaries | 1.2.3 | GPL-2.0-only | https://github.com/cyanfish/naps2-sane |
| NAPS2.Sdk.Worker.Win32 | 1.6.0 | see packages/NAPS2.Sdk.Worker.Win32.txt | https://www.naps2.com/sdk |
| NAPS2.Sdk | 1.6.0 | see packages/NAPS2.Sdk.txt | https://www.naps2.com/sdk |
| NAPS2.Tesseract.Binaries | 1.4.0 | Apache-2.0 (packages/NAPS2.Tesseract.Binaries.txt) | https://github.com/cyanfish/naps2-tesseract |
| NAPS2.Wia | 2.0.3 | MIT | https://github.com/cyanfish/naps2-wia |
| SharpZipLib | 1.4.2 | MIT | https://github.com/icsharpcode/SharpZipLib |
| SimpleBase | 1.3.1 | Apache-2.0 | https://github.com/ssg/SimpleBase |
| SixLabors.Fonts | 2.1.3 | see packages/SixLabors.Fonts.txt | https://github.com/SixLabors/Fonts |
| SixLabors.ImageSharp.Drawing | 2.1.7 | see packages/SixLabors.ImageSharp.Drawing.txt | https://github.com/SixLabors/ImageSharp.Drawing |
| SixLabors.ImageSharp | 3.1.12 | see packages/SixLabors.ImageSharp.txt | https://github.com/SixLabors/ImageSharp |
| SkiaSharp.NativeAssets.Linux | 3.119.4 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| SkiaSharp.NativeAssets.macOS | 3.119.4 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| SkiaSharp.NativeAssets.WebAssembly | 3.119.4 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| SkiaSharp | 3.119.4 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| StandardSocketsHttpHandler | 2.2.0.10 | MIT | https://github.com/TalAloni/StandardSocketsHttpHandler |
| Tmds.DBus.Protocol | 0.94.1 | MIT |  |
| ZXing.Net | 0.16.11 | Apache-2.0 | https://github.com/micjahn/ZXing.Net/ |
