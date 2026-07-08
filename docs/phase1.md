You are working inside an existing Avalonia + C# desktop application repository.

The project folder already exists, Avalonia is already installed, and the application has been built and launched successfully. Do not recreate the solution or replace the existing Avalonia setup.

Your task is to implement **Phase 1: Technical Proof of Concept** for a Windows keyboard-sound application.

## Objective

Create a minimal but structurally sound Windows-only proof of concept that:

1. Runs as an Avalonia desktop application.
2. Captures global physical keyboard key-down events using the Windows Raw Input API.
3. Continues receiving keyboard input when the Avalonia window is not focused.
4. Plays one preloaded WAV sound on each physical key-down.
5. Keeps one persistent WASAPI audio output open.
6. Provides a basic UI control to enable or disable keyboard sounds.
7. Avoids recording, storing, or displaying typed text.
8. Is designed so the Windows input and audio implementations can later be replaced by platform-specific implementations.

Do not implement profiles, sound packs, per-key mappings, procedural synthesis, application-specific rules, installers, auto-start, cloud functionality, telemetry, or cross-platform input.

## Required technology

Use:

* Existing Avalonia UI project
* C#
* Current .NET target already configured in the repository
* Win32 Raw Input through source-generated P/Invoke using `LibraryImport` where practical
* A dedicated hidden Win32 window for `WM_INPUT`
* `System.Threading.Channels`
* NAudio
* `WasapiOut`
* WASAPI shared mode
* Event-driven audio output
* One persistent 48 kHz stereo floating-point mixer
* Predecoded WAV data held in memory
* MVVM-compatible separation between UI and services

Do not use:

* `WH_KEYBOARD_LL` as the primary input mechanism
* Avalonia keyboard events for global input
* A kernel driver
* Electron
* Native AOT
* aggressive trimming
* a new audio output instance per keystroke
* disk reads during a keypress
* unbounded event queues
* logging of key sequences or typed text

## First action: inspect the repository

Before changing files:

1. Inspect the solution and project structure.
2. Identify the Avalonia application project.
3. Inspect its `.csproj`, `Program.cs`, `App.axaml`, `App.axaml.cs`, and main window files.
4. Determine the existing namespace and naming conventions.
5. Preserve the repository’s current structure where reasonable.
6. Do not create duplicate application entry points.
7. Do not change the target framework unless required for compatibility.
8. Report any significant conflict between the existing project and this specification before working around it.

Then implement the feature directly in the existing project or add focused class-library projects only when the current repository structure clearly benefits from them. Avoid unnecessary project proliferation for this proof of concept.

## Architecture

Use these logical boundaries, adapted to the existing repository:

```text
Avalonia UI
    |
    v
Application coordinator
    |
    +--> IKeyboardInputSource
    |        |
    |        +--> WindowsRawInputSource
    |
    +--> IAudioEngine
             |
             +--> WasapiAudioEngine
```

Suggested folders:

```text
Core/
    Input/
    Audio/

Application/
    KeyboardSoundCoordinator.cs

Platform/Windows/
    Input/
        WindowsRawInputSource.cs
        RawInputMessageWindow.cs
        RawInputParser.cs
        NativeMethods.cs
        NativeStructures.cs

Audio/
    WasapiAudioEngine.cs
    CachedSound.cs
    CachedSoundSampleProvider.cs

ViewModels/
    MainWindowViewModel.cs
```

Adapt names to the repository’s existing conventions.

## Core contracts

Create a normalized input event that contains only technical key-event metadata.

Example:

```csharp
public enum KeyboardAction
{
    KeyDown,
    KeyUp
}

public readonly record struct KeyboardInputEvent(
    ushort ScanCode,
    ushort VirtualKey,
    KeyboardAction Action,
    nint DeviceHandle,
    bool IsExtended,
    long Timestamp);
```

Create an input abstraction similar to:

```csharp
public interface IKeyboardInputSource : IAsyncDisposable
{
    event EventHandler<KeyboardInputEvent>? InputReceived;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
```

Create an audio abstraction similar to:

```csharp
public interface IAudioEngine : IAsyncDisposable
{
    bool IsInitialized { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    void PlayClick();
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
```

Exact signatures may be improved, but keep the contracts small and platform-independent.

## Windows Raw Input requirements

Implement keyboard capture using a dedicated hidden Win32 window.

The hidden native window must:

1. Register its own Win32 window class.
2. Create a hidden or message-processing window.
3. own a stable `HWND` independent from the Avalonia main window.
4. call `RegisterRawInputDevices`.
5. register usage page `0x01`.
6. register keyboard usage `0x06`.
7. use `RIDEV_INPUTSINK`.
8. receive and process `WM_INPUT`.
9. call `GetRawInputData`.
10. normalize `RAWKEYBOARD` data into `KeyboardInputEvent`.
11. preserve the source device handle from `RAWINPUTHEADER.hDevice`.
12. distinguish key-down and key-up.
13. identify extended keys where possible.
14. dispose the native window and unregister resources cleanly.

Do not subclass the Avalonia window procedure.

Run the native message loop on a dedicated background thread. The thread must have a deterministic startup and shutdown mechanism.

Catch native startup failures and expose them to the application in a controlled manner. Do not silently swallow Win32 errors.

Use `Marshal.GetLastWin32Error()` or equivalent error retrieval and throw meaningful exceptions containing the affected Win32 operation.

## Input pipeline

The Win32 callback must remain minimal.

Its responsibilities are limited to:

1. Read raw input.
2. Parse and normalize it.
3. attempt to enqueue it.
4. Return immediately.

Create a bounded channel:

```csharp
new BoundedChannelOptions(256)
{
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleReader = true,
    SingleWriter = false
}
```

A single consumer should process normalized events.

Only trigger audio for genuine key-down events.

For Phase 1:

* Ignore key-up events for playback.
* Suppress Windows key auto-repeat when practical.
* A held key should not generate an uncontrolled stream of click sounds.
* Maintain a set of currently pressed physical keys, preferably keyed by device handle plus scan code.
* Remove the key from the pressed set on key-up.
* Trigger a sound only on the transition from not-pressed to pressed.

Account for application-generated or injected events only if Raw Input exposes sufficient information. Do not introduce a low-level hook merely to detect injection.

## Audio requirements

Add NAudio through the project’s package management mechanism.

Implement one persistent audio engine with:

* `WasapiOut`
* shared mode
* event synchronization enabled
* a target latency starting around 15–20 milliseconds
* 48,000 Hz
* stereo
* 32-bit IEEE floating point
* one persistent `MixingSampleProvider`
* `ReadFully = true`

Initialize the audio output once during application startup and keep it running.

Do not create a new `WasapiOut`, audio device, file reader, or output stream for each keypress.

## Audio sample

Look for an existing suitable WAV asset in the repository.

If none exists:

1. Create an `Assets/Audio` folder.
2. Add a clearly named placeholder path such as `Assets/Audio/key-click.wav`.
3. If binary asset generation is practical in the environment, generate a very short original click WAV programmatically.
4. Otherwise implement the expected asset path and clearly report that the WAV file must be supplied manually.

Do not download copyrighted sound assets.

The application must fail gracefully when the audio asset is missing:

* The UI should remain operational.
* The error should be visible in application status.
* Keyboard input should not crash the process.
* Repeated keypresses must not generate repeated exception storms.

Load and decode the WAV once during audio initialization.

Store the decoded sample in memory in the mixer’s target format.

Every playback call should create only a lightweight playback cursor or voice over the cached PCM data.

## Voice handling

For Phase 1, implement a bounded active voice strategy.

Requirements:

* Permit overlapping click sounds during fast typing.
* Set a reasonable maximum, such as 32 simultaneous voices.
* When the limit is reached, discard the oldest completed or active voice rather than allocate without bounds.
* Remove completed mixer inputs.
* Avoid holding file streams open after the sample has been decoded.
* Avoid avoidable allocations on the Raw Input callback path.

A completely custom lock-free mixer is not required in Phase 1. Prefer correctness and stability over premature DSP optimization.

## Coordinator

Create a coordinator service responsible for connecting input to audio.

Responsibilities:

* Start the audio engine.
* Start the keyboard input source.
* Consume the bounded keyboard event channel.
* Track pressed keys to suppress auto-repeat.
* Check the global enabled state.
* Call `PlayClick()` only for eligible key-down transitions.
* Stop services in reverse order.
* Handle cancellation.
* Prevent duplicate startup and shutdown.
* Expose status and failure information to the view model.

The coordinator must not depend directly on Avalonia controls.

## UI requirements

Keep the UI minimal.

The main Avalonia window should provide:

* Application title
* Enable/disable toggle
* Input status:

  * Starting
  * Active
  * Stopped
  * Error
* Audio status:

  * Starting
  * Active
  * Missing sample
  * Device error
  * Stopped
* A compact count of click triggers for technical validation
* A short privacy statement:

  * key events are processed transiently
  * typed text is not stored
  * no keyboard data is transmitted

Do not display the actual key pressed.

The enabled toggle should affect sound playback immediately without stopping the native input listener.

Use bindings and a view model. Do not put input or audio logic in code-behind.

The UI does not need final styling, but it should be clean, aligned, and usable.

## Application lifecycle

At application startup:

1. Construct services.
2. Construct the coordinator.
3. Construct the main window view model.
4. Show the main Avalonia window.
5. Initialize audio.
6. Start Raw Input.
7. Update status properties.

At application shutdown:

1. Stop event consumption.
2. Stop Raw Input.
3. destroy the hidden Win32 window.
4. Stop and dispose WASAPI output.
5. Dispose cached audio resources.
6. Complete shutdown without hanging the Avalonia UI thread.

Do not block the UI thread with `.Wait()` or `.Result`.

Use asynchronous lifecycle handling where appropriate.

For Phase 1, closing the main window may exit the application. A tray icon is optional and should not distract from validating input and audio.

## Thread safety

Assume that:

* Raw Input events arrive from the native message-loop thread.
* Channel processing occurs on a background task.
* Audio callbacks occur on an audio thread.
* Avalonia properties must be updated on the UI thread.

Marshal UI-facing property changes through Avalonia’s dispatcher.

Do not access Avalonia controls from platform or audio services.

Do not hold long-running locks in the input or audio paths.

## Error handling

Handle at least:

* Raw Input registration failure
* Native window creation failure
* malformed or unexpected raw input data
* missing WAV asset
* unsupported WAV format
* no default audio output device
* WASAPI initialization failure
* audio device invalidation during runtime
* service startup called twice
* service shutdown called twice

For device invalidation, a complete automatic recovery system is not required in Phase 1, but the application must:

* stop trying to play,
* update status to an error state,
* remain open,
* allow a clean shutdown.

## Logging

Use lightweight logging.

Acceptable options:

* `Microsoft.Extensions.Logging`
* a small internal logger abstraction
* debug output plus structured UI status for this proof

Do not log:

* sequences of scan codes
* virtual keys associated with user text
* typed characters
* passwords
* key timing histories

It is acceptable to log:

* service startup and shutdown
* counts
* native API failures
* audio-device failures
* whether an event was key-down or key-up only when needed during development

Production-facing logging should remain privacy-preserving.

## Tests

Add focused automated tests for platform-independent logic.

At minimum test:

1. A key-down triggers one click.
2. Repeated key-down without key-up does not trigger another click.
3. Key-up clears pressed state.
4. A subsequent key-down after key-up triggers again.
5. Disabled state prevents playback.
6. Different device handles with the same scan code are tracked independently.
7. Shutdown is idempotent.
8. The bounded event-processing design does not create an unbounded collection.

Do not attempt to unit-test actual WASAPI playback or the Win32 message loop as ordinary unit tests.

Keep Raw Input parsing isolated enough to test with constructed native-data equivalents where practical.

## Code quality

Requirements:

* Nullable reference types enabled or respected.
* No unexplained magic numbers; native constants should be named.
* Native structs must use correct layouts and field sizes for x64.
* Dispose unmanaged and audio resources deterministically.
* Use cancellation tokens where they materially apply.
* Avoid static mutable global state.
* Add concise comments only around non-obvious Win32 behavior.
* Preserve the existing repository style.
* Do not add unnecessary frameworks.
* Do not create abstractions that have only speculative value.
* Build with zero compiler errors.
* Resolve warnings introduced by this implementation where practical.

## Verification

After implementation:

1. Restore packages.
2. Build the complete solution in Release configuration.
3. Run automated tests.
4. Launch the Avalonia application.
5. Verify the main window opens.
6. Verify status reports successful input initialization.
7. Verify status reports successful audio initialization.
8. Put another application in focus.
9. Press a key once.
10. Confirm one click sound plays.
11. Hold the key.
12. Confirm auto-repeat does not create an uncontrolled click stream.
13. Release and press the key again.
14. Confirm another click plays.
15. Disable sound in the Avalonia UI.
16. Confirm keyboard events remain active but no sound plays.
17. Re-enable sound.
18. Confirm playback resumes.
19. Close the application.
20. Confirm the process exits cleanly without leaving a hidden native thread running.

If interactive audio verification is impossible in the execution environment, complete all build and test verification and state precisely which runtime checks remain manual.

## Deliverables

When finished, provide:

1. A concise summary of the implemented architecture.
2. A list of files created.
3. A list of files modified.
4. NuGet packages added.
5. Build result.
6. Test result.
7. Any remaining manual setup, especially the WAV asset.
8. Known limitations of Phase 1.
9. Exact commands required to build and run the application.
10. Any architectural deviations from this prompt and their justification.

## Acceptance criteria

Phase 1 is complete only when:

* The existing Avalonia project still builds.
* Global keyboard input uses Raw Input.
* Input continues outside the focused Avalonia window.
* The Raw Input receiver uses a dedicated hidden Win32 window.
* One cached sound plays for physical key-down transitions.
* Auto-repeat is suppressed.
* The audio output is persistent.
* WAV data is not read from disk per keypress.
* Input events use a bounded channel.
* Sound can be enabled and disabled from the UI.
* No typed text is stored or displayed.
* Shutdown releases native and audio resources.
* Platform-independent key-state logic has automated tests.
* The complete solution builds successfully in Release mode.

Start by inspecting the repository. Then implement the smallest complete version that satisfies these criteria.
