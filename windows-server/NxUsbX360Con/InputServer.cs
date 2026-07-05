namespace SwitchInputServer;

/// <summary>
/// Top-level orchestrator.
///
/// ViGEm lifecycle is tied to the USB session:
///   USB connects   → virtual Xbox 360 controller is created
///   USB disconnects → controller is destroyed; waits for next connection
///
/// Console display:
///   A fixed-height status block is written once and refreshed in-place
///   (Console.SetCursorPosition).  An incrementing update counter makes
///   it obvious that the display has been refreshed.
/// </summary>
public sealed class InputServer : IDisposable
{
    private readonly AppSettings _cfg;

    // ── ViGEm (lives only while USB is connected) ─────────────────────────
    private ViGEmController? _vigem;

    // ── Console status block ──────────────────────────────────────────────
    private int  _statusRow = -1;
    private const int StatusH = 8;          // lines in the status block
    private const int StatusW = 52;         // target width for clearing old text

    private readonly object _conLock = new();

    // ── Stats ─────────────────────────────────────────────────────────────
    private long     _totalPackets;
    private long     _windowPackets;
    private DateTime _windowStart  = DateTime.UtcNow;
    private double   _currentRate;
    private long     _updateSeq;            // increments every display refresh

    private DateTime _lastDraw = DateTime.MinValue;
    private const double DrawHz = 30.0;     // cap display at 30 fps to avoid flicker

    // Last known packet for persistent display
    private InputPacket? _lastPkt;
    private string       _statusLabel = "WAITING";

    // FIX #4: Reuse a single StringBuilder instead of allocating one per
    // ForceRedraw() call. At up to 30 Hz this created significant GC pressure.
    private readonly System.Text.StringBuilder _btnBuf = new(64);

    public InputServer(AppSettings cfg) => _cfg = cfg;

    // ── Public API ────────────────────────────────────────────────────────

    public async Task RunAsync(CancellationToken ct)
    {
        using var usb = new UsbInputReader(_cfg);
        usb.Connected      += OnConnected;
        usb.PacketReceived += OnPacketReceived;
        usb.Disconnected   += OnDisconnected;

        ForceRedraw();

        try
        {
            await usb.RunAsync(ct);
        }
        finally
        {
            // Unsubscribe to release delegate references before usb is disposed.
            usb.Connected      -= OnConnected;
            usb.PacketReceived -= OnPacketReceived;
            usb.Disconnected   -= OnDisconnected;
        }
    }

    // ── USB event handlers ────────────────────────────────────────────────

    private void OnConnected(object? sender, EventArgs e)
    {
        try { _vigem = new ViGEmController(_cfg); }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ViGEm] Failed to create controller: {ex.Message}");
            Console.ResetColor();
        }

        _statusLabel = "CONNECTED";
        ForceRedraw();
    }

    private void OnPacketReceived(object? sender, PacketReceivedEventArgs e)
    {
        _vigem?.SendInput(e.Packet);

        Interlocked.Increment(ref _totalPackets);
        Interlocked.Increment(ref _windowPackets);

        // Update rolling rate once per second
        var now     = DateTime.UtcNow;
        var elapsed = (now - _windowStart).TotalSeconds;
        if (elapsed >= 1.0)
        {
            _currentRate  = Interlocked.Exchange(ref _windowPackets, 0) / elapsed;
            _windowStart  = now;
        }

        _statusLabel = "STREAMING";
        _lastPkt     = e.Packet;

        // Rate-limit screen redraws
        if ((now - _lastDraw).TotalSeconds >= 1.0 / DrawHz)
            ForceRedraw();
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        _vigem?.Dispose();
        _vigem = null;

        _statusLabel = "WAITING";
        _lastPkt     = null;
        _currentRate = 0;

        ForceRedraw();
    }

    // ── Display ───────────────────────────────────────────────────────────

    private void ForceRedraw()
    {
        _lastDraw = DateTime.UtcNow;
        long seq  = Interlocked.Increment(ref _updateSeq);
        long tot  = Interlocked.Read(ref _totalPackets);

        InputPacket? pkt   = _lastPkt;
        string       label = _statusLabel;

        lock (_conLock)
        {
            try
            {
                if (_statusRow < 0) return;

                int savedTop  = Console.CursorTop;
                int savedLeft = Console.CursorLeft;

                Console.SetCursorPosition(0, 15);

                // Colour scheme
                Console.ForegroundColor =
                    label == "STREAMING"    ? ConsoleColor.Green  :
                    label == "CONNECTED"    ? ConsoleColor.Green  :
                    label == "WAITING"      ? ConsoleColor.Yellow :
                    ConsoleColor.Gray;

                // ── Line 0: separator + update counter ────────────────────
                string seq6 = $"#{seq}";
                SLn($"── {seq6} " + new string('─', Math.Max(0, StatusW - 4 - seq6.Length - 1)));

                // ── Line 1: status ────────────────────────────────────────
                SLn($"  Status    {label,-14}" +
                    (label != "WAITING"
                        ? $"  VID=0x{_cfg.VendorId:X4}  PID=0x{_cfg.ProductId:X4}"
                        : "  (launch SwitchInputClient.nro)"));

                // ── Lines 2-6: input state ────────────────────────────────
                if (pkt.HasValue)
                {
                    SLn($"  Buttons   {BuildButtonStr(pkt.Value.Buttons)}");
                    SLn($"  L-Stick   X={pkt.Value.LeftX,7}  Y={pkt.Value.LeftY,7}");
                    SLn($"  R-Stick   X={pkt.Value.RightX,7}  Y={pkt.Value.RightY,7}");
                    SLn($"  Triggers  LT={pkt.Value.LeftTrig:000}  RT={pkt.Value.RightTrig:000}");
                }
                else
                {
                    SLn($"  Buttons   (none)");
                    SLn($"  L-Stick   X=      0  Y=      0");
                    SLn($"  R-Stick   X=      0  Y=      0");
                    SLn($"  Triggers  LT=000  RT=000");
                }

                // ── Line 7: stats ─────────────────────────────────────────
                SLn($"  Packets   {tot,10:N0} total  |  {_currentRate,6:F1} pkt/s");

                // ── Line 8: bottom border ─────────────────────────────────
                SLn(new string('─', StatusW));

                Console.ResetColor();

                // Park cursor below the block so normal Console.WriteLines go there
                int targetRow = _statusRow + StatusH;
                if (Console.CursorTop < targetRow)
                    Console.SetCursorPosition(0, targetRow);
            }
            catch
            {
                // Non-interactive terminal (redirected stdout, etc.) — ignore
            }
        }
    }

    private void PrintBanner()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════╗");
        Console.WriteLine("║       SwitchInputServer  v1.0.0                  ║");
        Console.WriteLine("║   Nintendo Switch  ──USB──►  Xbox 360 (ViGEm)   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine(
            $"  VID=0x{_cfg.VendorId:X4}  PID=0x{_cfg.ProductId:X4}  " +
            $"EP=0x{_cfg.ReadEndpointAddress:X2}  " +
            $"Poll={_cfg.UsbReadTimeoutMs} ms  " +
            $"SwapABXY={_cfg.SwapABXY}");
        Console.WriteLine();
        Console.WriteLine("  --scan to list USB devices   |   Ctrl+C to exit");
        Console.WriteLine();
    }

    /// <summary>Print line padded to <see cref="StatusW"/> to overwrite old content.</summary>
    private static void SLn(string text)
    {
        if (text.Length < StatusW)
            text = text.PadRight(StatusW);
        else if (text.Length > StatusW)
            text = text[..StatusW];
        Console.WriteLine(text);
    }

    private string BuildButtonStr(uint b)
    {
        // FIX #4: Reuse _btnBuf instead of allocating a new StringBuilder each call.
        _btnBuf.Clear();

        void Add(uint flag, string name)
        {
            if ((b & flag) != 0) { if (_btnBuf.Length > 0) _btnBuf.Append(' '); _btnBuf.Append(name); }
        }
        Add(SwitchButton.A,         "A");
        Add(SwitchButton.B,         "B");
        Add(SwitchButton.X,         "X");
        Add(SwitchButton.Y,         "Y");
        Add(SwitchButton.L,         "L");
        Add(SwitchButton.R,         "R");
        Add(SwitchButton.ZL,        "ZL");
        Add(SwitchButton.ZR,        "ZR");
        Add(SwitchButton.Plus,      "+");
        Add(SwitchButton.Minus,     "-");
        Add(SwitchButton.StickL,    "LS");
        Add(SwitchButton.StickR,    "RS");
        Add(SwitchButton.DPadUp,    "DU");
        Add(SwitchButton.DPadDown,  "DD");
        Add(SwitchButton.DPadLeft,  "DL");
        Add(SwitchButton.DPadRight, "DR");
        return _btnBuf.Length > 0 ? _btnBuf.ToString() : "(none)";
    }

    public void Dispose()
    {
        _vigem?.Dispose();
        _vigem = null;
    }
}