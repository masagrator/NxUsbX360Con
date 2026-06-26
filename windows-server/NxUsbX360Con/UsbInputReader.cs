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

        foreach (var candidate in _ctx.List())
        {
            if (candidate.VendorId == _cfg.VendorId && candidate.ProductId == _cfg.ProductId)
            {
                try
                {
                    // Attempt to fully open and claim it. 
                    // If this is a stale OS "ghost" handle, it will throw an exception here.
                    candidate.Open();
                    candidate.ClaimInterface(_cfg.InterfaceNumber);

                    _dev = candidate;
                    _reader = _dev.OpenEndpointReader((ReadEndpointID)_cfg.ReadEndpointAddress);
                    
                    break; // Success! We found the real, active device.
                }
                catch
                {
                    // It's a ghost device or access denied. Clean up and try the next match.
                    try { candidate.ReleaseInterface(_cfg.InterfaceNumber); } catch { }
                    try { candidate.Close(); } catch { }
                }
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
                consecutiveEmptyReads = 0; // Reset counter on valid packet
                
                var pkt = InputPacket.TryParse(buf);
                if (pkt.HasValue)
                    PacketReceived?.Invoke(this, new PacketReceivedEventArgs(pkt.Value));
            }
            else if (code == -7 || bytesRead == 0)
            {
                // Abort if the driver is NAKing continuously (approx 2 seconds at 7ms poll rate)
                consecutiveEmptyReads++;
                if (consecutiveEmptyReads > 300) 
                {
                    throw new IOException("Device stopped responding (continuous timeouts).");
                }
                await Task.Yield();
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
        _reader = null;   // UsbEndpointReader has no Dispose() in LibUsbDotNet 3.x

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