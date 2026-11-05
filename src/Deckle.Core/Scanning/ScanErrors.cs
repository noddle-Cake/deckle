using NAPS2.Scan.Exceptions;

namespace Deckle.Core.Scanning;

/// <summary>Turns driver exceptions into one-line messages a user can act on.</summary>
public static class ScanErrors
{
    public static string Describe(Exception ex) => ex switch
    {
        DeviceFeederEmptyException => "The document feeder is empty. Load paper and try again.",
        DevicePaperJamException => "Paper is jammed in the scanner. Clear the jam and try again.",
        DeviceCoverOpenException => "The scanner cover is open. Close it and try again.",
        DeviceBusyException => "The scanner is busy. Wait for it to finish, then try again.",
        DeviceWarmingUpException => "The scanner is warming up. Try again in a few seconds.",
        DeviceOfflineException => "The scanner is offline. Check that it is turned on and connected.",
        DeviceNotFoundException => "The scanner could not be found. Check the connection and refresh the scanner list.",
        DeviceCommunicationException => "Lost contact with the scanner. Check the cable or network connection.",
        NoFeederSupportException => "This scanner has no document feeder. Choose Flatbed instead.",
        NoDuplexSupportException => "This scanner cannot scan both sides. Turn off duplex.",
        DriverNotSupportedException => "This scanner driver is not available on this system.",
        OperationCanceledException => "Scan cancelled.",
        ScanDriverException sde when !string.IsNullOrWhiteSpace(sde.Message) => sde.Message,
        _ => $"Scanning failed: {ex.Message}",
    };

    /// <summary>Errors that mean "nothing more to scan" rather than a failure.</summary>
    public static bool IsBenign(Exception ex) => ex is OperationCanceledException;
}
