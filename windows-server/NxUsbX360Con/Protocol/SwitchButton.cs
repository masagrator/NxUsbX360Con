namespace SwitchInputServer.Protocol;

/// <summary>
/// Bitmask constants that mirror <c>HidNpadButton</c> from libnx.
/// The Switch homebrew packs these into the lower 16 bits of
/// <see cref="InputPacket.Buttons"/> (uint32 LE).
///
/// Source reference:
///   devkitPro/libnx  switch/services/hid.h — enum HidNpadButton
/// </summary>
public static class SwitchButton
{
    // ── Face buttons ──────────────────────────────────────────────────────
    public const uint A      = 1u << 0;   ///< A
    public const uint B      = 1u << 1;   ///< B
    public const uint X      = 1u << 2;   ///< X
    public const uint Y      = 1u << 3;   ///< Y

    // ── Stick clicks ──────────────────────────────────────────────────────
    public const uint StickL = 1u << 4;   ///< Left  stick pressed
    public const uint StickR = 1u << 5;   ///< Right stick pressed

    // ── Shoulder / trigger ────────────────────────────────────────────────
    public const uint L      = 1u << 6;   ///< L  (left  bumper)
    public const uint R      = 1u << 7;   ///< R  (right bumper)
    public const uint ZL     = 1u << 8;   ///< ZL (left  trigger — digital)
    public const uint ZR     = 1u << 9;   ///< ZR (right trigger — digital)

    // ── Special ───────────────────────────────────────────────────────────
    public const uint Plus   = 1u << 10;  ///< Plus  (≈ Start)
    public const uint Minus  = 1u << 11;  ///< Minus (≈ Back)

    // ── D-Pad ─────────────────────────────────────────────────────────────
    public const uint DPadLeft  = 1u << 12;  ///< D-Pad Left
    public const uint DPadUp    = 1u << 13;  ///< D-Pad Up
    public const uint DPadRight = 1u << 14;  ///< D-Pad Right
    public const uint DPadDown  = 1u << 15;  ///< D-Pad Down
}
