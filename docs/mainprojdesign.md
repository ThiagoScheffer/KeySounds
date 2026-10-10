> **Historical architecture proposal.** This document describes the initial recommended design, including a multi-project solution that was not adopted. For the architecture implemented today, read the [current system design (SDD)](../.sdlc/design/SYSTEM-DESIGN.md). Use the [documentation index](README.md) for current guidance and phase records.

## Avalonia + C# architecture

Avalonia is a strong choice when you want a modern interface now and the option to support macOS or Linux later. The correct design is to keep **Avalonia restricted to presentation and application lifecycle**, while keyboard capture and low-latency audio remain independent platform services.

```text
Avalonia UI
    ↓
Application services
    ↓
Input and audio abstractions
    ↓
Windows-specific implementations
    ├── Win32 Raw Input
    └── WASAPI through NAudio
```

Avalonia supports access to native window handles and platform APIs, so it can coexist cleanly with Win32 interop. It also includes native system-tray support. ([docs.avaloniaui.net][1])

## Recommended stack

| Area           | Technology                                    |
| -------------- | --------------------------------------------- |
| Runtime        | **.NET 10**                                   |
| Language       | **C#**                                        |
| UI             | **Avalonia UI**                               |
| UI pattern     | **MVVM, without a heavy framework initially** |
| Keyboard input | **Win32 Raw Input via P/Invoke**              |
| Audio          | **NAudio + WASAPI shared mode**               |
| Configuration  | **System.Text.Json**                          |
| Logging        | **Microsoft.Extensions.Logging**              |
| Packaging      | **Self-contained `win-x64` publish**          |
| Testing        | **xUnit or NUnit**                            |
| Installer      | **WiX Toolset, Inno Setup, or MSIX later**    |

For the first implementation, target Windows only:

```xml
<TargetFramework>net10.0</TargetFramework>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

Do not pursue cross-platform input and audio immediately merely because Avalonia supports cross-platform UI. Global keyboard capture and low-latency audio require materially different implementations on Windows, macOS, and Linux.

---

# Solution structure

Use a modular monolith with six projects:

```text
KeySound.sln
│
├── KeySound.App
│   ├── App.axaml
│   ├── Views
│   ├── ViewModels
│   ├── Assets
│   └── Program.cs
│
├── KeySound.Core
│   ├── Models
│   ├── Events
│   ├── Interfaces
│   └── ValueObjects
│
├── KeySound.Application
│   ├── Profiles
│   ├── SoundRouting
│   ├── Settings
│   └── UseCases
│
├── KeySound.Audio
│   ├── AudioEngine
│   ├── Mixer
│   ├── SampleCache
│   └── VoicePool
│
├── KeySound.Platform.Windows
│   ├── RawInput
│   ├── ForegroundWindow
│   ├── Startup
│   └── AudioDevices
│
└── KeySound.Tests
```

This is enough separation to keep platform code isolated without creating unnecessary abstraction layers.

## Project responsibilities

### `KeySound.App`

Contains only:

* Avalonia views
* View models
* Tray icon
* Window lifecycle
* Commands
* Dependency composition

It should not contain P/Invoke definitions, audio mixing logic, or keyboard-device parsing.

### `KeySound.Core`

Contains platform-independent contracts:

```csharp
public interface IKeyboardInputSource
{
    event EventHandler<KeyboardInputEvent> InputReceived;

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IAudioEngine
{
    void Play(SoundTrigger trigger);
    Task InitializeAsync(CancellationToken cancellationToken);
    Task ShutdownAsync(CancellationToken cancellationToken);
}

public interface ISoundProfileRepository
{
    Task<IReadOnlyList<SoundProfile>> GetAllAsync(
        CancellationToken cancellationToken);

    Task SaveAsync(
        SoundProfile profile,
        CancellationToken cancellationToken);
}
```

### `KeySound.Application`

Makes decisions such as:

* Which profile is active
* Which sound corresponds to a key
* Whether key repeat is enabled
* Whether the foreground application is excluded
* Which sample variation should play
* Per-device and per-key volume

### `KeySound.Platform.Windows`

Contains:

* Raw Input registration
* `WM_INPUT` parsing
* Keyboard device enumeration
* Foreground process detection
* Windows startup registration
* Windows session and device-change handling

---

# Avalonia application lifecycle

The application should behave primarily as a tray utility:

1. Start silently.
2. Initialize the audio engine.
3. Create the hidden native window required for Raw Input.
4. Register for global keyboard events.
5. Display the tray icon.
6. Open the settings window only when requested.
7. Continue running when the settings window is closed.
8. Exit only from the tray menu or explicit shutdown command.

Avalonia provides a cross-platform `TrayIcon` control with a native menu. ([docs.avaloniaui.net][2])

Example:

```xml
<Application
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    x:Class="KeySound.App.App">

    <TrayIcon.Icons>
        <TrayIcons>
            <TrayIcon
                Icon="/Assets/keysound.ico"
                ToolTipText="KeySound">

                <TrayIcon.Menu>
                    <NativeMenu>
                        <NativeMenuItem Header="Open settings"
                                        Command="{Binding OpenSettingsCommand}" />

                        <NativeMenuItem Header="Enabled"
                                        ToggleType="CheckBox"
                                        IsChecked="{Binding IsEnabled}"
                                        Command="{Binding ToggleEnabledCommand}" />

                        <NativeMenuItemSeparator />

                        <NativeMenuItem Header="Exit"
                                        Command="{Binding ExitCommand}" />
                    </NativeMenu>
                </TrayIcon.Menu>
            </TrayIcon>
        </TrayIcons>
    </TrayIcon.Icons>
</Application>
```

Use explicit shutdown mode so closing the settings window does not terminate the process:

```csharp
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    base.OnFrameworkInitializationCompleted();
}
```

---

# Raw Input integration with Avalonia

## Main technical issue

Raw Input delivers keyboard events through the native Windows message:

```text
WM_INPUT
```

An Avalonia window has an underlying native handle, and Avalonia supports retrieving native platform handles for direct interoperability. ([docs.avaloniaui.net][1])

However, coupling Raw Input directly to the visible settings window is poor architecture because:

* The user may close the window.
* The native handle may be recreated.
* UI lifecycle events should not control keyboard capture.
* The input listener should remain active independently of UI visibility.

## Superior implementation: dedicated hidden Win32 message window

Create a dedicated message-only or hidden native Win32 window in `KeySound.Platform.Windows`.

```text
Avalonia process
├── Avalonia settings window
├── Avalonia tray icon
└── Hidden Win32 input window
      └── receives WM_INPUT
```

This is more stable than attaching to the Avalonia window procedure.

Benefits:

* No dependency on Avalonia window recreation
* No subclassing of Avalonia’s native window procedure
* Raw Input remains operational when the settings window is closed
* Platform integration is isolated
* Easier automated testing around the event normalization layer

Windows requires applications to call `RegisterRawInputDevices` before receiving `WM_INPUT`. ([Microsoft Learn][3])

Conceptual registration:

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct RawInputDevice
{
    public ushort UsagePage;
    public ushort Usage;
    public uint Flags;
    public nint TargetWindow;
}
```

```csharp
private const ushort GenericDesktopUsagePage = 0x01;
private const ushort KeyboardUsage = 0x06;
private const uint RidevInputSink = 0x00000100;

var device = new RawInputDevice
{
    UsagePage = GenericDesktopUsagePage,
    Usage = KeyboardUsage,
    Flags = RidevInputSink,
    TargetWindow = messageWindowHandle
};

bool registered = RegisterRawInputDevices(
    [device],
    1,
    (uint)Marshal.SizeOf<RawInputDevice>());

if (!registered)
{
    throw new Win32Exception(Marshal.GetLastWin32Error());
}
```

`RIDEV_INPUTSINK` enables background input delivery to the designated window. A background event arrives as `RIM_INPUTSINK`; the raw data is then read using `GetRawInputData`. ([Microsoft Learn][4])

## Normalized keyboard event

Do not expose Win32 structures outside the Windows platform project.

Convert each raw event immediately:

```csharp
public readonly record struct KeyboardInputEvent(
    ushort ScanCode,
    ushort VirtualKey,
    KeyboardAction Action,
    nint DeviceHandle,
    bool IsExtended,
    long Timestamp);
```

```csharp
public enum KeyboardAction
{
    KeyDown,
    KeyUp
}
```

Use scan codes as the primary physical-key identifier. Virtual key codes are useful for semantic mappings but can vary according to keyboard layout.

---

# Input processing pipeline

```text
WM_INPUT
    ↓
GetRawInputData
    ↓
WindowsRawInputParser
    ↓
KeyboardInputEvent
    ↓
bounded Channel<KeyboardInputEvent>
    ↓
SoundRoutingService
    ↓
AudioEngine.Play()
```

Use a bounded `Channel<T>`:

```csharp
var options = new BoundedChannelOptions(256)
{
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleReader = true,
    SingleWriter = false
};

_channel = Channel.CreateBounded<KeyboardInputEvent>(options);
```

Why bounded:

* Prevents unbounded memory growth.
* Keeps the input callback non-blocking.
* Protects the application during pathological input rates.
* Dropping an old click event is preferable to freezing the keyboard processing path.

The raw callback should perform only:

1. Parse.
2. Normalize.
3. Enqueue.
4. Return.

No file access, profile deserialization, UI updates, or audio decoding should happen there.

---

# Audio engine

## Recommended initial engine

Use:

```text
NAudio
WasapiOut
Shared mode
Event synchronization
48 kHz
32-bit floating-point mixer
```

NAudio supports WASAPI playback and multiple Windows audio APIs. ([GitHub][5])

The audio engine should be one persistent graph:

```text
Cached samples
     ↓
Voice instances
     ↓
32-bit mixer
     ↓
Limiter
     ↓
WASAPI output
```

Do not instantiate `WasapiOut` or an audio file reader per keypress.

## Suggested components

```text
AudioEngine
├── WasapiOut
├── MixingSampleProvider
├── SampleCache
├── VoicePool
├── MasterVolumeProvider
└── SoftLimiter
```

A simplified initial implementation:

```csharp
public sealed class AudioEngine : IAudioEngine, IDisposable
{
    private readonly MixingSampleProvider _mixer;
    private readonly WasapiOut _output;
    private readonly SampleCache _samples;

    public AudioEngine(SampleCache samples)
    {
        _samples = samples;

        _mixer = new MixingSampleProvider(
            WaveFormat.CreateIeeeFloatWaveFormat(
                sampleRate: 48_000,
                channels: 2))
        {
            ReadFully = true
        };

        _output = new WasapiOut(
            AudioClientShareMode.Shared,
            useEventSync: true,
            latency: 20);

        _output.Init(_mixer);
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        _output.Play();
        return Task.CompletedTask;
    }

    public void Play(SoundTrigger trigger)
    {
        var sample = _samples.Get(trigger.SampleId);

        var voice = new CachedSoundSampleProvider(
            sample,
            trigger.Volume,
            trigger.Pan);

        _mixer.AddMixerInput(voice);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        _output.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _output.Dispose();
    }
}
```

This is acceptable for an MVP, but production code needs:

* Voice limits
* Buffer reuse
* Device invalidation recovery
* Sample-rate conversion
* Clipping protection
* Controlled concurrency
* Output-device selection

## Latency target

Target measured key-to-audio onset:

|      Result | Assessment           |
| ----------: | -------------------- |
| Under 10 ms | Excellent            |
|    10–20 ms | Strong               |
|    20–30 ms | Usable               |
| Above 30 ms | Visibly disconnected |

Do not optimize based only on the configured WASAPI buffer. Measure actual latency using:

* Keyboard LED or switch activation signal
* Audio waveform recording
* High-frame-rate camera or loopback measurement

The perceived result also includes:

```text
input delivery
+ application scheduling
+ audio buffer wait
+ DAC latency
+ speaker/headphone latency
```

---

# Sound routing

Use group-based mapping before per-key customization.

```csharp
public enum KeySoundGroup
{
    Alpha,
    Number,
    Modifier,
    Spacebar,
    Enter,
    Backspace,
    Navigation,
    Function,
    Numpad
}
```

Routing process:

```csharp
public sealed class SoundRoutingService
{
    private readonly IActiveProfileProvider _profiles;
    private readonly ISoundVariationSelector _variations;
    private readonly IAudioEngine _audio;

    public void Handle(KeyboardInputEvent input)
    {
        var profile = _profiles.Current;

        if (!profile.Enabled)
            return;

        var mapping = profile.Resolve(input);

        if (mapping is null)
            return;

        var sample = _variations.Select(mapping.SampleSet);

        _audio.Play(new SoundTrigger(
            sample.Id,
            mapping.Volume,
            mapping.Pan));
    }
}
```

The profile lookup should compile configuration into efficient runtime dictionaries when a profile is loaded:

```csharp
Dictionary<KeyIdentifier, CompiledKeyMapping>
```

Do not traverse nested JSON configuration on every keypress.

---

# MVVM choice

Use MVVM, but avoid a large framework at the beginning.

A minimal base class is sufficient:

```csharp
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));

        return true;
    }
}
```

`CommunityToolkit.Mvvm` is also reasonable and reduces boilerplate. The trade-off is another dependency and source-generated behavior. It is not a meaningful memory problem.

Recommended decision:

* Use `CommunityToolkit.Mvvm` for application screens.
* Keep core and audio projects independent of it.
* Do not use ReactiveUI unless the application actually requires complex reactive state composition.

ReactiveUI is capable, but unnecessary for this product’s first version.

---

# Dependency injection

Do not introduce a full generic-host architecture unless you need hosted services, structured startup, or extensive configuration.

A simple composition root is enough:

```csharp
public static class AppComposition
{
    public static AppServices Create()
    {
        var sampleCache = new SampleCache();
        var audioEngine = new AudioEngine(sampleCache);
        var inputSource = new WindowsRawInputSource();
        var profileRepository = new JsonProfileRepository();
        var router = new SoundRoutingService(
            profileRepository,
            audioEngine);

        return new AppServices(
            inputSource,
            audioEngine,
            profileRepository,
            router);
    }
}
```

Microsoft.Extensions.DependencyInjection is acceptable, but it will not materially improve a small application unless services and lifetimes become difficult to manage.

---

# Configuration model

Store settings separately from profiles.

```text
%LocalAppData%\KeySound\
├── settings.json
├── profiles\
│   ├── default.json
│   └── typewriter.json
└── soundpacks\
    └── mechanical-linear\
        ├── manifest.json
        └── samples\
```

Example profile:

```json
{
  "id": "deep-linear",
  "name": "Deep Linear",
  "enabled": true,
  "masterVolume": 0.72,
  "playOnKeyDown": true,
  "playOnKeyUp": false,
  "playOnRepeat": false,
  "groups": {
    "alpha": {
      "sampleSet": "linear-alpha",
      "volume": 0.75,
      "pitchVariation": 0.018
    },
    "spacebar": {
      "sampleSet": "linear-space",
      "volume": 0.9,
      "pitchVariation": 0.01
    }
  }
}
```

Write configuration atomically:

1. Serialize to a temporary file.
2. Flush.
3. Replace the existing file.
4. Keep one backup.

This prevents configuration corruption during crashes or power loss.

---

# UI scope

The first Avalonia UI should have four screens.

## Dashboard

* Global enable/disable
* Active profile
* Master volume
* Current keyboard
* Audio output
* Input and audio health indicators

## Profile editor

* Key groups
* Sample-set selection
* Volume
* Pitch variation
* Key-down and release controls
* Per-key overrides

## Sound-pack manager

* Import pack
* Preview sounds
* Show memory footprint
* Validate sample format
* Remove pack

## Application settings

* Start with Windows
* Minimize to tray
* Output device
* Audio latency setting
* Auto-repeat behavior
* Logging level

Avoid building a graphical keyboard editor in version one. It requires substantial layout, localization, keyboard-layout, and mapping work without validating the core product proposition.

---

# Cross-platform boundaries

Avalonia makes the UI portable, but these modules are platform-specific:

| Capability     | Windows       | macOS                    | Linux                            |
| -------------- | ------------- | ------------------------ | -------------------------------- |
| Global input   | Raw Input     | Event tap / IOHIDManager | evdev/XInput/Wayland constraints |
| Audio          | WASAPI        | Core Audio               | PipeWire/PulseAudio/ALSA         |
| Startup        | Registry/task | LaunchAgent              | Desktop entry/systemd user       |
| Active process | Win32         | NSWorkspace              | X11/portal/compositor-dependent  |

Define platform contracts now, but implement only Windows:

```csharp
public interface IPlatformInputSource;
public interface IPlatformAudioDeviceService;
public interface IForegroundApplicationService;
public interface IStartupRegistrationService;
```

Do not attempt to make NAudio cross-platform. Treat it as the Windows audio implementation.

---

# Memory expectations

Reasonable release-build targets:

| Component                      | Expected range |
| ------------------------------ | -------------: |
| .NET + Avalonia runtime/UI     |      45–100 MB |
| Audio engine and buffers       |        3–15 MB |
| Active PCM sound pack          |        5–50 MB |
| Profiles and application state |        2–10 MB |
| Typical total                  |  **60–160 MB** |

These are engineering estimates, not guarantees.

Avalonia will usually consume more memory than a minimal native Win32 interface but significantly less than a Chromium-based desktop application. The actual footprint will depend heavily on:

* Number and resolution of UI assets
* Sound sample duration
* Number of loaded variations
* Whether all profiles are loaded simultaneously
* Debug versus release build
* GPU backend and driver behavior

Only preload the active sound pack. Keep inactive packs on disk.

---

# Publishing configuration

Start conservatively:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0</TargetFramework>

  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>

  <PublishSingleFile>true</PublishSingleFile>
  <PublishReadyToRun>true</PublishReadyToRun>

  <PublishTrimmed>false</PublishTrimmed>
  <PublishAot>false</PublishAot>

  <InvariantGlobalization>false</InvariantGlobalization>
  <DebugType>embedded</DebugType>
</PropertyGroup>
```

Do not enable trimming or Native AOT in the initial release. Avalonia, serializers, bindings, reflection, and third-party libraries must all be validated before aggressive trimming. A smaller binary is not worth intermittent runtime failures.

After stability is proven:

1. Add trim annotations.
2. Enable partial trimming in CI.
3. Run complete startup, navigation, profile import, and audio tests.
4. Evaluate Native AOT separately.
5. Compare startup, memory, binary size, and compatibility.

---

# Build sequence

## Phase 1: technical proof

Build only:

* Hidden Win32 Raw Input window
* Key-down event stream
* Persistent WASAPI output
* One cached WAV sound
* Tray enable/disable
* Latency measurement

This validates the two largest risks.

## Phase 2: MVP

Add:

* Multiple sample variations
* Key groups
* JSON profiles
* Output-device selection
* Master volume
* Auto-repeat control
* Basic Avalonia settings window
* Crash-safe settings

## Phase 3: production hardening

Add:

* Audio-device recovery
* Keyboard connect/disconnect handling
* Logging
* Installer and code signing
* Update mechanism
* Sound-pack validation
* Privacy documentation
* Performance telemetry that does not capture typed keys

## Phase 4: product differentiation

Add:

* Per-device profiles
* Foreground-application rules
* Key-up sounds
* Procedural layers
* Visual keyboard mapping
* Cross-platform implementations

---

# Final technical decision

Use this exact baseline:

```text
C# / .NET 10
Avalonia UI
CommunityToolkit.Mvvm
Dedicated hidden Win32 message window
Raw Input through LibraryImport/PInvoke
System.Threading.Channels
NAudio WasapiOut in shared event-driven mode
Persistent 48 kHz float mixer
Predecoded in-memory PCM samples
System.Text.Json
Self-contained win-x64 deployment
```

The most important architectural rule is:

> Avalonia owns the interface; it must not own the timing-critical keyboard or audio pipeline.

That separation preserves the primary benefits of Avalonia while preventing UI lifecycle, rendering, or cross-platform abstractions from degrading keyboard capture and audio stability. **Confidence: high.**

[1]: https://docs.avaloniaui.net/docs/app-development/native-interop?utm_source=chatgpt.com "Native platform interop"
[2]: https://docs.avaloniaui.net/controls/navigation/trayicon?utm_source=chatgpt.com "TrayIcon"
[3]: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerrawinputdevices?utm_source=chatgpt.com "RegisterRawInputDevices function (winuser.h) - Win32 apps"
[4]: https://learn.microsoft.com/ko-kr/windows/win32/inputdev/using-raw-input?utm_source=chatgpt.com "원시 입력 사용 - Win32 apps"
[5]: https://github.com/naudio/NAudio/blob/master/Docs/WasapiOut.md?utm_source=chatgpt.com "NAudio/Docs/WasapiOut.md at master"
