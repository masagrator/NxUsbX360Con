using System.Diagnostics.CodeAnalysis;

namespace SwitchInputServer;

/// <summary>
/// Enumerates every USB device visible to LibUsbDotNet and prints its
/// VID, PID, and the decimal values ready to paste into appsettings.json.
///
/// LibUsbDotNet 3.x note:
///   IUsbDevice does NOT expose Manufacturer / Product string-descriptor
///   properties.  Only VendorId and ProductId are available without opening
///   the device and issuing raw control requests.
/// </summary>
public static class UsbScanner
{
    public static void ScanDevices()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine("  USB Device Scanner  (LibUsbDotNet 3.x)");
        Console.WriteLine();
        Console.WriteLine("  TIP: Launch SwitchInputClient.nro on the Switch first,");
        Console.WriteLine("  then re-run --scan — the new entry is your device.");
        Console.WriteLine("═══════════════════════════════════════════════════════════");
        Console.WriteLine();

        using var ctx = new UsbContext();
        var devices = ctx.List().ToList();

        if (devices.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  No USB devices found.");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("  • Ensure the USB cable is connected.");
            Console.WriteLine("  • If the Switch is present but invisible, install WinUSB");
            Console.WriteLine("    via Zadig first so LibUsbDotNet can enumerate it.");
            return;
        }

        int index = 0;
        foreach (var dev in devices)
        {
            index++;
            Console.WriteLine(
                $"  [{index:D3}]  VID=0x{dev.VendorId:X4}  PID=0x{dev.ProductId:X4}");

            // ── Optional: attempt to open and read string descriptors ────
            //    This succeeds only if WinUSB (or libusb-win32) is already
            //    installed for this specific device via Zadig.
            TryPrintStrings(dev);

            // Print the decimal values ready for appsettings.json
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(
                $"         → appsettings.json:  " +
                $"\"VendorId\": {dev.VendorId},  \"ProductId\": {dev.ProductId}");
            Console.ResetColor();
            Console.WriteLine();
        }

        Console.WriteLine($"  Total: {index} device(s) found.");
        Console.WriteLine();
        Console.WriteLine("  Copy the decimal VendorId / ProductId values into appsettings.json.");
        Console.WriteLine("  If the Switch is missing, install WinUSB for it with Zadig,");
        Console.WriteLine("  then run --scan again.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reflection failures are caught and handled gracefully without crashing.")]
    private static void TryPrintStrings(IUsbDevice dev)
    {
        // IUsbDevice in LibUsbDotNet 3.x does not expose Manufacturer / Product
        // as plain string properties.  We attempt to open and call GetStringDescriptor
        // if the method exists (it may not in all builds), otherwise skip gracefully.
        try
        {
            dev.Open();

            // Try reflection approach: some 3.x builds expose Info property
            // that carries descriptor strings — try a dynamic lookup so we don't
            // get a hard compile error if the member doesn't exist in this build.
            var infoProperty = dev.GetType().GetProperty("Info");
            if (infoProperty is not null)
            {
                var info = infoProperty.GetValue(dev);
                if (info is not null)
                {
                    var mfr = info.GetType().GetProperty("Manufacturer")?.GetValue(info) as string;
                    var prd = info.GetType().GetProperty("ProductString")?.GetValue(info) as string
                           ?? info.GetType().GetProperty("Product")?.GetValue(info) as string;

                    if (!string.IsNullOrWhiteSpace(mfr))
                        Console.WriteLine($"         Manufacturer : {mfr}");
                    if (!string.IsNullOrWhiteSpace(prd))
                        Console.WriteLine($"         Product      : {prd}");
                }
            }

            dev.Close();
        }
        catch
        {
            // Device needs a driver or access was denied — skip strings silently.
        }
    }
}
