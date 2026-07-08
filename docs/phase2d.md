You are working inside an existing Avalonia + C# Windows keyboard-sound application.

Phase 1, lifecycle hardening, Phase 2 runtime foundations, Phase 2B sound-pack infrastructure, and Phase 2C profile management/editor UI are complete.

## Current implemented state

The application already includes:

* Avalonia desktop UI
* Dashboard and Profiles navigation
* Dedicated hidden Win32 Raw Input window
* Global background keyboard input
* Bounded event channel
* Deterministic input and application shutdown
* Persistent NAudio WASAPI output
* 48 kHz stereo floating-point mixer
* Bounded 32-voice playback
* Key-down and key-up routing
* Suppressed, every-event, and throttled repeat modes
* Physical scan-code key grouping
* Immutable compiled profile snapshots
* Per-trigger gain, pitch, and volume variation
* Versioned sound-pack manifests
* Strict pack validation
* Transactional folder import
* Immutable pack catalog
* Pack-aware decoded-sample cache
* Constant-time playback lookup
* Safe prepare → verify → activate → persist → release profile switching
* Preview through the same cache and mixer
* Profile CRUD
* Deep-copy editable sessions
* Dirty tracking and revert
* Structured validation
* Key-group editors
* Sparse per-key overrides
* One-key capture
* Profile import/export
* Backup recovery
* Batched UI counter updates
* Release build with 0 warnings/errors
* 59 passing tests
* Clean startup and shutdown
* Approximate Release working set of 116–117 MiB
* No keyboard-data logging

Do not recreate, replace, or weaken these systems.

Your task is to implement:

# Phase 2D — Audio output-device selection and resilient switching

## Objective

After this slice, the user must be able to:

1. View available Windows audio render devices.
2. Select the system-default output.
3. Select a specific output device.
4. Switch devices without restarting the application.
5. Preserve the current working output when a requested switch fails.
6. Fall back safely when the active device disappears.
7. Persist the user’s selected output-device preference.
8. Distinguish configured preference from temporary runtime fallback.
9. Test sound and preview through the selected output.
10. See clear audio-device status and failures.
11. Change a safe latency setting where supported.
12. Preserve deterministic shutdown during enumeration or switching.

Do not implement keyboard-device assignment, installer, updater, tray behavior, procedural synthesis, exclusive-mode audio, or cross-platform audio.

# First action: inspect the repository

Before editing:

1. Inspect the current `WasapiAudioEngine`.
2. Inspect its synchronization and lifecycle state.
3. Inspect how the mixer and active voices are owned.
4. Inspect the preview service.
5. Inspect application settings and persistence.
6. Inspect application composition.
7. Inspect current audio status in the dashboard.
8. Inspect shutdown ordering.
9. Inspect all 59 tests.
10. Identify every direct dependency on the existing audio engine constructor and initialization API.

Preserve:

* persistent mixer architecture,
* bounded voice count,
* cached PCM samples,
* no disk access on playback,
* no output creation per trigger,
* thread-safe playback,
* deterministic shutdown,
* current privacy guarantees.

# Scope

Implement:

```text
Audio render-device enumeration
Default-device abstraction
Specific-device selection
Serialized device switching
Safe working-output preservation
Device-loss detection
Single fallback attempt
Persistent configured preference
Runtime effective-device state
Latency setting
Audio settings UI
Test sound
Tests
```

Do not implement:

```text
WASAPI exclusive mode
ASIO
Per-application outputs
Multiple simultaneous outputs
Spatial audio
Bluetooth-specific tuning
Automatic device-policy rules
Cross-platform audio backend
```

# Domain model

Create a stable application-level audio-device model.

Recommended:

```csharp
public sealed record AudioOutputDevice(
    string Id,
    string DisplayName,
    bool IsSystemDefault,
    AudioDeviceAvailability Availability);
```

```csharp
public enum AudioDeviceAvailability
{
    Active,
    Disabled,
    Unplugged,
    NotPresent,
    Unknown
}
```

Create an explicit system-default selection value.

Do not overload a null device ID ambiguously throughout the application.

Suggested:

```csharp
public readonly record struct AudioOutputSelection(
    AudioOutputSelectionKind Kind,
    string? DeviceId);
```

```csharp
public enum AudioOutputSelectionKind
{
    SystemDefault,
    SpecificDevice
}
```

Validation must ensure a specific selection has a nonempty device ID.

# Configured versus effective output

Maintain two distinct concepts.

## Configured selection

The user’s persisted preference:

```text
System default
or
Specific device ID
```

## Effective runtime device

The actual currently initialized endpoint.

For example:

```text
Configured: USB DAC
Effective: Speakers (system default)
Fallback active: true
```

This distinction is mandatory.

Do not overwrite the user’s configured device preference merely because temporary fallback was required.

# Audio-device service

Create a Windows-specific service responsible for endpoint enumeration and resolution.

Suggested contract:

```csharp
public interface IAudioOutputDeviceService
{
    Task<IReadOnlyList<AudioOutputDevice>> GetDevicesAsync(
        CancellationToken cancellationToken);

    Task<AudioOutputResolution> ResolveAsync(
        AudioOutputSelection selection,
        CancellationToken cancellationToken);

    event EventHandler? DevicesChanged;
}
```

Suggested resolution:

```csharp
public sealed record AudioOutputResolution(
    bool Success,
    string? EffectiveDeviceId,
    string? EffectiveDisplayName,
    bool IsFallback,
    string? ErrorCode,
    string? ErrorMessage);
```

Use NAudio/Core Audio endpoint APIs already available through NAudio where practical.

Do not expose NAudio endpoint objects outside the Windows/audio infrastructure layer.

# Device enumeration

Enumerate Windows render endpoints.

At minimum expose:

* active endpoints,
* stable endpoint ID,
* friendly display name,
* current system-default marker.

Optionally include disabled, unplugged, or not-present endpoints if NAudio exposes them reliably, but the normal UI should prioritize active devices.

Requirements:

* malformed endpoint metadata must not crash enumeration,
* duplicate IDs must not produce duplicate UI entries,
* ordering should be deterministic,
* system-default selection appears as a distinct first option,
* enumeration must not initialize playback devices,
* enumeration should not block the UI thread,
* cancellation must be respected where practical.

Recommended ordering:

```text
System Default
Current default endpoint
Other active endpoints alphabetically
Unavailable configured endpoint, if needed for status
```

# Application settings

Extend the persistent settings model.

Recommended fields:

```csharp
public AudioOutputSelection AudioOutputSelection { get; set; }
    = AudioOutputSelection.SystemDefault;

public int AudioLatencyMilliseconds { get; set; } = 20;
```

If an older nullable device-ID property already exists:

* migrate it safely,
* preserve current behavior,
* back up before migration,
* do not repeatedly migrate.

Recommended latency range:

```text
10–100 milliseconds
```

Default:

```text
20 milliseconds
```

Do not expose values that the current implementation cannot initialize reliably.

# Audio engine refactor

Refactor the audio engine to support output replacement without replacing:

* decoded sample cache,
* runtime profiles,
* Raw Input,
* coordinator,
* persistent logical mixer where feasible.

Preferred architecture:

```text
SoundSampleCache
      |
Persistent logical mixer / voice source
      |
Current WASAPI output instance
```

The mixer should remain stable across a device switch if NAudio permits safe reinitialization with the same provider.

If NAudio does not permit the provider to be attached safely to overlapping outputs, create a controlled swap strategy with a brief playback pause.

Do not rebuild the sample cache during an output-device switch.

# Device-switch operation

Implement one serialized switch operation.

Suggested API:

```csharp
Task<AudioDeviceSwitchResult> SwitchOutputAsync(
    AudioOutputSelection selection,
    int latencyMilliseconds,
    CancellationToken cancellationToken);
```

Suggested result:

```csharp
public sealed record AudioDeviceSwitchResult(
    bool Success,
    AudioOutputSelection RequestedSelection,
    string? EffectiveDeviceId,
    string? EffectiveDeviceName,
    bool IsFallback,
    string? ErrorCode,
    string? ErrorMessage);
```

## Required switch sequence

Preferred sequence:

```text
Validate request
    ↓
Resolve requested endpoint
    ↓
Create candidate output
    ↓
Initialize candidate completely
    ↓
Start candidate
    ↓
Atomically publish candidate as current output
    ↓
Prevent submissions to old output path
    ↓
Stop and dispose old output
    ↓
Persist configured selection
    ↓
Publish effective-device status
```

The exact sequence may need adjustment because one sample provider generally cannot feed two outputs concurrently.

If overlap is unsafe, use:

```text
Pause playback submission
    ↓
Stop old output
    ↓
Initialize candidate
    ↓
On success: activate candidate
On failure: reinitialize previous output
    ↓
Resume playback
```

The implementation must prioritize correctness over seamless transition.

# Failure preservation

A failed switch must not leave audio permanently unavailable when the previous device can still be restored.

Required behavior:

1. Capture enough previous state to restore it.
2. Attempt requested device initialization.
3. If it fails:

   * dispose candidate,
   * restore or retain previous output,
   * keep previous configured setting unchanged,
   * report failure.
4. Persist new preference only after success.
5. Do not claim success if rollback failed.
6. Distinguish:

   * requested-device failure with successful rollback,
   * requested-device failure with rollback failure.

Suggested error codes:

```text
DeviceNotFound
DeviceUnavailable
InitializationFailed
PlaybackStartFailed
RollbackFailed
SwitchCancelled
ShutdownInProgress
```

# Playback behavior during switching

During an active switch:

* new trigger submissions may be temporarily suppressed,
* no trigger may access a disposed output,
* active voice bookkeeping must remain consistent,
* no unbounded trigger queue should accumulate,
* no disk access should occur,
* preview commands should be gated or fail with a controlled busy result.

A brief loss of click sounds is preferable to a race or crash.

Expose aggregate suppressed-during-switch diagnostics if useful, but do not log key identity.

# Device-loss detection

Subscribe to Windows endpoint notifications through NAudio/Core Audio APIs where practical.

Detect at least:

* active endpoint removed,
* endpoint state changed away from active,
* system-default endpoint changed.

Requirements:

* callback must remain lightweight,
* no long-running switch logic inside the native/COM callback,
* enqueue or schedule reconciliation,
* coalesce rapid notifications,
* avoid infinite retry loops,
* remain safe during shutdown.

# Specific-device loss

When the configured specific device disappears:

1. mark configured device unavailable,
2. attempt one runtime fallback to system default,
3. retain configured specific-device preference,
4. mark fallback active,
5. update the UI,
6. do not repeatedly retry on every notification.

When the configured device becomes available again:

* automatic return is optional in this slice,
* do not automatically switch back unless implemented deliberately,
* provide a `Retry configured device` command.

Recommended behavior for this slice:

```text
Fallback once
Remain on fallback
User explicitly retries configured device
```

# System-default selection behavior

When configured selection is `SystemDefault`:

* follow the current system-default render device,
* if Windows changes the default endpoint, schedule a serialized switch,
* do not change persisted settings,
* update effective endpoint status.

Coalesce multiple rapid default-device notifications.

# No-device behavior

If no usable endpoint exists:

* audio enters a controlled unavailable state,
* keyboard input remains active,
* routing remains safe,
* playback submissions are suppressed,
* UI remains operational,
* device refresh and retry commands remain available,
* shutdown remains clean.

Do not crash application startup because no output device exists.

# Latency setting

Expose a conservative shared-mode latency setting.

Allowed UI range:

```text
10–100 ms
```

Recommended selectable values:

```text
10
15
20
30
40
50
75
100
```

Changing latency should use the same serialized reinitialization path as device switching.

Requirements:

* validate range,
* persist only after successful reinitialization,
* preserve previous latency on failure,
* show that lower latency may be less stable,
* do not implement exclusive mode.

# Thread safety and lifecycle

Define one coherent audio lifecycle state.

Suggested:

```csharp
public enum AudioEngineState
{
    Uninitialized,
    Initializing,
    Running,
    Switching,
    Unavailable,
    Stopping,
    Stopped,
    Faulted
}
```

Expose immutable status snapshots.

Example:

```csharp
public sealed record AudioEngineStatus(
    AudioEngineState State,
    AudioOutputSelection ConfiguredSelection,
    string? EffectiveDeviceId,
    string? EffectiveDeviceName,
    bool IsFallbackActive,
    int LatencyMilliseconds,
    int ActiveVoiceCount,
    string? ErrorCode,
    string? ErrorMessage);
```

Requirements:

* status reads are thread-safe,
* switch operations are serialized,
* initialization and switching cannot race shutdown,
* concurrent callers must await the same switch where requests are equivalent or be serialized deterministically,
* shutdown cancels pending enumeration and switching,
* no monitor lock is held across awaited I/O,
* output is disposed exactly once.

# Startup behavior

At application startup:

1. load settings,
2. enumerate or resolve configured selection,
3. initialize configured device,
4. if configured specific device fails:

   * attempt system-default fallback once,
   * retain configured preference,
5. if no device succeeds:

   * continue in unavailable state,
6. expose status to UI.

Do not delay the main window unnecessarily while waiting for long device operations.

The application may show the UI with audio status `Initializing`.

# Shutdown behavior

During shutdown:

1. reject new device refreshes and switches,
2. cancel pending reconciliation,
3. await active switch operation,
4. unsubscribe endpoint notifications,
5. suppress new playback,
6. stop and dispose output,
7. preserve existing sample-cache shutdown ordering,
8. complete without orphan COM callbacks or tasks.

Maintain all previous immediate-close guarantees.

# Audio settings UI

Add or complete an `Audio` navigation section.

Display:

* configured output selection,
* effective runtime device,
* fallback status,
* endpoint availability,
* latency,
* engine state,
* active voice count,
* maximum voice count,
* loaded sample count,
* decoded sample memory,
* last device error.

Controls:

* output-device combo box,
* refresh devices,
* apply device,
* latency selector,
* test sound,
* retry configured device,
* use system default.

The UI must clearly distinguish:

```text
Configured output
Currently active output
```

Example:

```text
Configured: USB DAC
Currently active: Speakers
Fallback active
```

# UI command behavior

Use asynchronous commands.

Gate:

* refresh,
* apply switch,
* retry,
* test sound,
* latency change.

Requirements:

* prevent double switch,
* show switching progress,
* keep previous selection visible after failure,
* do not update saved selection optimistically,
* show actionable errors,
* allow cancellation where practical,
* remain responsive.

# Test sound

Use the existing preview/cache/audio path.

Requirements:

* use the fallback or active profile test sample,
* do not read from disk if already prepared,
* prepare through existing cache if required,
* respect current voice limit,
* reject while switching or unavailable,
* return controlled result,
* do not create a separate audio engine.

# Dashboard integration

Update dashboard audio summary to show:

* current effective output name,
* fallback indicator,
* engine state,
* current latency,
* audio unavailable warning where applicable.

Keep detailed controls on the Audio page.

# Migration

If settings currently store:

```csharp
string? AudioOutputDeviceId
```

migrate:

```text
null or empty → SystemDefault
nonempty → SpecificDevice(existing ID)
```

Requirements:

* create backup,
* preserve valid latency or apply default,
* increment schema version,
* migration is idempotent,
* failed migration falls back safely without destroying the old file.

# Logging

Allowed:

* endpoint ID,
* endpoint display name,
* device availability,
* configured/effective selection,
* switch start/result,
* fallback activation,
* initialization error,
* aggregate suppressed trigger count.

Do not log:

* keyboard values,
* scan codes,
* key timings,
* key identities,
* typed text.

Endpoint identifiers are not keyboard data.

# Testing requirements

Expand tests substantially.

## Selection model

Test:

1. system-default selection valid,
2. specific selection requires ID,
3. equality and persistence behavior,
4. migration from nullable device ID.

## Enumeration

Using a fake endpoint provider, test:

1. active devices returned,
2. deterministic ordering,
3. duplicate IDs removed,
4. system-default marker correct,
5. malformed metadata isolated,
6. cancellation handled,
7. empty device list supported.

## Startup initialization

Test:

1. configured default initializes,
2. configured specific device initializes,
3. missing specific device falls back to default,
4. failed specific device does not overwrite preference,
5. no available device enters unavailable state,
6. startup remains operational without audio.

## Successful switch

Test:

1. requested device resolves,
2. candidate initializes,
3. candidate becomes effective,
4. old output disposed once,
5. configured preference persists after success,
6. status updates correctly,
7. playback resumes.

## Failed switch and rollback

Test:

1. candidate initialization fails,
2. previous output retained or restored,
3. configured preference unchanged,
4. effective device unchanged,
5. failure result returned,
6. rollback failure separately reported,
7. no disposed output remains published.

## Concurrent switching

Test:

1. switches serialize,
2. duplicate apply is gated,
3. shutdown cancels pending switch,
4. playback cannot race output disposal,
5. preview during switch returns controlled busy result,
6. latency and device changes use the same gate.

## Device loss

Test:

1. active specific endpoint loss detected,
2. one fallback attempt occurs,
3. preference remains specific device,
4. fallback state is published,
5. repeated notifications do not create retry loop,
6. retry configured device succeeds when available,
7. retry failure preserves fallback.

## System-default changes

Test:

1. default-selection mode follows new default endpoint,
2. rapid notifications coalesce,
3. persisted setting remains system default,
4. failed default switch enters controlled state or preserves prior output.

## Latency

Test:

1. valid latency accepted,
2. invalid latency rejected,
3. successful latency change persists,
4. failed latency change restores prior latency,
5. latency switch serialized with device switch.

## No-device behavior

Test:

1. playback suppressed safely,
2. input/routing does not crash,
3. refresh can discover a later device,
4. retry initializes audio,
5. shutdown succeeds.

## Status snapshots

Test:

1. configured and effective device differ during fallback,
2. state transitions are coherent,
3. snapshots are immutable or isolated,
4. error cleared after successful recovery.

## Shutdown

Test shutdown while:

* enumerating,
* initializing,
* switching,
* rolling back,
* handling device notification,
* preparing test sound.

Confirm:

* no deadlock,
* output disposed once,
* notification subscription removed,
* all callers observe completion.

# Manual verification

After implementation, manually verify:

1. Audio page opens.
2. System Default appears as a selectable option.
3. Active Windows output appears.
4. Other active endpoints appear.
5. Selecting another output works.
6. Test sound plays through selected output.
7. Profile preview uses selected output.
8. Keyboard sounds use selected output.
9. Selection persists after restart.
10. Changing latency succeeds.
11. Invalid or unsupported change preserves old output.
12. Disconnect selected USB/Bluetooth device.
13. Fallback activates once.
14. Configured preference remains visible.
15. Retry works after reconnect.
16. System-default mode follows Windows default-device change.
17. No-device state leaves application usable.
18. Refresh finds newly connected device.
19. Close during switch exits cleanly.
20. Close during enumeration exits cleanly.
21. Five immediate-close cycles pass.
22. No orphan process remains.
23. No keyboard data appears in logs.

Hardware-dependent checks must be clearly reported if they cannot be automated.

# Performance requirements

Preserve:

* current 48 kHz stereo float mixer,
* no sample redecoding during device switch,
* no file access during ordinary playback,
* bounded voice count,
* no unbounded device-notification tasks,
* no UI dispatch per trigger,
* working-set target near the current baseline.

Measure and report:

* enumeration duration,
* successful switch duration,
* failed switch plus rollback duration,
* working-set change after Audio page use,
* whether active voices are intentionally dropped during switching.

# Acceptance criteria

This slice is complete only when:

* Windows render devices can be listed,
* system-default and specific-device selections exist,
* configured and effective devices are modeled separately,
* output can switch without application restart,
* failed switch preserves or restores previous audio where possible,
* settings persist only after successful switch,
* device loss triggers at most one fallback attempt,
* configured preference survives temporary fallback,
* system-default mode follows Windows default changes,
* no-device startup remains nonfatal,
* latency is validated and safely reinitialized,
* test sound uses existing cache and mixer,
* switching cannot race playback or shutdown,
* endpoint notifications are unsubscribed on shutdown,
* Release build succeeds with zero introduced warnings,
* all tests pass,
* no keyboard data is logged.

# Deliverables

When complete, provide:

1. Audio-device architecture summary.
2. Configured-versus-effective selection design.
3. Device enumeration behavior.
4. Switching transaction and rollback behavior.
5. Device-loss fallback behavior.
6. System-default change behavior.
7. Latency behavior.
8. Audio lifecycle state model.
9. UI additions.
10. Settings migration.
11. Files added.
12. Files modified.
13. Packages added or changed.
14. Test count and results.
15. Release build result.
16. Manual verification results.
17. Enumeration and switching timings.
18. Working-set measurement.
19. Known limitations.
20. Remaining Phase 2 work.

Start by inspecting the existing audio engine and all call sites. Introduce the device-selection model and endpoint abstraction first, then lifecycle-safe switching, then persistence and UI, then endpoint notifications and recovery.
