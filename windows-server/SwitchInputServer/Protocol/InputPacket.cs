namespace SwitchInputServer.Protocol;

/// <summary>
/// 18-byte binary frame sent from the Switch homebrew to the PC.
///
/// Layout (all values little-endian):
/// <code>
/// [0]     magic 0xAB
/// [1]     magic 0xCD
/// [2]     version (0x01)
/// [3]     reserved
/// [4-7]   buttons   – uint32, lower 16 bits are HidNpadButton flags
/// [8-9]   left_x    – int16  −32767…32767
/// [10-11] left_y    – int16
/// [12-13] right_x   – int16
/// [14-15] right_y   – int16
/// [16]    left_trig  – byte 0 or 255  (ZL digital)
/// [17]    right_trig – byte 0 or 255  (ZR digital)
/// </code>
/// </summary>
public readonly record struct InputPacket(
    uint    Buttons,
    short   LeftX,
    short   LeftY,
    short   RightX,
    short   RightY,
    byte    LeftTrig,
    byte    RightTrig)
{
    // ── Constants ─────────────────────────────────────────────────────────

    public const int  Size     = 18;
    public const byte Magic0   = 0xAB;
    public const byte Magic1   = 0xCD;
    public const byte Version  = 0x01;

    // ── Parser ────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse a 18-byte buffer into an <see cref="InputPacket"/>.
    /// Returns <see langword="null"/> if the magic bytes or length are wrong.
    /// </summary>
    public static InputPacket? TryParse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)      return null;
        if (data[0] != Magic0)       return null;
        if (data[1] != Magic1)       return null;
        // data[2] = version  — accept any for forward compatibility
        // data[3] = reserved

        return new InputPacket(
            Buttons:   BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]),
            LeftX:     BinaryPrimitives.ReadInt16LittleEndian(data[8..10]),
            LeftY:     BinaryPrimitives.ReadInt16LittleEndian(data[10..12]),
            RightX:    BinaryPrimitives.ReadInt16LittleEndian(data[12..14]),
            RightY:    BinaryPrimitives.ReadInt16LittleEndian(data[14..16]),
            LeftTrig:  data[16],
            RightTrig: data[17]
        );
    }

    // ── Debug helpers ─────────────────────────────────────────────────────

    public override string ToString() =>
        $"btn=0x{Buttons:X4} LX={LeftX,6} LY={LeftY,6} " +
        $"RX={RightX,6} RY={RightY,6} " +
        $"LT={LeftTrig,3} RT={RightTrig,3}";
}
