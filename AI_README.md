# NxUsbX360Con

Send Nintendo Switch controller input over USB to a Windows PC, emulating an Xbox 360 controller via the ViGEmBus driver.

```
Nintendo Switch (Homebrew NRO)  ──USB──►  Windows PC (C# Server)  ──►  Virtual Xbox 360 Controller
```

---

## Architecture

| Component | Technology |
|---|---|
| Switch side | C / libnx — compiled with devkitPro to `.nro` |
| Transport | USB Bulk Transfer (18-byte packets at ~120 Hz) |
| PC side | C# .NET 9 console app |
| USB host | LibUsbDotNet 3.x (WinUSB backend) |
| Controller emulation | Nefarius ViGEmBus + Nefarius.ViGEm.Client |

### Packet format (Switch → PC, 18 bytes)

| Offset | Size | Field |
|--------|------|-------|
| 0–1 | 2 | Magic `0xAB 0xCD` |
| 2 | 1 | Version `0x01` |
| 3 | 1 | Reserved |
| 4–7 | 4 | Buttons bitmask (uint32 LE) |
| 8–9 | 2 | Left stick X (int16, −32767…32767) |
| 10–11 | 2 | Left stick Y (int16) |
| 12–13 | 2 | Right stick X (int16) |
| 14–15 | 2 | Right stick Y (int16) |
| 16 | 1 | Left trigger (0 or 255, ZL) |
| 17 | 1 | Right trigger (0 or 255, ZR) |

### Button mapping

| Switch | Xbox 360 |
|--------|----------|
| A | A |
| B | B |
| X | X |
| Y | Y |
| L | LB |
| R | RB |
| ZL | LT (0 / 255) |
| ZR | RT (0 / 255) |
| + | Start |
| − | Back |
| L-Stick | LS |
| R-Stick | RS |
| D-Up/Down/Left/Right | D-Pad |

---

## Prerequisites

### Switch side
- Custom firmware (Atmosphère or similar) with homebrew access
- [devkitPro](https://devkitpro.org/wiki/Getting_Started) with the `switch-dev` group:
  ```
  dkp-pacman -S switch-dev
  ```
- USB cable (Switch → PC)

### PC side
- Windows 10 / 11 (64-bit)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or the .NET 9 Runtime (for running only)
- [ViGEmBus driver](https://github.com/ViGEm/ViGEmBus/releases/latest) — install **before** running the server
- [Zadig](https://zadig.akeo.ie/) — needed once to install the WinUSB filter driver for the Switch device

---

## Step 1 — Build the Switch homebrew

```bash
cd switch-client
make
```

This produces `SwitchInputClient.nro`. Copy it to `/switch/SwitchInputClient/SwitchInputClient.nro` on your microSD card (or wherever your launcher looks).

To clean: `make clean`

> **Icon**: The build omits an icon by default (`NO_ICON := 1`). To add one, place a 256×256 `icon.jpg` in `switch-client/` and remove the `NO_ICON` line from the Makefile.

---

## Step 2 — Install ViGEmBus

Download the latest `ViGEmBus_Setup_*.exe` from  
https://github.com/ViGEm/ViGEmBus/releases/latest  
and run it. Reboot if prompted.

---

## Step 3 — Find your USB VID / PID (one-time setup)

1. Launch the `SwitchInputClient.nro` on your Switch while it is connected via USB.
2. On the PC, run the scanner:
   ```
   cd windows-server\SwitchInputServer
   dotnet run -- --scan
   ```
3. Look for a Nintendo / unknown device that appeared after launching the homebrew. Note its VID and PID (shown in hex).
4. Convert the hex values to decimal and edit `appsettings.json`:
   ```json
   {
     "VendorId": 1317,
     "ProductId": 42151
   }
   ```
   (Replace with your actual values — the defaults 0x0525 / 0xA4A7 are common for libnx gadgets but may differ on your firmware.)

---

## Step 4 — Install WinUSB driver with Zadig (one-time per device)

1. Open **Zadig**.
2. Go to **Options → List All Devices**.
3. Select your Switch device from the dropdown.
4. In the driver box on the right, choose **WinUSB**.
5. Click **Replace Driver** (or **Install Driver**).

> This is safe and reversible. It only affects the Switch when used as a USB gadget with this homebrew.

---

## Step 5 — Run the server

```bash
cd windows-server\SwitchInputServer
dotnet run
```

Or build a self-contained executable:
```bash
dotnet publish -c Release -r win-x64 --self-contained true -o publish\
```
Then run `publish\SwitchInputServer.exe`.

You should see:
```
[USB] Connected to Switch
[ViGEm] Xbox 360 controller connected (index 0)
[Server] Streaming input…
```

Open any game or tool that reads XInput / Xbox controllers — the Switch pad now acts as controller #1.

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `Switch not found` | Ensure homebrew is running, cable is plugged in, and VID/PID in `appsettings.json` match `--scan` output |
| `Access denied` on USB | Run as Administrator, or reinstall WinUSB via Zadig |
| `ViGEmBus not found` | Install ViGEmBus and reboot |
| `usbCommsInitialize() failed` on Switch | Ensure no other app is using the USB gadget service |
| Buttons work but sticks are inverted | Toggle `InvertLeftY` / `InvertRightY` in `appsettings.json` |
| Reconnection loop | Try a different USB cable or port (USB 3.x port recommended) |

---

## Project layout

```
NxUsbX360Con/
├── README.md
├── switch-client/
│   ├── Makefile
│   └── source/
│       └── main.c          ← homebrew entry point
└── windows-server/
    ├── SwitchInputServer.sln
    └── SwitchInputServer/
        ├── SwitchInputServer.csproj
        ├── GlobalUsings.cs
        ├── Program.cs          ← entry point + config
        ├── Settings.cs         ← AppSettings record
        ├── InputServer.cs      ← orchestrator
        ├── UsbInputReader.cs   ← USB bulk-transfer reader
        ├── ViGEmController.cs  ← Xbox 360 emulation
        ├── UsbScanner.cs       ← device discovery utility
        ├── appsettings.json    ← user config
        └── Protocol/
            ├── InputPacket.cs  ← 18-byte packet struct + parser
            └── SwitchButton.cs ← Switch HID button bit flags
```

---
