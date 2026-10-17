# SyncCast (SyncB) — Multi-Device Audio Output Router

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C# 12](https://img.shields.io/badge/C%23-12.0-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![WPF](https://img.shields.io/badge/UI-WPF%20%7C%20Windows%2011%20Fluent-0078D4?logo=windows11&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![Audio API](https://img.shields.io/badge/Audio-WASAPI%20Shared%20Mode-FF6F00?logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/windows/win32/coreaudio/wasapi)
[![NAudio](https://img.shields.io/badge/Audio%20Engine-NAudio%20v2.2.1-008080)](https://github.com/naudio/NAudio)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D7?logo=windows)](https://microsoft.com)
[![Drivers](https://img.shields.io/badge/Kernel%20Drivers-Zero%20(100%25%20User%20Mode)-2ea44f)](#how-it-works-at-a-glance)
[![License](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](LICENSE)

A personal software-only Windows desktop application built with **.NET 8** and **WPF** designed to route audio output to up to **five (5)** independently available audio endpoints concurrently using native Windows audio APIs.

---

## How It Works (At a Glance)

SyncCast operates entirely in user mode using native Windows Core Audio APIs (**WASAPI**). It requires **no third-party virtual audio cables**, **no kernel drivers**, and **no elevated administrator permissions**.

```
                           +------------------------------------+
                           |  Any Windows Application / Player   |
                           |  (Spotify, YouTube, VLC, Games...) |
                           +-----------------+------------------+
                                             |
                                             v (Native Audio Playback)
                           +------------------------------------+
                           |   Windows Default Audio Endpoint   |
                           |   (e.g., Laptop Speakers / AUX)    |
                           +-----------------+------------------+
                                             |
                                             v (WASAPI Loopback Capture)
                                    [SyncCast Engine]
                                             |
        +------------------------------------+------------------------------------+
        |                                    |                                    |
        v                                    v                                    v
+---------------+                    +---------------+                    +---------------+
| Native Source |                    | Destination 1 |                    | Destination 2 |
| (Zero Echo)   |                    | (Ring Buffer) |                    | (Ring Buffer) |
+-------+-------+                    +-------+-------+                    +-------+-------+
        |                                    |                                    |
(Already Playing)                    (Delay Sample Provider)              (Delay Sample Provider)
(No duplicate stream)                        |                                    |
                                     (Resampler / Formatter)              (Resampler / Formatter)
                                             |                                    |
                                             v                                    v
                                    [WasapiOut Shared]                   [WasapiOut Shared]
                                    Bluetooth Headphones                   USB / 3.5mm Headset
```

### 1. Loopback Capture Stage
* SyncCast attaches to the active Windows default render endpoint using **WASAPI Loopback Capture** (`WasapiLoopbackCapture`).
* It captures live PCM audio frames rendered by any desktop app, browser, or game directly from the Windows audio mixer before physical output.

### 2. Zero-Echo Feedback Prevention (Native Source Bypass)
* If your capture source is also toggled as one of your selected outputs, SyncCast **does not** create a duplicate audio playback stream to that device.
* Instead, it marks that device as **`Native Source`** (letting Windows play it natively at zero added latency) and routes live audio frames **only to the other selected destinations**. This prevents echo, duplicate audio, and recursive loopback.

### 3. Fan-Out & Format Adaptation
* Each additional destination endpoint maintains an isolated 250ms bounded ring buffer (`BufferedWaveProvider`).
* Because each audio device operates at its own hardware sample rate and bit depth (e.g., 44.1 kHz 16-bit vs. 48 kHz 32-bit float), SyncCast passes captured audio through a real-time `AudioFormatAdapter` with high-quality resampling (`WdlResamplingSampleProvider`) and channel mapping.

### 4. Software Sync Offset (0–1000 ms)
* Bluetooth headphones introduce inherent physical wireless latency (~100–250ms) compared to wired speakers.
* SyncCast provides an independent delay line (`DelaySampleProvider`) with fractional smoothing for each destination, allowing you to delay earlier devices so all audio reaches your ears in acoustic synchronization.

---

## Quick Start: How to Use SyncCast

1. **Launch SyncCast**:
   Run `SyncCast.exe` (or `dotnet run --project SyncCast -c Release`).
2. **Select Your Output Devices**:
   - Go to the **Audio Devices** tab (or **Overview** tab).
   - Click the toggle switch next to up to 5 audio endpoints you want to hear audio from.
   - The device with the cyan badge is your **Capture Source** (where Windows normally sends audio).
3. **Start Sharing**:
   - Click the **Start Sharing** button in the header (or on the Audio Devices page).
   - Audio playing in Windows (YouTube, Spotify, etc.) will immediately play through all selected devices simultaneously.
   - Adjust individual volume sliders or sync delay as needed.

---

## Bluetooth Hardware Notice: Connecting Two Bluetooth Headphones

> [!IMPORTANT]
> **Windows Classic Bluetooth (A2DP) Single-Radio Limitation**
>
> A standard PC built-in Bluetooth adapter (Intel, Realtek, Qualcomm) supports connecting multiple Bluetooth devices for mouse, keyboard, and phone sync, but **only supports ONE active stereo audio playback stream (A2DP) per physical Bluetooth radio controller**.
>
> If you pair and connect two pairs of classic Bluetooth headphones (e.g., two pairs of AirPods) to the same built-in PC Bluetooth card, Windows will show both as "paired", but as soon as the second one starts playing, the Bluetooth adapter will automatically disconnect or silence the first one (`CM_PROB_PHANTOM` / device unplugged). This is a physical Bluetooth controller hardware limit, not a software bug.

### Supported Multi-Output Configurations:
| Configuration | Supported? | Hardware Needed | Practical Setup |
| :--- | :---: | :--- | :--- |
| **Option A: 1 Bluetooth + 1 Wired (3.5mm AUX or USB)** | **YES** | 0 extra hardware | Connect 1 Bluetooth pair + plug 1 wired headphone/speaker into your PC jack. |
| **Option B: 1 Bluetooth + Built-in Laptop / Monitor Speakers** | **YES** | 0 extra hardware | Listen on your Bluetooth headphones while sharing the room audio through your laptop speakers. |
| **Option C: 2 Bluetooth Headphones (Dual Dongle)** | **YES** | 1 cheap USB Bluetooth Dongle (~$5–$10) | Pair Headphone 1 to built-in Bluetooth, Pair Headphone 2 to the USB dongle. |
| **Option D: 2 USB / Wired Headphones** | **YES** | 0 extra hardware (or USB-C to 3.5mm adapter) | Plug one into 3.5mm jack and one into a USB/USB-C headphone adapter. |
| **Option E: 2 Bluetooth Headphones via 1 Built-in Adapter** | **NO** | Single PC Bluetooth Radio | PC hardware disconnects one device when the other plays (A2DP hardware limit). |

---

## How to Stream Simultaneously (Step-by-Step Setup Recipes)

Depending on your hardware, here is exactly what to do to get simultaneous multi-headphone or multi-speaker audio working:

### Recipe 1: 1 Bluetooth Headset + 1 Wired Headset (Zero Extra Cost)
*Best for sharing audio with a friend right now without buying anything.*

1. **Connect your devices**:
   - Pair and connect your Bluetooth headphones (e.g. AirPods, Sony, Bose) to Windows.
   - Plug your second pair of headphones into your computer's 3.5mm headphone jack or USB port.
2. **Open SyncCast**:
   - Go to the **Audio Devices** tab.
   - Click the toggle switch on for your **Bluetooth Headphones**.
   - Click the toggle switch on for your **Realtek / High Definition Audio (Speakers/Headphones)**.
3. **Start Playing**:
   - Click **Start Sharing** at the top.
   - Start any media (YouTube, Spotify, movie player). Audio plays in both pairs simultaneously!
4. *(Optional) Align Acoustic Timing*:
   - If the wired headphones sound a split second ahead of the Bluetooth headphones (due to Bluetooth wireless latency), go to the **Sync Calibration** tab and increase the delay on the wired headphones by ~100–180ms until the sound is perfectly in sync.

---

### Recipe 2: Two Bluetooth Headphones (AirPods, etc.) Simultaneously
*Standard Windows laptops and PCs have only ONE Bluetooth radio chip. To stream to two separate Bluetooth headphones simultaneously without either cutting out, you must provide a second Bluetooth radio.*

#### What you need:
* A mini **USB Bluetooth Dongle** (Bluetooth 5.0, 5.1, 5.3, or 5.4 — typically costs $5 to $10 on Amazon / Best Buy / electronics stores, from brands like TP-Link, ASUS, Baseus, or UGREEN).

#### Step-by-Step Setup:
1. **Plug in the USB Bluetooth Dongle** to an available USB port on your PC.
   - Windows 10/11 will automatically install the driver. You now have **two independent Bluetooth controllers** on your machine.
2. **Pair Headphone 1**:
   - Pair your first pair of headphones (e.g. AirPods #1) to the primary Bluetooth controller.
3. **Pair Headphone 2**:
   - Pair your second pair of headphones (e.g. AirPods #2) to the second Bluetooth controller.
   - Both devices will now remain in an active, streaming `Connected Audio` state without knocking each other offline.
4. **Select & Stream in SyncCast**:
   - Open SyncCast and go to **Audio Devices**.
   - Toggle both Bluetooth headphones ON.
   - Click **Start Sharing**. Both listeners will hear full stereo audio simultaneously!

---

### Recipe 3: Dual Wired Headphones (3.5mm Jack + USB Headphone Adapter)
*Best for long flights, train rides, or zero-latency co-listening.*

1. **Connect your devices**:
   - Plug Headphone 1 into your laptop's standard 3.5mm audio jack.
   - Plug Headphone 2 into any USB port using a cheap USB-to-3.5mm audio adapter or USB-C DAC dongle (e.g. Apple USB-C to 3.5mm headphone jack adapter).
2. **In SyncCast**:
   - Both appear as distinct audio endpoints in Windows.
   - Toggle both devices ON in SyncCast.
   - Click **Start Sharing**. You have instant, zero-latency dual listening with independent volume controls for each person!

---

### Recipe 4: Headphone + Room Speakers (Silent Listening + Room Audio)
*Best for presentations, streaming, gaming, or watching a movie where one person wants headphones at high volume and another listens through room speakers.*

1. **Select Devices**:
   - Set Windows default output to your **Laptop / Desktop Speakers**.
   - In SyncCast, toggle your **Speakers** and your **Bluetooth / USB Headphones** ON.
2. **Click Start Sharing**:
   - Room speakers play cleanly at the volume you choose.
   - Headphone listener can turn their per-device volume slider up or down independently in SyncCast without affecting the room speakers.

## Core Features & Architecture

SyncCast is built on a modular user-mode architecture using native Windows Core Audio APIs (**WASAPI Shared Mode**), requiring zero kernel drivers or third-party virtual audio cables:

### 1. Endpoint Discovery & Health Management
* **Native Core Audio Discovery**: Automatically enumerates physical and virtual audio endpoints via `MMDeviceEnumerator` (DataFlow `Render`), detecting friendly names, hardware sample rates, and bit depths.
* **Transport Classification**: Heuristically classifies endpoints (`Bluetooth`, `Built-in / Wired`, or `Unknown Transport`) based on PnP hardware IDs and driver descriptors.
* **Hot-Plug & Disconnection Recovery**: Automatically detects device additions, removals, and state changes via `IMMNotificationClient`. If a device disconnects, it safely isolates the stream and reconnects with exponential backoff (1s, 2s, 4s, 8s, 15s) once re-plugged.

### 2. Multi-Output Audio Pipeline
* **Real-Time Loopback Capture**: Captures live PCM/IEEE Float audio frames rendered by any desktop app, browser, or game using `WasapiLoopbackCapture`.
* **Zero-Echo Protection**: If the loopback capture source is also toggled as an output, SyncCast marks it as `Native Source` (playing natively via Windows) and routes frames only to secondary destinations, preventing duplicate playback and feedback loops.
* **Format Adaptation & Resampling**: Each output stream inspects the endpoint's native `MixFormat` and automatically applies high-quality resampling (`WdlResamplingSampleProvider`), channel mapping (mono/stereo/surround), and bit-depth conversion.
* **Bounded Ring Buffering**: Each destination maintains an isolated 250ms circular ring buffer (`BufferedWaveProvider`) with overflow protection to discard stale frames and prevent unbounded latency growth.

### 3. Volume Control & Acoustic Calibration
* **Master & Per-Device Volume**: Independent volume sliders (0–100%) and mute toggles for each output, backed by smooth gain slewing (`SmoothGainSampleProvider`) over 10–20ms to eliminate clicks and pops.
* **Acoustic Delay Engine**: Integrated circular delay lines (0–1000ms range) per destination with fractional interpolation allow fine-tuning output timing to acoustically align fast wired devices with wireless Bluetooth buffers.
* **Guided Sync Wizard**: Built-in acoustic alignment pulse generator emitting gentle 880 Hz Hann-windowed musical metronome pings (-20 dBFS) to assist manual ear-calibration.
* **Continuous Reference Tone**: Independent test-tone mode (440 Hz, 528 Hz, 1000 Hz sine waves) for hardware diagnostics and channel verification.

### 4. Modern Windows 11 Fluent Interface
* **Fluent App Shell**: Built with WPF and native Desktop Window Manager (DWM) integration, supporting Windows 11 Mica material backdrop, rounded corners, and dark/light theme synchronization.
* **Real-Time Telemetry Dashboard**: Throttled 250ms diagnostic monitoring displaying frames captured, software buffer delay, drift deltas, buffer occupancy, and one-click diagnostics export to clipboard.
* **Per-User Configuration**: User preferences, volume levels, delay calibrations, and themes automatically persist in `%APPDATA%\SyncCast\settings.json`.

## Real Hardware Concurrency Test Results (On Current Machine)

During automated hardware probing (`RealHardwareAudioConcurrencyTests`), the engine probed this physical host's audio hardware:

```
[Hardware Probe] Discovered 3 active audio render endpoint(s) on this machine:
  -> Endpoint: 'Speaker (Realtek(R) Audio)'     | Transport: BuiltInOrWired | Format: 48,000 Hz • Stereo (2 ch), 32-bit
  -> Endpoint: 'Speakers (OMEN Cam & Voice)'    | Transport: Unknown        | Format: 48,000 Hz • Stereo (2 ch), 32-bit
  -> Endpoint: 'Speakers (Wonder Speaker)'      | Transport: Unknown        | Format: 48,000 Hz • Stereo (2 ch), 32-bit

[Hardware Concurrency Result] Attempted: 3 | Successfully Opened: 3 | Failed: 0
  - Stream 'Speaker (Realtek(R) Audio)' (BuiltInOrWired): State=Playing, TargetFormat=[48,000 Hz • 2 Ch • 32-bit (Extensible)], Error=[None]
  - Stream 'Speakers (OMEN Cam & Voice)' (Unknown):       State=Playing, TargetFormat=[48,000 Hz • 2 Ch • 32-bit (Extensible)], Error=[None]
  - Stream 'Speakers (Wonder Speaker)' (Unknown):         State=Playing, TargetFormat=[48,000 Hz • 2 Ch • 32-bit (Extensible)], Error=[None]

[Hardware Probe] Concurrency verified for 3 stream(s) running simultaneously for 1000 ms.
[Hardware Probe] Real hardware multi-output test completed and streams released cleanly.
```

**Outcome**: All three physical/virtual render endpoints simultaneously initialized WASAPI shared mode streams, rendered audio in parallel without buffer underruns, and tore down cleanly upon stop.

---

## Step-by-Step Manual Testing Guide

Use this manual test guide to verify Chunk 3 System Audio Sharing on your machine:

### Test Scenario A: System Audio Sharing with Multiple Endpoints
1. **Prepare Output Devices**:
   - Pair 1 or more Bluetooth headphones/speakers to Windows, or connect USB/3.5mm external speakers.
   - Verify all devices appear in Windows Settings > Sound as active playback devices.
2. **Launch SyncCast**:
   - Run `dotnet run --project SyncCast` from PowerShell.
   - Click **Refresh Devices** and confirm all connected endpoints appear.
3. **Configure Capture Source**:
   - In **Section 2: Multi-Output Audio Engine**, locate **Capture Source (WASAPI Loopback)**.
   - Select your primary listening device (e.g., `Speaker (Realtek(R) Audio)`).
4. **Select Destination Outputs**:
   - Click **+ Select Output** on up to 5 endpoints.
   - Notice the dynamic advisory banner:
     - If the capture source is selected: notice the advisory stating that the source will play natively via Windows to prevent feedback, while audio is mirrored to other selected outputs.
     - If the capture source is not selected: notice the warning that Windows will continue rendering audio to the source endpoint independently.
5. **Start System Audio Sharing**:
   - Start playing any audio on your PC (e.g. YouTube, Spotify, or a media player).
   - Click **Start Sharing System Audio**.
   - Observe the indicators:
     - **Capture Status**: Transitions to **RUNNING** with captured byte counter incrementing.
     - **Source Device Card**: Displays **Native Source (Windows)** in cyan.
     - **Mirrored Device Cards**: Display **Playing** in emerald green with target format and latency.
   - Verify that audio is audible simultaneously across all connected destination devices.
6. **Stop Sharing**:
   - Click **Stop Sharing**.
   - Confirm capture halts cleanly, byte counter stops, and destination streams return to idle.

### Test Scenario B: Dynamic Disconnect Resilience
1. Start System Audio Sharing with at least one Bluetooth device selected.
2. Turn off the Bluetooth device or put it in its charging case during active audio sharing.
3. Observe SyncCast:
   - The disconnected device card transitions to **Failed** showing the WASAPI error message.
   - All other destination devices **continue streaming audio uninterrupted**.
   - The application does not freeze or crash.

### Test Scenario C: Test Tone vs. System Audio Mutual Exclusion
1. Start **System Audio Sharing**.
2. Expand the **Mode 2: Test Tone Generator** expander.
3. Click **Start Audio Test**:
   - SyncCast automatically halts the System Audio capture session and transitions cleanly to Test Tone playback.
4. Click **Start Sharing System Audio**:
   - SyncCast automatically halts the Test Tone session and resumes System Audio loopback capture.

---

## Architecture & Code Structure

```
SyncB/
├── SyncB.sln
├── README.md
├── SyncCast/
│   ├── App.xaml                  # DI setup & application startup
│   ├── App.xaml.cs               # Service container registration & clean shutdown
│   ├── MainWindow.xaml           # Polished dark UI with dual audio mode controls
│   ├── MainWindow.xaml.cs
│   ├── Models/
│   │   ├── AudioEndpointInfo.cs      # Core Audio MMDevice model
│   │   ├── DeviceTransportType.cs    # Bluetooth, BuiltInOrWired, Unknown
│   │   ├── EndpointActiveState.cs    # Active, Disabled, Unplugged, NotPresent
│   │   ├── DeviceStatus.cs           # Available, Selected, Unavailable, PlaybackNotTested
│   │   ├── DiagnosticInfo.cs         # Host runtime & hardware stats
│   │   ├── OutputStreamStatus.cs     # Per-stream lifecycle state & metrics
│   │   ├── SystemAudioModels.cs      # CaptureState, AudioEngineMode, SystemAudioCaptureStatus
│   │   └── LogMessage.cs             # Real-time diagnostic event log
│   ├── Services/
│   │   ├── Audio/
│   │   │   ├── IAudioEndpointDiscoveryService.cs
│   │   │   ├── AudioEndpointDiscoveryService.cs  # MMDeviceEnumerator query engine
│   │   │   ├── Capture/
│   │   │   │   ├── ISystemAudioCaptureService.cs # WASAPI loopback capture interface
│   │   │   │   └── SystemAudioCaptureService.cs  # Background thread WASAPI loopback engine
│   │   │   └── Engine/
│   │   │       ├── ISampleSource.cs              # Signal source interface
│   │   │       ├── ToneGeneratorSampleSource.cs  # Continuous test-tone generator
│   │   │       ├── CalibrationPulseSampleSource.cs # Chunk 5: Gentle Hann-windowed musical alignment pulse
│   │   │       ├── AudioFormatAdapter.cs         # Resampling & format converter
│   │   │       ├── DelaySampleProvider.cs        # Chunk 5: Bounded circular delay buffer with fractional slewing
│   │   │       ├── SmoothGainSampleProvider.cs   # Click-free gain ramping & anti-clipping
│   │   │       ├── SharedAudioTimeline.cs        # Chunk 5: Monotonic sequence tracking & drift monitoring
│   │   │       ├── IEndpointAudioStream.cs       # Independent stream lifecycle & live buffer
│   │   │       ├── EndpointAudioStream.cs        # WASAPI Shared Mode stream & bounded buffer
│   │   │       ├── IMultiOutputAudioEngine.cs    # Session fan-out manager
│   │   │       └── MultiOutputAudioEngine.cs     # Resilient multi-mode orchestrator
│   │   │   └── Notifications/
│   │   │       ├── IAudioEndpointNotificationService.cs
│   │   │       └── AudioEndpointNotificationService.cs # IMMNotificationClient hardware event listener
│   │   ├── Devices/
│   │   │   ├── IDeviceSelectionManager.cs
│   │   │   └── DeviceSelectionManager.cs         # 5-device limit enforcement
│   │   ├── Diagnostics/
│   │   │   ├── IDiagnosticsService.cs
│   │   │   └── DiagnosticsService.cs             # System & concurrency diagnostics
│   │   ├── Logging/
│   │   │   ├── ILogService.cs
│   │   │   └── LogService.cs                     # Thread-safe UI logging
│   │   └── Settings/
│   │       ├── SyncCastSettings.cs               # Settings data model (volumes, delays, reference device)
│   │       ├── ISettingsService.cs               # Settings service contract
│   │       └── SettingsService.cs                # JSON persistence (%APPDATA%\SyncCast)
│   │   └── UI/
│   │       ├── WindowBackdropHelper.cs           # Windows 11 DWM Mica, corner & title bar interop
│   │       └── ThemeService.cs                   # Fluent Dark, Light & Follow Windows theme manager
│   ├── ViewModels/
│   │   ├── AudioEndpointViewModel.cs # Observable endpoint representation
│   │   ├── OutputStreamViewModel.cs  # Observable per-stream status, volume, delay & health metrics
│   │   └── MainViewModel.cs          # Root ViewModel with navigation, dual-mode controls, calibration wizard & diagnostics
│   ├── Converters/
│   │   └── Converters.cs             # XAML brushes, status, stream state, capture, equality & icon converters
│   └── Resources/
│       └── Styles.xaml               # Windows 11 Fluent Design tokens, vector icons & controls
└── SyncCast.Tests/
    ├── DeviceSelectionManagerTests.cs        # Limit (5), rejection, pruning tests
    ├── BluetoothDetectionTests.cs            # Heuristics & honest 'Unknown' classification
    ├── AudioEndpointViewModelTests.cs        # Status & format formatting tests
    ├── MainViewModelTests.cs                 # Engine controls, limit banner, error resilience
    ├── AudioEngineTests.cs                   # Tone generator, format adapter, failure isolation
    ├── SystemAudioCaptureTests.cs            # Loopback capture, feedback prevention, bounded buffer
    ├── VolumeAndHealthMonitoringTests.cs     # Gain math, smooth ramp, settings persistence, reconnect
    ├── FluentUiDesignTests.cs                # Windows 11 navigation, theme switching & DWM tests
    ├── AudioSynchronizationTests.cs         # Chunk 5: Shared timeline, delay slewing, pulse source, settings
    ├── RealWindowsAudioDiscoveryTests.cs     # Native MMDevice enumeration test
    └── RealHardwareAudioConcurrencyTests.cs  # Real host hardware concurrency test
```

---

## Build & Test Commands

```powershell
# Build solution
dotnet build

# Run complete automated test suite (73 tests passing)
dotnet test

# Run real hardware concurrency test with detailed logs
dotnet test --filter "RealHardware_TestEndpointConcurrency_OnCurrentMachine" --logger "console;verbosity=detailed"

# Run WPF desktop application
dotnet run --project SyncCast
```

---

## Manual Testing Guides

### Test Scenario C: Master Volume, Individual Volume & Mute Controls
1. Start playback in either **System Audio** or **Test Tone** mode across 2 or more devices.
2. Adjust the **Master Volume** slider in Section 2:
   - Verify all audio outputs change volume simultaneously and smoothly without audible clicks or pops.
   - Verify the capture session continues uninterrupted (no restart of loopback capture).
3. Click **Mute All** on the Master Volume control:
   - Verify all output streams go silent.
   - Click **Unmute All**; verify sound returns at the previously set level.
4. Adjust an individual device's **Volume Slider** on its card:
   - Verify only that device's sound changes volume. Other devices remain at their previous levels.
5. Click **Mute** on a single device card:
   - Verify only that device is silenced.
   - Verify all other selected devices continue playing unaffected.
   - Click **Unmute**; verify only that device returns to its set volume.

### Test Scenario D: Device Disconnection & Automatic Reconnection
1. Start sharing audio across multiple endpoints (including at least one USB or Bluetooth device).
2. Unplug the USB audio adapter or turn off/disconnect the Bluetooth device:
   - Verify the disconnected device's card transitions to **Disconnected** (amber indicator).
   - Verify remaining active devices **continue playing without interruption**.
   - Verify the event log records the disconnection event.
3. If **Auto-Reconnect** is enabled:
   - Re-connect/turn on the device within 15 seconds.
   - Verify SyncCast detects the device reappearing and automatically attempts reconnection using backoff (1s, 2s, 4s, 8s, 15s).
   - Once reconnected, verify its card transitions to **Playing** (green) and audio resumes on that device.
4. If Auto-Reconnect is disabled or retries exhaust:
   - Click the **Retry** button on the device card to manually re-establish the stream.

### Test Scenario E: Windows 11 Fluent Navigation & Theme Switching
1. Launch SyncCast (`dotnet run --project SyncCast`).
2. Verify the window displays native Windows 11 rounded corners and a dark title bar matching the app theme.
3. Click through the left navigation rail:
   - **Overview**: Real-time status header, primary Start/Stop Sharing action button, feedback-prevention advisory, capture source selector with telemetry, global master volume slider & mute.
   - **Audio Devices**: Dual-panel list of discovered Windows endpoints and configured output streams with independent volume sliders.
   - **Diagnostics**: Pipeline health metric cards and live diagnostic event log.
   - **Audio Settings**: Grouped settings cards for Appearance, Device Recovery, and Storage.
   - **About**: App information, runtime specs, and Bluetooth constraints.
4. In **Audio Settings**, click **Light**:
   - Verify the interface immediately transitions to the Fluent Light theme with light background surfaces, dark text, and synchronized Windows title bar.
5. Click **Dark**:
   - Verify the interface smoothly returns to the Fluent Dark theme.
6. Click **Follow Windows**:
   - Verify SyncCast adopts your current Windows Personalization theme mode (Light or Dark).

---

## Synchronization Guide & Manual Testing Checklist

### How Synchronization Works in SyncCast
1. **Shared Audio Timeline**: All audio blocks distributed to active outputs originate from a common timeline epoch tracked by a high-resolution monotonic stopwatch clock (`SharedAudioTimeline`). Every audio block receives a monotonically advancing sequence number (`LastBlockSequence`) and source sample-frame position.
2. **Per-Device Delay Buffer**: Prior to digital gain application and hardware format conversion, each audio stream routes through a bounded circular delay buffer (`DelaySampleProvider`). This buffer stores up to 1000 milliseconds of float audio samples.
3. **Click-Free Slew Interpolation**: When the user adjusts a delay slider or nudges delay buttons during active playback, the buffer does not abruptly jump or drop samples. Instead, it adjusts fractional read pointers gradually (`0.2%` slew per frame), shifting delay seamlessly without clicks or pops.
4. **Timing Reference Model**: The user can designate one device as the **Timing Reference**. In Bluetooth setups, the **slowest** physical device (the device with the greatest hardware DSP or radio latency) should be chosen as the reference, and positive software delay added to the faster devices.

### What Software Delay CAN and CANNOT Correct
* **CAN**: Delay faster outputs (such as low-latency wired headphones, laptop internal speakers, or aptX-LL earbuds) so their sound waves arrive at your ears at the exact same instant as a slower Bluetooth speaker.
* **CAN**: Smooth out steady-state buffer timing differences across multiple independent WASAPI render clients.
* **CANNOT**: Make an audio stream play *earlier* than the device's physical Bluetooth radio transmission, packet buffering, and onboard DAC/DSP latency. Software cannot pull audio backward in time.
* **CANNOT**: Guarantee sub-millisecond physical acoustic synchronization across random Bluetooth headphones without hardware synchronization protocols (like Bluetooth LE Audio Auracast).

---

### Manual Test Scenarios for Chunk 5

#### Test Scenario F: Guided Sync Calibration Wizard & Acoustic Pulse
1. Launch SyncCast (`dotnet run --project SyncCast`).
2. Go to **Audio Devices** and select at least two connected audio outputs (e.g. your Laptop Speakers and a pair of Bluetooth Headphones).
3. Click **Sync Calibration** in the left navigation rail.
4. Click **Start Calibration Pulse**:
   - Verify that a gentle, periodic 880 Hz metronome pulse (40ms duration, 800ms interval) is generated.
   - Verify the pulse is comfortable, quiet (-20 dBFS safe volume), and free of harsh clicks.
   - Verify the status badge updates to **PULSE RUNNING (880 Hz • 40ms • 800ms Interval)**.
5. Put on both headphones (or place the two speakers equidistant from your ears).
6. Listen for the sound:
   - If you hear a double-tap ("flam" effect), one output is arriving earlier than the other.
7. Locate the card for the **earlier** device on the screen:
   - Click the **+10ms** or **+5ms** nudge buttons to increase its delay.
   - Notice the sound waves getting closer together in time.
   - Continue nudging until the two clicks merge into a single solid ping centered in your head.
8. Click **Stop Pulse**:
   - Verify the pulse stops cleanly and all streams return to idle.

#### Test Scenario G: Live Delay Adjustment During Playback
1. Start **System Audio Sharing** with YouTube or Spotify playing music.
2. On the **Sync Calibration** page or on the **Audio Devices** page, drag the **Software Delay** slider on one output from 0ms to 200ms.
3. Observe:
   - The audio delay adjusts smoothly in real time without audio glitches, pops, or stream restarts.
   - Other output devices continue playing completely unaffected.
4. Click **Reset (0 ms)** to instantly return that device to 0 ms baseline.

#### Test Scenario H: Reference Device Selection & Failover
1. On the **Sync Calibration** page, click **Set as Reference** on one of the secondary outputs.
2. Verify:
   - The device immediately receives the **REFERENCE** badge.
   - The top header updates: `Timing Reference: [Device Name]`.
   - The setting is saved to `%APPDATA%\SyncCast\settings.json`.
3. Disconnect or turn off the reference device:
   - SyncCast marks the stream as Disconnected.
   - The app logs an advisory and cleanly shifts reference to another selected active endpoint without wiping your configured delay values.

#### Test Scenario I: Diagnostics & Drift Telemetry Inspection
1. Go to **Diagnostics** in the navigation rail during active playback.
2. Inspect the **Session Synchronization & Clock Alignment** card:
   - Review **Timing Reference**, **Configuration Status**, and the per-destination health table.
   - Verify **Software Delay**, **Queue Occupancy**, **Estimated Drift**, and **Drops / Underruns** update in real time.
   - Confirm that the Acoustic Latency Disclaimer is visible, clearly stating that hardware over-the-air acoustic latency is unknown.

#### Test Scenario J: Chunk 6 Stability, Stress Testing & Diagnostics Export
1. Launch SyncCast (`dotnet run --project SyncCast`).
2. Run automated stress test suite:
   ```powershell
   dotnet test
   ```
   - Verifies 20 consecutive Start/Stop cycles of Test Tone and System Audio without resource exhaustion or memory leaks.
   - Verifies 10 concurrent start invocations safely serialized without state corruption.
3. In the running application, start System Audio Sharing across 2+ destinations.
4. Go to **Diagnostics** in the navigation rail.
5. Click **Copy Diagnostics**:
   - Verify notice: *"Diagnostics copied to clipboard."*
   - Paste clipboard content into Notepad.
   - Confirm it contains OS & runtime details, engine mode, capture state, per-stream buffer occupancy, frame counts, and the 50 most recent diagnostic log events.
   - Confirm zero audio data is recorded or stored.
6. Repeatedly click **Start / Stop Sharing** 5 times in rapid succession:
   - Verify the session transitions smoothly without UI lockups, deadlocks, or crashed WASAPI render threads.

---

## Local Hardware Validation Checklist (For End User)

Since this software environment does not have physical Bluetooth headphones attached, follow this exact procedure on your physical Windows laptop:

1. **Pair Your Bluetooth Audio Devices in Windows Settings**:
   - Open Windows Settings (`Win + I`) -> **Bluetooth & devices**.
   - Pair your Bluetooth headphones, earbuds, or speakers. Ensure they show as **"Connected voice, music"**.
2. **Launch SyncCast**:
   ```powershell
   dotnet run --project SyncCast
   ```
3. **Scan Devices**:
   - On the **Audio Devices** page, click **Scan Devices**.
   - Verify your paired Bluetooth endpoints appear with the Bluetooth radio icon.
4. **Select Endpoints (up to 5)**:
   - Check the checkbox on each device you want to test.
   - Note: If Windows allows multiple Bluetooth audio connections simultaneously, both will initialize. If your laptop's Bluetooth adapter firmware or Windows A2DP driver restricts active streaming to 1 device, SyncCast will report the specific Windows error honestly on the second device card without crashing.
5. **Start System Audio Sharing**:
   - Select your default PC speakers as the **Capture Source**.
   - Click **Start Sharing**.
   - Play music from your browser or media player.
   - Verify sound is forwarded to your secondary devices.
6. **Adjust Delay Compensation**:
   - On **Sync Calibration**, use the nudge buttons or delay slider to align audio if you notice an echo.
7. **Copy Diagnostics If Issues Occur**:
   - On the **Diagnostics** page, click **Copy Diagnostics** to get a complete snapshot of all stream formats, buffer health, and Windows errors.

---

## Release Packaging & Distribution

### 1. Portable Windows x64 Distribution
SyncCast is packaged as a fully self-contained portable distribution that requires **no development tools, no .NET SDK, and no runtime installations** on the host machine.

* **Package File**: `artifacts/SyncCast-win-x64-portable.zip`
* **Uncompressed Directory**: `artifacts/SyncCast-win-x64/`
* **Primary Executable**: `SyncCast.exe` (Embedded icon, DPI awareness manifest, zero-admin requirements)

#### How to Run the Portable Release:
1. Extract `SyncCast-win-x64-portable.zip` to any local folder (e.g. `C:\Tools\SyncCast` or `Desktop`).
2. Double-click `SyncCast.exe`.
3. The application will initialize, discover all active Windows Core Audio endpoints, and load user settings from `%APPDATA%\SyncCast\settings.json`.

> [!NOTE]
> **Windows SmartScreen Notice**:
> SyncCast is a personal open-source utility and is not signed with a commercial Microsoft Authenticode code-signing certificate ($300+/yr). Windows SmartScreen may show an alert on first launch (*"Windows protected your PC"*). Click **More info** -> **Run anyway**. SyncCast never modifies system registry keys, never alters Windows audio drivers, and never requests administrator privileges.

### 2. Building from Source
To compile and test SyncCast from source:

```powershell
# Prerequisites: .NET 8.0 SDK on Windows 10/11
git clone <repo-url>
cd SyncB

# Build in Release mode (0 warnings, 0 errors)
dotnet build -c Release

# Run automated test suite (81 tests)
dotnet test -c Release

# Launch development application
dotnet run --project SyncCast -c Release
```

### 3. Publishing the Standalone Release Package
To reproduce the self-contained portable release package:

```powershell
# Publish win-x64 self-contained build
dotnet publish SyncCast/SyncCast.csproj -c Release -r win-x64 --self-contained true -o artifacts/SyncCast-win-x64

# Package into ZIP archive
Compress-Archive -Path artifacts/SyncCast-win-x64 -DestinationPath artifacts/SyncCast-win-x64-portable.zip -CompressionLevel Optimal
```

### 4. Optional Inno Setup Windows Installer
A complete Inno Setup script is provided at `installer/SyncCast.iss`.

* **Target Directory**: `{localappdata}\Programs\SyncCast` (Per-user, non-elevated installation).
* **Shortcuts**: Adds Start Menu shortcut and optional Desktop icon.
* **Uninstaller**: Registers a clean Windows Add/Remove Programs uninstaller.

To compile the installer on a machine with Inno Setup 6 installed:
```powershell
iscc.exe installer\SyncCast.iss
```
The compiled installer will be generated in `artifacts/SyncCast-1.0.0-Setup.exe`.
*(Note: If `iscc.exe` is not installed on your system, use the verified portable ZIP package).*

### 5. Release Artifact Checksums (SHA-256)
All release packages are verified with SHA-256 hashes recorded in `artifacts/checksums.sha256`:

```
4F19C31620455C8961FDEEC7B296248AD4D01D64CBEC5D692C8F782AFA45925F *SyncCast-win-x64-portable.zip
E465ADD5E701B52F3886E7C3D83318643318837176BCC161F841867DED2B6649 *SyncCast.exe
```

---

## Configuration & Storage Locations

| Item | Path / Location | Description |
| :--- | :--- | :--- |
| **User Settings** | `%APPDATA%\SyncCast\settings.json` | Stores theme, volumes, mute flags, delays, reference device |
| **Diagnostic Clipboard** | Windows Clipboard | Structured text export with OS, audio streams, metrics, and logs |
| **Application Executable** | Portable directory / `{localappdata}\Programs\SyncCast` | Self-contained win-x64 application bundle |

---

## Troubleshooting & Diagnostics

* **No Sound on Mirrored Endpoints**:
  - Verify device volume is not set to 0% and Mute is toggled off.
  - Verify the device shows **Playing** (green) rather than **Disconnected** or **Failed**.
  - Check the Diagnostics page for buffer underruns or WasapiOut exceptions.
* **Echo / Acoustic Slapback Between Devices**:
  - Open **Sync Calibration** in the navigation rail.
  - Start the **Calibration Pulse** or play music.
  - Nudge the delay on the faster device by +10ms or +20ms until the beats coalesce.
* **Bluetooth Headphone Disconnects**:
  - SyncCast automatically isolates the failed device without interrupting other speakers.
  - Reconnect the Bluetooth device in Windows Settings.
  - If Auto-Reconnect is enabled, SyncCast will resume playback automatically within 1–15 seconds.
  - Alternatively, click the **Retry** button on the device card.

---

## License

This project is licensed under the [GNU General Public License v3.0 (GPL-3.0)](LICENSE).
