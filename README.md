# SyncCast (SyncB) — Multi-Device Audio Output Router

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

## Evolution & Stage Breakdown

SyncCast is engineered to discover, manage, capture, and concurrently render audio across multiple independent Windows render endpoints without third-party kernel drivers, virtual audio cables, or paid hardware transmitters.

### Stage 1: Audio Endpoint Discovery & Bluetooth Diagnostics
* **Native Windows Core Audio Discovery**: Enumerates real render endpoints via `NAudio.CoreAudioApi` / `MMDeviceEnumerator` (DataFlow `Render`).
* **Transport Classification Heuristics**: Analyzes Windows Core Audio property store strings, PnP IDs, and driver descriptors to classify endpoints as `Bluetooth`, `Built-In / Wired`, or `Unknown Transport`.
* **Honest Unknown Reporting**: If the transport cannot be determined reliably from Windows metadata, it is labeled `Unknown Transport` instead of guessing.
* **5-Device Selection Enforcement**: Enforces a strict maximum limit of five candidate audio output endpoints. Toggling a 6th device generates visual feedback and warning diagnostics without crashing.
* **Selection State Preservation**: Selected devices are preserved during manual and periodic device refreshes as long as the endpoint remains active.
* **Distinct Status Indicators**: Clearly separates `Available`, `Selected`, `Unavailable` (unplugged/disabled), and `Playback Not Tested`.

### Chunk 2: Real Multi-Output Audio Engine (NAudio WASAPI)
* **Low-Volume Generated Test-Tone Source**: Built-in continuous sine wave generator with Start/Stop controls. Features a safe default level (-20 dBFS / 10% volume) with user-selectable reference frequencies (440 Hz Concert A, 528 Hz Harmonic, 1000 Hz Standard Sine).
* **Multi-Stream Shared-Mode Architecture**: Opens a separate, dedicated `WasapiOut` render stream in shared mode for each selected, available endpoint (up to 5).
* **Automatic Format Adaptation & Resampling**: Each stream inspects the endpoint's native `device.AudioClient.MixFormat` and applies sample rate resampling (`WdlResamplingSampleProvider`), channel mapping (mono/stereo/surround), and bit-depth conversion (32-bit Float, 16-bit, 24-bit, 32-bit PCM) on demand.
* **Independent Stream Lifecycle Management**: Each stream operates with its own lifecycle (`Stopped`, `Initializing`, `Playing`, `Failed`).
* **Failure Isolation**: An individual stream failure (e.g. disconnected headphone, Bluetooth packet loss, or device exclusive lock) **never** crashes or halts remaining streams.
* **Honest Concurrency Reporting**: Never counts selected endpoints as successful playback. Only streams that successfully initialize and stream audio samples are reported as playing.

### Chunk 3: Real Windows System-Audio Capture & Multi-Output Distribution
* **Dedicated WASAPI Loopback Capture Service**: Uses `NAudio.Wave.WasapiLoopbackCapture` targeting the specific active Windows audio render endpoint selected by the user.
* **Real Mix Capture (Not Visual Simulation)**: Captures actual PCM/IEEE Float audio frames rendered through the selected playback device by any desktop application, browser, media player, or game.
* **Feedback and Duplicate Audio Prevention (Zero Echo)**:
  - If the user selects the capture source as one of the output destinations, SyncCast **does not** open a second mirrored playback stream back to that same endpoint. Instead, it marks that device as **`Native Source`** (playing via Windows native mixer) and forwards live audio **only to the other selected destinations**.
  - If the capture source is *not* selected as an output, the UI displays an explicit advisory that Windows will continue rendering audio natively through the source device in addition to the mirrored destinations.
  - Recursion guard prevents loopback audio from recapturing mirrored streams.
* **Bounded Buffering & Latency Drop Policy**:
  - Each destination stream maintains a bounded 250ms ring buffer (`BufferedWaveProvider`) with `DiscardOnBufferOverflow = true` and `ReadFully = true`.
  - When a destination endpoint is slow (e.g., Bluetooth packet backpressure) or momentarily stalls, excess audio frames are discarded rather than allowing unbounded memory growth or runaway playback latency.
* **Mutually Exclusive Operating Modes**:
  - **Mode 1: System Audio Sharing**: Captures live Windows sound and mirrors it across eligible destinations.
  - **Mode 2: Test Tone Generator**: Synthesizes reference test tones for hardware calibration and channel verification.
  - Starting one mode automatically stops any running mode to ensure clean stream isolation and prevent audio mixing conflicts.
* **Honest Diagnostics**:
  - Differentiates: (1) Capture Initialized, (2) Audio Frames Actually Captured, (3) Destination Stream Opened, (4) Audio Rendered, and (5) Endpoint Failure.
  - Displays real-time captured data metrics (bytes captured, format description, activity timestamp).

### Chunk 4: Per-Device Volume, Master Volume, Audio Health Monitoring & Device Reconnection
* **Global Master Volume & Mute**:
  - Global master volume slider (0–100%) with a quick-action Mute/Unmute toggle.
  - Applies master gain smoothly to all active SyncCast output streams without restarting capture or recreating WasapiOut instances.
  - Master mute silences forwarded audio without modifying the user's global Windows system volume.
  - Smooth gain ramp (`SmoothGainSampleProvider`) dynamically adjusts gain over 10–20ms to eliminate clicks and pops during volume changes.
* **Per-Device Volume & Mute Controls**:
  - Each selected destination card features an independent volume slider (0–100%) and Mute/Unmute toggle.
  - Formula: `effectiveGain = (isMasterMuted || isDeviceMuted) ? 0.0 : (masterGain × deviceGain)`.
  - Muting one destination does not silence other destinations.
  - Unmuting instantly restores the previous volume setting.
  - Gain is applied directly within the sample processing pipeline (`SmoothGainSampleProvider`), protected by hard limits `[-1.0, +1.0]` against digital clipping.
  - Gain is applied exactly once before format conversion; it is never multiplied repeatedly across copied or retried buffers.
* **Audio Health Monitoring & Latency Diagnostics Dashboard**:
  - Collapsible real-time health dashboard with throttled 250ms UI updates (avoids UI stalls from high-frequency audio callbacks).
  - Aggregate metrics: Total frames captured, estimated capture-to-output processing delay (~ms), active & healthy destinations, total dropped frames.
  - Per-destination stream metrics: Stream initialization status, audio bytes written, audio frames processed, current ring buffer occupancy (bytes and ms), buffer overflow drop count, last successful write timestamp, last error message, and estimated internal buffer delay.
  - **Honest Latency Disclosure**: Explicitly differentiates measured software buffer delay (~5–20ms) from hardware Bluetooth acoustic latency (`Unknown (Hardware/Bluetooth dependent)`). Does not fake end-to-end acoustic latency measurements.
* **Native Endpoint Disconnection Detection**:
  - Uses Windows Core Audio `IMMNotificationClient` callbacks (`OnDeviceStateChanged`, `OnDeviceRemoved`) via `AudioEndpointNotificationService`.
  - Disconnecting or unplugging one device isolates and transitions only that stream to `Disconnected` or `Recovering` while keeping all other healthy streams playing smoothly.
* **Automatic Reconnection & Recovery Policy**:
  - Controlled exponential backoff policy: 1s, 2s, 4s, 8s, 15s (capped at 15s).
  - Preserves the disconnected device's volume and mute settings.
  - Recovery attempts and results are tracked in the real-time event log.
  - Canceled immediately upon session stop (`Stop Sharing`), device deselection, or application shutdown.
  - Manual `Retry` button provided on device cards if auto-reconnection is disabled or retries are exhausted.
* **Settings Persistence**:
  - User preferences saved to `%APPDATA%\SyncCast\settings.json`.
  - Persists Master Volume, Master Mute, Auto-Reconnect toggle, per-device volume/mute preferences, and last selected capture source.
  - Robust error handling: Corrupted, empty, or outdated settings safely fall back to defaults without application failure.
  - Asynchronous, debounced disk writes ensure audio streaming threads are never blocked.

### Stage 5: Windows 11 Fluent Design UI/UX Redesign
* **Native Windows 11 Fluent App Shell**:
  - Integrated DWM API backdrop interop (`WindowBackdropHelper`): Supports Windows 11 Mica backdrop (`DWMWA_SYSTEMBACKDROP_TYPE = 38`, `DWMWA_MICA_EFFECT = 1029`), rounded window corners (`DWMWA_WINDOW_CORNER_PREFERENCE = 33`), and immersive title bar theme synchronization (`DWMWA_USE_IMMERSIVE_DARK_MODE = 20`).
  - Graceful fallback for Windows 10 or virtualized environments without composition crashes.
* **Compact Left Navigation Rail**:
  - Fixed-width 230px navigation rail with branding header, live audio indicator pill, and modern Fluent pill radio buttons.
  - 6 Dedicated Views:
    1. **Overview**: Real-time status header, primary Start/Stop Sharing action button, feedback-prevention advisory, capture source selector with telemetry, global master volume slider & mute, and test-tone hardware calibration mode.
    2. **Audio Devices**: Dual-panel layout with discovered Windows endpoints (transport icons, friendly name, format, selection toggle) and configured active streams (independent volume sliders, mute toggles, ring-buffer telemetry, retry buttons).
    3. **Sync Calibration**: Dedicated guided alignment wizard with Acoustic Alignment Pulse generator, listening tips, reference device indicator, and per-device delay sliders with ±5ms and ±10ms nudge controls.
    4. **Diagnostics**: Real-time aggregate pipeline health cards (capture state, frames captured, buffer delay, healthy destination count), session synchronization and clock alignment table, acoustic latency disclosure, and a monospace diagnostic event log.
    5. **Audio Settings**: Grouped settings cards for Appearance (Dark, Light, Follow Windows), Device Recovery (Auto-Reconnect toggle with backoff details), Zero-Echo Protection, and local file storage paths.
    6. **About**: System runtime info (.NET 8, OS description, NAudio WASAPI Shared Mode), Bluetooth radio concurrency principles, and zero-kernel-driver architectural principles.
* **Vector Fluent Iconography & Typography**:
  - All icons rendered via standard Fluent vector geometry paths (no emoji or low-res bitmap approximations).
  - Clean font hierarchy using Segoe UI Variable Display and Segoe UI Variable Text.
* **Multi-Theme Support**:
  - Real-time dynamic theme switching via `ThemeService`: **Dark** (Default Fluent Dark Mica), **Light** (Fluent Light Mica), or **Follow Windows** (queries `AppsUseLightTheme` in Windows registry).
  - Preference saved and loaded automatically from `%APPDATA%\SyncCast\settings.json`.

---

### Chunk 5: Multi-Output Audio Synchronization, Per-Device Delay Calibration, Drift Monitoring & Guided Sync Wizard
* **Shared Audio Timeline & Sequence Tracking (`SharedAudioTimeline`)**:
  - High-resolution monotonic clock (`Stopwatch.GetTimestamp`) establishes a unified reference epoch across all outputs.
  - Distributes audio blocks with contiguous, monotonically increasing sequence numbers and sample-frame offsets.
  - Tracks cumulative source frames and per-destination frame consumption, computing real-time software clock drift deltas without fabricating imaginary physical measurements.
* **Bounded Click-Free Delay Engine (`DelaySampleProvider`)**:
  - Pre-allocated circular delay line (0–1000 ms range) placed ahead of volume scaling and format conversion.
  - Implements fractional delay slewing (`MaxSlewPerFrame = 0.002`) to ensure live adjustments during playback glide smoothly without clicks, pops, or harsh buffer underruns.
  - Independent per-device delay values with instant nudge buttons (±5ms, ±10ms) and one-click reset to 0ms.
* **Timing Reference Device Workflow**:
  - Allows the user to designate any active output as the **Timing Reference** (REFERENCE badge).
  - Clarifies that the reference is a software alignment anchor, not a magic guarantee of zero hardware latency.
  - Automatic disconnection recovery: if the reference device disconnects or is removed, SyncCast logs an advisory and cleanly shifts reference to another selected active endpoint without wiping saved delay values.
* **Guided Manual Calibration Experience (`CalibrationPulseSampleSource`)**:
  - Dedicated **Sync Calibration** page with acoustic alignment guidance.
  - Generates gentle, Hann-windowed raised-cosine musical metronome pings (880 Hz A5, 40ms duration, 800ms period) at a safe -20 dBFS volume level.
  - Completely free of harsh clicks, pops, or high-volume bursts.
  - Clear listening instructions: human ears can detect relative delays as small as 5–10ms (the "flam" or slapback echo effect). Users delay the earlier (faster) device until clicks fuse into a single centered image.
* **System-Audio Mode Synchronization Limitations & Advisories**:
  - When the loopback capture source is also in selected outputs, it bypasses loopback to prevent feedback and plays natively via Windows at a 0 ms mixer baseline.
  - Software delay can only delay mirrored destinations relative to each other; it **cannot** pull native Windows mixer audio backward in time.
  - Prominent UI advisory warns the user when the source device cannot be synchronized with mirrored destinations.
* **Software Timing & Drift Diagnostics**:
  - Tracks queue buffer occupancy (ms and bytes), estimated buffer delay, clock drift relative to source frames, and drop counts.
  - Integrated Session Synchronization status card in Diagnostics page displaying reference endpoint, configuration status, and per-destination health.
* **Settings Persistence**:
  - Persists `SyncDelayMs` per device, `ReferenceEndpointId`, and `DriftMonitoringEnabled` to `%APPDATA%\SyncCast\settings.json` with automatic range sanitization (0–1000 ms).

### Chunk 6: Comprehensive Stability Hardening, Concurrency Serialization & Diagnostics Export
* **Strict Session Serialization (`SemaphoreSlim _sessionLock`)**:
  - Resolves re-entrancy races between `StartPlaybackAsync`, `StartSystemAudioSharingAsync`, `StartCalibrationPulseAsync`, `StopPlaybackAsync`, and `RetryEndpointAsync`.
  - Ensures clean teardown before starting new audio sessions, preventing concurrent WASAPI client initializations or state corruption.
* **CancellationTokenSource & Timer Resource Leak Fixes**:
  - Auto-reconnect background tasks (`ScheduleAutoReconnect`) safely dispose their `CancellationTokenSource` upon completion or cancellation within guaranteed `finally` blocks.
  - Periodic diagnostics update timer (`_diagnosticUpdateTimer`) throttled to 250ms with sync context marshalling to eliminate UI thread stalls.
* **Zero-Allocation Hot-Path Optimization**:
  - Replaced per-sample `BitConverter.GetBytes()` allocation in `FormattedWaveProvider` with zero-allocation `Buffer.BlockCopy`.
  - Replaced heap-allocating `_activeStreams.Values` snapshots on every audio capture tick with zero-allocation `ConcurrentDictionary` struct enumeration.
* **Format Bridge Frame Alignment & Resilience**:
  - Enforced 4-byte and 2-byte frame boundary truncations in `ExtensibleFloatSampleProvider` and `Pcm16BitToSampleProvider` before copying into destination buffers.
  - Enhanced `CreateSampleProviderBridge` to support both IEEE Float and PCM subformats under `WaveFormatExtensible` without throwing `ArgumentException`.
* **Clipboard Diagnostics Export (`Copy Diagnostics`)**:
  - One-click copy button on the Diagnostics page exports complete system telemetry, capture state, per-stream buffer occupancy, volume, drift metrics, and the 50 most recent event log entries to the Windows clipboard.
  - Strictly software diagnostics only — zero transmission to external cloud services and zero recording of audio data.
* **Comprehensive Automated Test Expansion**:
  - **81 total automated tests** (0 failures, 100% pass rate) covering repeated 20-cycle Start/Stop stress tests, concurrent start serialization, idempotent disposal, anti-clipping gain bounds, corrupted settings recovery, and destination failure isolation.

### Chunk 7: Final Product Polish, Release Build, Portable Packaging & Release Validation
* **Unified Application Identity & Multi-Resolution Windows Icon**:
  - Professional high-fidelity multi-resolution icon (`SyncCast.ico`: 256x256, 64x64, 48x48, 32x32, 16x16) embedded as `<ApplicationIcon>` and packaged as a WPF window title bar resource.
  - Native `app.manifest` integrated with `PerMonitorV2` DPI awareness, Windows 10/11 OS GUID declarations (`{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}`), and standard `asInvoker` user security (zero administrator privilege elevation required).
  - Version `1.0.0` synchronized across `SyncCast.csproj`, Assembly Information, and runtime About telemetry.
* **Release Mode Optimization & Build Cleanliness**:
  - Solution compiles in Release configuration with **0 warnings and 0 errors**.
  - All 81 automated tests pass in Release mode with 100% pass rate.
* **Self-Contained Portable Windows x64 Distribution**:
  - Standalone release package published to `artifacts/SyncCast-win-x64/` with runtime self-containment (zero external .NET SDK or runtime installation required on the target machine).
  - Packaged portable ZIP archive `artifacts/SyncCast-win-x64-portable.zip` (67.16 MB) stripped of debugging symbols (`.pdb`) and crash dump tools for a clean consumer footprint.
  - Generates verifiable SHA-256 checksums in `artifacts/checksums.sha256`.
* **Zero-Elevated Per-User Storage & Writable Defaults**:
  - Persistent user preferences, device gain calibrations, and delay values store strictly in `%APPDATA%\SyncCast\settings.json`.
  - Zero attempt to write mutable settings into protected installation directories (`Program Files`).
* **Inno Setup Script Specification**:
  - Provided `installer/SyncCast.iss` for clean, per-user Windows installation (`{localappdata}\Programs\SyncCast`) with Start Menu shortcuts and uninstaller registration.
  - Documented honest status: Host build environment lacks `iscc.exe`, preserving the portable ZIP as the verified primary distribution.
* **Packaged Smoke Test Validation**:
  - Published self-contained executable launched on the physical host and verified live UI initialization, active process responding state (PID verified, `Responding = True`), zero startup crashes, and clean resource teardown.

---

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
