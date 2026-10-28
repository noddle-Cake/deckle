using NAPS2.Scan;

namespace Scanwise.Core.Scanning;

public sealed record DiscoveredScanner(ScanDevice Device)
{
    public string Id => Device.ID;
    public string Name => Device.Name;
    public Driver Driver => Device.Driver;

    /// <summary>Short protocol label shown next to the name, e.g. "SANE" or "Network (eSCL)".</summary>
    public string DriverLabel => Driver switch
    {
        Driver.Wia => "WIA",
        Driver.Twain => "TWAIN",
        Driver.Sane => "SANE",
        Driver.Escl => "Network (eSCL)",
        Driver.Apple => "macOS",
        _ => Driver.ToString(),
    };

    public string DisplayName => $"{Name} · {DriverLabel}";

    public bool Matches(string? id, Driver? driver) => id == Id && (driver is null || driver == Driver);
}
