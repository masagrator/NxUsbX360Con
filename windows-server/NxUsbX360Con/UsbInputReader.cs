namespace SwitchInputServer;

/// <summary>
/// Finds the Switch USB device, reads 18-byte <see cref="InputPacket"/> frames from
/// the bulk IN endpoint, and reconnects automatically on disconnect.
/// Fires <see cref="Connected"/>, <see cref="PacketReceived"/>, and
/// <see cref="Disconnected"/> events so callers can manage dependent resources
/// (e.g. ViGEmController) tied to the USB session.
/// </summary>
public sealed class UsbInputReader : IDisposable
{
    private readonly AppSettings   _cfg;
    private UsbContext?            _ctx;
    private IUsbDevice?            _dev;
    private UsbEndpointReader?     _reader;
    private bool                   _disposed;

    // ── Events ───────────────────────────────────────────────────────────
    /// <summary>Fired once the device is opened and the bulk endpoint is ready.</summary>
    public event EventHandler? Connected;

    /// <summary>Fired for each valid 18-byte packet received from the Switch.</summary>
    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;

    /// <summary>Fired when the USB connection is lost or the reader is stopped.</summary>
    public event EventHandler? Disconnected;

    public UsbInputReader(AppSettings cfg) => _cfg = cfg;

    // ── Public API ───────────────────────────────────────────────────────

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Connect();
                Connected?.Invoke(this, EventArgs.Empty);
                await ReadLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[USB] {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                bool wasConnected = _dev is not null;
                Disconnect();
                if (wasConnected)
                    Disconnected?.Invoke(this, EventArgs.Empty);
            }

            if (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"[USB] Reconnecting in {_cfg.ReconnectDelaySeconds:F1} s…");
                try { await Task.Delay(TimeSpan.FromSeconds(_cfg.ReconnectDelaySeconds), ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        Disconnect();
    }

    // ── USB lifecycle ─────────────────────────────────────────────────────

    private void Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _ctx = new UsbContext();
        _dev = null;

        // FIX #1: Collect the full device list first so we can dispose every
        // non-selected entry. IUsbDevice wraps a ref-counted libusb_device*;
        // abandoning those references without calling Dispose() leaks the
        // native handles. Over many reconnect cycles this exhausts the OS
        // USB device table and is the root cause of the long-running failures.
        var allDevices = _ctx.List().ToList();

        try
        {
            foreach (var candidate in allDevices)
            {
                if (candidate.VendorId != _cfg.VendorId || candidate.ProductId != _cfg.ProductId)
                    continue;

                try
                {
                    // Attempt to fully open and claim it.
                    // If this is a stale OS "ghost" handle it will throw here.
                    candidate.Open();
                    candidate.ClaimInterface(_cfg.InterfaceNumber);

                    // FIX #2: Assign _dev only AFTER OpenEndpointReader succeeds.
                    // Previously _dev was set before the call, so a failure left
                    // _dev pointing at a closed device while _reader stayed null.
                    // The subsequent ReadLoopAsync then hit a null-dereference on
                    // _reader!.Read(), which manifested as a spurious reconnect loop
                    // rather than a clear error.
                    var reader = candidate.OpenEndpointReader((ReadEndpointID)_cfg.ReadEndpointAddress);
                    _dev    = candidate;
                    _reader = reader;
                    break; // Success — leave this device out of the dispose loop below.
                }
                catch
                {
                    // Ghost device or access denied. Clean up and try the next match.
                    try { candidate.ReleaseInterface(_cfg.InterfaceNumber); } catch { }
                    try { candidate.Close();                                } catch { }
                    // candidate stays in allDevices and will be disposed in the
                    // finally block below together with all other non-selected entries.
                }
            }
        }
        finally
        {
            // FIX #1 (continued): Dispose every device that we did NOT select.
            // This releases the libusb_device* reference for each one.
            foreach (var candidate in allDevices)
            {
                if (!ReferenceEquals(candidate, _dev))
                    try { (candidate as IDisposable)?.Dispose(); } catch { }
            }
        }

        if (_dev is null)
            throw new InvalidOperationException(
                $"Switch not found (VID=0x{_cfg.VendorId:X4} PID=0x{_cfg.ProductId:X4}). " +
                "Is SwitchInputClient.nro running? Use --scan to list devices.");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(
            $"[USB] Connected  VID=0x{_cfg.VendorId:X4}  " +
            $"PID=0x{_cfg.ProductId:X4}  EP=0x{_cfg.ReadEndpointAddress:X2}");
        Console.ResetColor();
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        byte[] buf = new byte[InputPacket.Size];
        int consecutiveEmptyReads = 0;

        while (!ct.IsCancellationRequested)
        {
            int bytesRead;
            var err  = _reader!.Read(buf, _cfg.UsbReadTimeoutMs, out bytesRead);
            int code = (int)err;

            if (code == 0 && bytesRead == InputPacket.Size)
            {
                consecutiveEmptyReads = 0;

                var pkt = InputPacket.TryParse(buf);
                if (pkt.HasValue)
                    PacketReceived?.Invoke(this, new PacketReceivedEventArgs(pkt.Value));
            }
            else if (code == -7 || bytesRead == 0)
            {
                // Timeout / NAK — device is present but silent.
                // Abort if NAKing continuously for ~2 s at 7 ms poll rate.
                consecutiveEmptyReads++;
                if (consecutiveEmptyReads > 300)
                    throw new IOException("Device stopped responding (continuous timeouts).");
                await Task.Yield();
            }
            else if (code == 0 && bytesRead > 0)
            {
                // FIX #3: Short (partial) frame — code is success (0) but fewer
                // bytes arrived than the expected 18-byte packet size.
                // Previously this case fell through ALL branches silently:
                //   - the "valid packet" branch requires bytesRead == InputPacket.Size
                //   - the "timeout" branch requires code == -7 || bytesRead == 0
                // Neither matched, so the counter was never reset. A burst of
                // fragmented USB frames would accumulate until hitting the 300-read
                // threshold, triggering a forced disconnect — the "not accepting
                // certain data" symptom. Log and reset the idle counter so a
                // partial read doesn't penalise the timeout watchdog.
                consecutiveEmptyReads = 0;
                if (_cfg.Verbose)
                    Console.WriteLine($"[USB] Short frame: expected {InputPacket.Size} B, got {bytesRead} B — discarding.");
            }
            else if (code == -4)
            {
                throw new IOException("Switch disconnected (no device).");
            }
            else if (code < 0)
            {
                throw new IOException($"USB bulk read error: libusb code {code}.");
            }
        }
    }

    private void Disconnect()
    {
        // LibUsbDotNet 3.x: UsbEndpointReader does not implement IDisposable,
        // but try anyway so future library upgrades that add Dispose() work for free.
        try { (_reader as IDisposable)?.Dispose(); } catch { }
        _reader = null;

        if (_dev is not null)
        {
            try { _dev.ReleaseInterface(_cfg.InterfaceNumber); } catch { }
            try { _dev.Close();                                } catch { }
            _dev = null;
        }

        if (_ctx is not null)
        {
            _ctx.Dispose();
            _ctx = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }
}

public sealed class PacketReceivedEventArgs : EventArgs
{
    public InputPacket Packet { get; }
    public PacketReceivedEventArgs(InputPacket packet) => Packet = packet;
}
