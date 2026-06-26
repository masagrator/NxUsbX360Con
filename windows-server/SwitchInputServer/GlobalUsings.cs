// Global usings — applied to every file in the project.
// Standard .NET usings are already covered by <ImplicitUsings>enable</ImplicitUsings>.

global using System.Buffers.Binary;
global using System.Text.Json;
global using System.Text.Json.Serialization;

// LibUsbDotNet 3.x namespaces
global using LibUsbDotNet;
global using LibUsbDotNet.LibUsb;   // UsbContext, IUsbDevice
global using LibUsbDotNet.Main;     // ReadEndpointID, UsbEndpointReader

// ViGEm.Client namespaces
// NOTE: Nefarius.ViGEm.Client.Targets.Xbox360 is intentionally NOT imported globally.
//       Xbox360Report and Xbox360FeedbackReceivedEventArgs were removed in 1.21.x.
//       We use the IXbox360Controller ref-property API + SubmitReport() instead.
global using Nefarius.ViGEm.Client;
global using Nefarius.ViGEm.Client.Targets;   // IXbox360Controller, IVirtualGamepad

// Project protocol types
global using SwitchInputServer.Protocol;
