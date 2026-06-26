namespace SwitchInputServer;

public sealed class AppSettings
{
    // ── USB device ────────────────────────────────────────────────────────
    [JsonPropertyName("VendorId")]
    public int VendorId { get; init; } = 0x057E;      // Nintendo

    [JsonPropertyName("ProductId")]
    public int ProductId { get; init; } = 0x3000;     // Switch homebrew gadget

    [JsonPropertyName("ReadEndpointAddress")]
    public int ReadEndpointAddress { get; init; } = 0x81;   // Bulk IN EP1

    [JsonPropertyName("InterfaceNumber")]
    public int InterfaceNumber { get; init; } = 0;

    /// <summary>
    /// libusb read timeout in milliseconds.
    /// Acts as the USB poll interval: 7 ms ≈ 0.0075 s ≈ 133 Hz.
    /// Shorter = lower latency + faster disconnect detection; longer = less CPU.
    /// </summary>
    [JsonPropertyName("UsbReadTimeoutMs")]
    public int UsbReadTimeoutMs { get; init; } = 7;

    [JsonPropertyName("ReconnectDelaySeconds")]
    public double ReconnectDelaySeconds { get; init; } = 2.0;

    // ── Controller ────────────────────────────────────────────────────────
    /// <summary>
    /// Swap A↔B and X↔Y so physical button positions match Nintendo layout.
    /// With this on: pressing Switch-A reports Xbox-B (right face button),
    /// Switch-B → Xbox-A (bottom), Switch-X → Xbox-Y (top), Switch-Y → Xbox-X (left).
    /// Use for games that expect Nintendo positional scheme on an Xbox report.
    /// </summary>
    [JsonPropertyName("SwapABXY")]
    public bool SwapABXY { get; init; } = false;

    [JsonPropertyName("InvertLeftY")]
    public bool InvertLeftY { get; init; } = false;

    [JsonPropertyName("InvertRightY")]
    public bool InvertRightY { get; init; } = false;

    // ── Diagnostics ───────────────────────────────────────────────────────
    [JsonPropertyName("Verbose")]
    public bool Verbose { get; init; } = false;

    // ── Factory ───────────────────────────────────────────────────────────
    public static AppSettings Load()
    {
        const string path = "appsettings.json";
        if (!File.Exists(path))
        {
            Console.WriteLine("[Config] appsettings.json not found — using defaults.");
            return new AppSettings();
        }
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), opts)
                   ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Parse error: {ex.Message} — using defaults.");
            return new AppSettings();
        }
    }
}