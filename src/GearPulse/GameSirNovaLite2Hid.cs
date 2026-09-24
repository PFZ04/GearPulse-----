using System.IO;

namespace GearPulse;

// Only the Nova Lite 2 2.4 GHz device-info query verified against GameSir Connect.
// The output report requests status; it does not change configuration or firmware.
public static class GameSirNovaLite2Hid
{
    private const int Vendor = 0x3537;
    private const int WirelessProduct = 0x1098;
    private const int ReportLength = 65;

    public static bool IsWirelessInfoInterface(AtkMouseHid.Device device) =>
        device.Vendor == Vendor && device.Product == WirelessProduct &&
        device.UsagePage == 0xff7a && device.Usage == 1 &&
        device.InputLength == ReportLength && device.OutputLength == ReportLength;

    public static int? ParseBattery(ReadOnlySpan<byte> report)
    {
        if (report.Length != ReportLength || report[0] != 0 || report[1] != 1 ||
            report.Slice(2, 6).IndexOfAnyExcept((byte)0) < 0 || report[19] > 100)
            return null;
        return report[19];
    }

    public static int? ReadWireless(Guid containerId)
    {
        if (containerId == Guid.Empty) return null;
        try
        {
            var interfaces = AtkMouseHid.EnumerateVendor(Vendor)
                .Where(d => d.ContainerId == containerId && IsWirelessInfoInterface(d)).Take(2).ToArray();
            if (interfaces.Length != 1) return null;
            return ReadInterface(interfaces[0]);
        }
        catch (OperationCanceledException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (Exception error)
        {
            AppLog.Write("GameSir Nova Lite 2 battery query failed", error);
            return null;
        }
    }

    private static int? ReadInterface(AtkMouseHid.Device device)
    {
        using var handle = File.OpenHandle(device.Path, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite, FileOptions.Asynchronous);
        using var stream = new FileStream(handle, FileAccess.ReadWrite, ReportLength, isAsync: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));
        var request = new byte[ReportLength];
        request[1] = 1;
        request[2] = 1;
        var response = new byte[ReportLength];
        stream.WriteAsync(request, timeout.Token).GetAwaiter().GetResult();
        var pending = stream.ReadAsync(response, timeout.Token);
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var length = pending.GetAwaiter().GetResult();
            if (length == ReportLength)
            {
                var battery = ParseBattery(response);
                if (battery.HasValue) return battery;
            }
            if (attempt < 31) pending = stream.ReadAsync(response, timeout.Token);
        }
        return null;
    }
}
