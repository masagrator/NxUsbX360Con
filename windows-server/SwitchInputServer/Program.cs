using SwitchInputServer;

// ── Banner ────────────────────────────────────────────────────────────────
Console.Title = "SwitchInputServer";
Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║       SwitchInputServer  v1.0.0                  ║");
Console.WriteLine("║   Nintendo Switch  ──USB──►  Xbox 360 (ViGEm)    ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

// ── Scan mode ─────────────────────────────────────────────────────────────
// Usage:  SwitchInputServer --scan
if (args.Contains("--scan", StringComparer.OrdinalIgnoreCase))
{
    UsbScanner.ScanDevices();
    return 0;
}

// ── Load settings ─────────────────────────────────────────────────────────
AppSettings config = AppSettings.Load();

Console.WriteLine($"  USB Device   : VID=0x{config.VendorId:X4}  PID=0x{config.ProductId:X4}");
Console.WriteLine($"  Endpoint IN  : 0x{config.ReadEndpointAddress:X2}  (interface {config.InterfaceNumber})");
Console.WriteLine($"  Read timeout : {config.UsbReadTimeoutMs} ms");
Console.WriteLine($"  Invert L-Y   : {config.InvertLeftY}");
Console.WriteLine($"  Invert R-Y   : {config.InvertRightY}");
Console.WriteLine($"  Verbose      : {config.Verbose}");
Console.WriteLine();
Console.WriteLine("  Run with --scan to list connected USB devices.");
Console.WriteLine("  Press Ctrl+C to exit cleanly.");
Console.WriteLine();

// ── Ctrl+C → graceful shutdown ────────────────────────────────────────────
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;   // prevent hard kill — let us clean up
    Console.WriteLine();
    Console.WriteLine("[Main] Ctrl+C — requesting shutdown…");
    cts.Cancel();
};

// ── Run ───────────────────────────────────────────────────────────────────
try
{
    var server = new InputServer(config);
    await server.RunAsync(cts.Token);
    Console.WriteLine("[Main] Server stopped cleanly.");
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("[Main] Cancelled.");
    return 0;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[Main] Fatal: {ex.Message}");
    Console.ResetColor();

    if (config.Verbose)
        Console.WriteLine(ex.ToString());

    return 1;
}
