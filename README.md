# NxUsbX360Con

Send Nintendo Switch controller input over USB to a Windows PC, emulating an Xbox 360 controller via the ViGEmBus driver.

This was made as an alternative to using Joycons on Windows via Bluetooth. Every native solution I tested resulted in big input lag on one of joycons. My solution has no visible input lag outside of joycon lag.
Technically it should support also pro controller and any other controller that Switch detects as Joycon/Pro Controller, but were not tested.

Jailbroken Nintendo Switch is required.

This tool was made mainly by Claude, I was fixing some additional bugs.

## How to use it:
- If you didn't install libusbK driver before:
  - Download [Zadig](https://zadig.akeo.ie/)
  - Connect your Nintendo Switch
  - Run NxUsbX360Con homebrew on Nintendo Switch, connect it to PC
  - Launch Zadig
  - From listed devices find the device "Nintendo Switch", with USB ID 057E 3000
  - Change driver from WinUSB to libusbK
  - Choose "Install driver" button
- If you didn't install ViGEmBus driver before:
  - Download latest driver [HERE](https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0) and install it
- After installing one of them you may need to restart homebrew and PC to get it to work properly
- Open homebrew, plug USB, run NxUsbX360Con.exe
- If you get in green color `[USB] Connected  VID=0x057E  PID=0x3000  EP=0x81`, it means it works.

## Additional informations
- Homebrew and server have code for reconnecting implemented, but it's finnicky, so you may need to restart homebrew/server to reeastibilish connection in case of closing server/homebrew and/or disconnecting USB while homebrew is running
- When connection is estabilished, Home menu button is disabled. To exit, press at once all D-Pad buttons + A + B + X + Y

## Additional functionalities
- If you want to use it in game that has Nintendo Pro Controller input layout support, you may change in "appsettings.json" `SwapABXY` to "true", then you will get input matching shown buttons
- Homebrew by default is sending packages in refresh rate matching display vsync. By pressing + for 3 seconds you can disable backlight and refresh rate of sending usb packages will increase to 66.66 Hz matching joycon input refresh rate
