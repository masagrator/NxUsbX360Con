namespace SwitchInputServer;

/// <summary>
/// Creates a virtual Xbox 360 controller via ViGEmBus and translates
/// <see cref="InputPacket"/> data into Xbox 360 HID reports.
///
/// Button swap mode (<see cref="AppSettings.SwapABXY"/> = true):
///   Maps Switch face buttons to matching *physical positions* on Xbox layout.
///   Switch A (right)  → Xbox B (right)
///   Switch B (bottom) → Xbox A (bottom)
///   Switch X (top)    → Xbox Y (top)
///   Switch Y (left)   → Xbox X (left)
///   For games that ship with Nintendo-layout button prompts.
/// </summary>
public sealed class ViGEmController : IDisposable
{
    // Xbox 360 wButtons bitmask (XINPUT_GAMEPAD spec)
    private const ushort XB_DPAD_UP        = 0x0001;
    private const ushort XB_DPAD_DOWN      = 0x0002;
    private const ushort XB_DPAD_LEFT      = 0x0004;
    private const ushort XB_DPAD_RIGHT     = 0x0008;
    private const ushort XB_START          = 0x0010;
    private const ushort XB_BACK           = 0x0020;
    private const ushort XB_LEFT_THUMB     = 0x0040;
    private const ushort XB_RIGHT_THUMB    = 0x0080;
    private const ushort XB_LEFT_SHOULDER  = 0x0100;
    private const ushort XB_RIGHT_SHOULDER = 0x0200;
    private const ushort XB_A              = 0x1000;
    private const ushort XB_B              = 0x2000;
    private const ushort XB_X              = 0x4000;
    private const ushort XB_Y              = 0x8000;

    private readonly AppSettings        _cfg;
    private readonly ViGEmClient        _client;
    private readonly IXbox360Controller _controller;
    private bool _disposed;

    public ViGEmController(AppSettings cfg)
    {
        _cfg        = cfg;
        _client     = new ViGEmClient();
        _controller = _client.CreateXbox360Controller();
        _controller.Connect();
        _controller.AutoSubmitReport = false;
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void SendInput(in InputPacket pkt)
    {
        short leftY  = _cfg.InvertLeftY  ? (short)-pkt.LeftY  : pkt.LeftY;
        short rightY = _cfg.InvertRightY ? (short)-pkt.RightY : pkt.RightY;

        _controller.ButtonState  = MapButtons(pkt.Buttons, _cfg.SwapABXY);
        _controller.LeftTrigger  = pkt.LeftTrig;
        _controller.RightTrigger = pkt.RightTrig;
        _controller.LeftThumbX   = pkt.LeftX;
        _controller.LeftThumbY   = leftY;
        _controller.RightThumbX  = pkt.RightX;
        _controller.RightThumbY  = rightY;

        _controller.SubmitReport();
    }

    // ── Button mapping ────────────────────────────────────────────────────

    private static ushort MapButtons(uint sw, bool swap)
    {
        ushort xb = 0;

        // ── Face buttons ─────────────────────────────────────
        if (!swap)
        {
            // Default: label-for-label  (Switch A → Xbox A, etc.)
            if ((sw & SwitchButton.A) != 0) xb |= XB_A;
            if ((sw & SwitchButton.B) != 0) xb |= XB_B;
            if ((sw & SwitchButton.X) != 0) xb |= XB_X;
            if ((sw & SwitchButton.Y) != 0) xb |= XB_Y;
        }
        else
        {
            // Nintendo positional: Switch A (right)  → Xbox B (right)
            //                      Switch B (bottom) → Xbox A (bottom)
            //                      Switch X (top)    → Xbox Y (top)
            //                      Switch Y (left)   → Xbox X (left)
            if ((sw & SwitchButton.A) != 0) xb |= XB_B;
            if ((sw & SwitchButton.B) != 0) xb |= XB_A;
            if ((sw & SwitchButton.X) != 0) xb |= XB_Y;
            if ((sw & SwitchButton.Y) != 0) xb |= XB_X;
        }

        // ── Non-face buttons (same in both modes) ─────────────
        if ((sw & SwitchButton.L)         != 0) xb |= XB_LEFT_SHOULDER;
        if ((sw & SwitchButton.R)         != 0) xb |= XB_RIGHT_SHOULDER;
        if ((sw & SwitchButton.Plus)      != 0) xb |= XB_START;
        if ((sw & SwitchButton.Minus)     != 0) xb |= XB_BACK;
        if ((sw & SwitchButton.StickL)    != 0) xb |= XB_LEFT_THUMB;
        if ((sw & SwitchButton.StickR)    != 0) xb |= XB_RIGHT_THUMB;
        if ((sw & SwitchButton.DPadUp)    != 0) xb |= XB_DPAD_UP;
        if ((sw & SwitchButton.DPadDown)  != 0) xb |= XB_DPAD_DOWN;
        if ((sw & SwitchButton.DPadLeft)  != 0) xb |= XB_DPAD_LEFT;
        if ((sw & SwitchButton.DPadRight) != 0) xb |= XB_DPAD_RIGHT;
        // ZL/ZR → trigger bytes, not buttons

        return xb;
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _controller.Disconnect(); } catch { }
        _client.Dispose();
    }
}