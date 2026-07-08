You are working inside an existing Avalonia + C# Windows keyboard-sound application.

Phase 1 and its hardening pass are complete.

The current application already includes:

* Dedicated hidden Win32 Raw Input window
* Background native message loop
* Global keyboard input outside the focused Avalonia window
* Bounded `Channel<T>` event pipeline
* Per-device pressed-key tracking
* Auto-repeat suppression
* Persistent NAudio `WasapiOut`
* WASAPI shared mode
* 48 kHz stereo floating-point mixer
* Preloaded WAV playback
* Maximum 32 simultaneous voices
* MVVM enable/disable control
* Input and audio status display
* Click counter
* Privacy notice
* Graceful failure handling
* Deterministic shutdown
* Raw Input startup/shutdown race fixes
* Thread-safe audio shutdown
* Awaitable and idempotent lifecycle operations
* Raw Input size validation
* Reusable Raw Input buffer
* Deterministic Raw Input and native window-class cleanup
* Passing Release build and tests

Do not recreate or replace the Phase 1 architecture.

Your task is to implement **Phase 2: Profile-Based MVP**.

# Phase 2 objective

Transform the single-click proof of concept into a usable keyboard-sound application that supports:

1. Multiple sound profiles.
2. Multiple sample variations per key group.
3. Key-group routing.
4. Separate key-down and key-up behavior.
5. Configurable held-key repeat behavior.
6. Master volume and per-group volume.
7. Pitch and volume randomization within safe ranges.
8. Audio output-device selection.
9. Persistent application settings.
10. Crash-safe profile and settings storage.
11. Importable local sound packs.
12. Basic sound preview.
13. Runtime profile switching without restarting the application.
14. Device-specific keyboard profile assignment where Raw Input device identity is available.

The result should be a coherent Windows MVP, not a full commercial product.

Do not implement:

* procedural synthesis,
* cloud synchronization,
* account systems,
* online marketplace,
* automatic downloads,
* AI-generated sounds,
* visual full-keyboard editor,
* installer,
* automatic updater,
* macOS or Linux support,
* kernel drivers,
* keyboard firmware integration,
* telemetry,
* advanced DSP effects,
* MIDI,
* per-application foreground-process rules,
* Native AOT,
* aggressive trimming.

# First action: inspect the repository

Before changing files:

1. Inspect the complete solution structure.
2. Identify the existing application, core, platform, audio, and test projects.
3. Review all Phase 1 lifecycle contracts.
4. Review the current coordinator.
5. Review the current audio mixer and cached sample implementation.
6. Review the existing Raw Input normalized event.
7. Review how application state reaches the Avalonia view model.
8. Review all current tests.
9. Preserve naming and namespace conventions.
10. Do not weaken any Phase 1 hardening guarantees.

Report significant architectural conflicts before introducing workarounds.

# Required architecture

Extend the existing architecture with these logical components:

```text
Avalonia UI
    |
    v
Application coordinator
    |
    +--> Active profile service
    |
    +--> Keyboard sound router
    |
    +--> Sound variation selector
    |
    +--> Sound-pack repository
    |
    +--> Settings repository
    |
    +--> Audio device service
    |
    +--> Existing input source
    |
    +--> Existing audio engine
```

Suggested logical structure:

```text
Core/
    Audio/
        SoundId.cs
        SoundTrigger.cs
        AudioOutputDevice.cs
    Input/
        KeyIdentifier.cs
        KeySoundGroup.cs
    Profiles/
        SoundProfile.cs
        KeyGroupMapping.cs
        KeyOverride.cs
        DeviceProfileAssignment.cs
    SoundPacks/
        SoundPackManifest.cs
        SoundSetDefinition.cs
    Settings/
        ApplicationSettings.cs

Application/
    Profiles/
        ActiveProfileService.cs
        ProfileCompiler.cs
        ProfileValidationService.cs
    Routing/
        KeyboardSoundRouter.cs
        KeyGroupResolver.cs
        SoundVariationSelector.cs
    SoundPacks/
        SoundPackService.cs
        SoundPackImportService.cs
    Settings/
        ApplicationSettingsService.cs
    Audio/
        AudioDeviceSelectionService.cs

Infrastructure/
    Persistence/
        JsonApplicationSettingsRepository.cs
        JsonProfileRepository.cs
        JsonSoundPackRepository.cs
        AtomicFileWriter.cs

Audio/
    WasapiAudioEngine.cs
    CachedSound.cs
    SoundSampleCache.cs
    PitchSampleProvider.cs
    VolumeSampleProvider.cs

ViewModels/
    MainWindowViewModel.cs
    ProfilesViewModel.cs
    ProfileEditorViewModel.cs
    SoundPacksViewModel.cs
    AudioSettingsViewModel.cs
```

Adapt this to the current repository. Do not create projects or layers solely to match this outline.

# Data model

## Key groups

Introduce a stable key-group model.

At minimum support:

```csharp
public enum KeySoundGroup
{
    Alpha,
    NumberRow,
    Modifier,
    Spacebar,
    Enter,
    Backspace,
    Tab,
    Navigation,
    Arrow,
    Function,
    Numpad,
    Escape,
    Other
}
```

The grouping logic must use normalized physical-key information, primarily scan codes and extended-key state.

Do not group solely by localized characters.

The same physical key should resolve consistently across keyboard layouts where practical.

Create a dedicated `KeyGroupResolver`.

It must:

* distinguish main Enter from numpad Enter,
* distinguish number row from numpad,
* identify arrow keys,
* identify left and right modifiers where possible,
* handle extended scan codes,
* safely classify unknown keys as `Other`,
* avoid throwing on unknown scan codes.

## Physical key identifier

Create a stable value object for per-key overrides.

Example:

```csharp
public readonly record struct KeyIdentifier(
    ushort ScanCode,
    bool IsExtended);
```

Do not use display text as the persistent key identity.

Device handle must not be serialized directly as a permanent identity because handles are session-specific.

# Sound profile model

Create a serializable profile model.

A profile should include:

```csharp
public sealed class SoundProfile
{
    public required string Id { get; init; }
    public required string Name { get; set; }

    public bool Enabled { get; set; } = true;

    public float MasterVolume { get; set; } = 0.75f;

    public bool PlayOnKeyDown { get; set; } = true;
    public bool PlayOnKeyUp { get; set; }

    public RepeatSoundMode RepeatMode { get; set; }

    public Dictionary<KeySoundGroup, KeyGroupMapping> Groups { get; init; }

    public Dictionary<KeyIdentifier, KeyOverride> KeyOverrides { get; init; }
}
```

Suggested repeat modes:

```csharp
public enum RepeatSoundMode
{
    Suppress,
    PlayEveryRepeat,
    Throttled
}
```

For throttled repeat, support a configurable minimum interval, such as:

```csharp
public int RepeatMinimumIntervalMilliseconds { get; set; } = 80;
```

Validate the interval within a safe range.

## Group mapping

Each group mapping should support:

```csharp
public sealed class KeyGroupMapping
{
    public bool Enabled { get; set; } = true;

    public required string SoundPackId { get; set; }
    public required string KeyDownSoundSetId { get; set; }

    public string? KeyUpSoundSetId { get; set; }

    public float Volume { get; set; } = 1.0f;

    public float PitchVariationSemitones { get; set; }

    public float VolumeVariation { get; set; }

    public VariationSelectionMode VariationMode { get; set; }
}
```

Suggested variation modes:

```csharp
public enum VariationSelectionMode
{
    Random,
    RoundRobin,
    AvoidImmediateRepeat
}
```

`AvoidImmediateRepeat` should be the default.

## Per-key override

A per-key override may:

* disable the key,
* use a different sound set,
* override volume,
* override pitch variation,
* override key-down or key-up mapping.

Keep overrides optional and sparse.

Do not build a full visual keyboard editor in Phase 2. A simple list or technical key-capture assignment interface is sufficient.

# Sound-pack format

Implement a local sound-pack format.

Recommended folder structure:

```text
SoundPacks/
    mechanical-linear/
        manifest.json
        samples/
            alpha/
                alpha-01.wav
                alpha-02.wav
            space/
                space-01.wav
            enter/
                enter-01.wav
```

Recommended manifest:

```json
{
  "schemaVersion": 1,
  "id": "mechanical-linear",
  "name": "Mechanical Linear",
  "author": "Local",
  "description": "Linear mechanical keyboard samples",
  "sampleRate": 48000,
  "soundSets": [
    {
      "id": "alpha-down",
      "name": "Alpha Key Down",
      "files": [
        "samples/alpha/alpha-01.wav",
        "samples/alpha/alpha-02.wav"
      ]
    },
    {
      "id": "space-down",
      "name": "Spacebar Down",
      "files": [
        "samples/space/space-01.wav"
      ]
    }
  ]
}
```

## Sound-pack validation

Validate every imported or loaded pack.

At minimum validate:

* manifest exists,
* schema version is supported,
* pack ID is non-empty and safe,
* sound-set IDs are unique,
* referenced files exist,
* paths remain within the sound-pack directory,
* no absolute paths,
* no `..` path traversal,
* supported file extension,
* file is a valid readable WAV,
* sample count is nonzero,
* sample duration is within a reasonable maximum,
* total decoded memory is within a configured maximum,
* duplicate pack IDs are handled explicitly.

Reject malformed packs without crashing.

Do not trust imported JSON or paths.

## Import behavior

Support importing a local sound-pack folder or archive.

Preferred Phase 2 behavior:

* folder import is mandatory,
* ZIP import is optional.

For folder import:

1. user chooses a source folder,
2. validate the manifest and files,
3. copy into the application-managed sound-pack directory,
4. write to a temporary destination first,
5. atomically rename into place,
6. do not partially install invalid packs,
7. detect an existing pack ID,
8. ask through the view model flow whether to replace or cancel,
9. never overwrite unrelated files.

If ZIP import is implemented:

* prevent Zip Slip path traversal,
* reject symbolic-link-like entries where relevant,
* enforce extracted-size limits,
* validate before final installation.

# Storage locations

Use application-managed local storage.

Recommended root:

```text
%LocalAppData%\KeySounds\
```

Suggested structure:

```text
%LocalAppData%\KeySounds\
    settings.json
    settings.backup.json
    profiles/
        default.json
        custom-profile.json
    soundpacks/
        default/
        imported-pack/
    cache/
```

Do not write user state into the application installation directory.

Provide one service responsible for resolving storage paths.

# Crash-safe persistence

All profile and settings writes must be atomic.

Implement an `AtomicFileWriter` or equivalent.

Required sequence:

1. serialize to a temporary file in the same directory,
2. flush managed buffers,
3. flush file-system buffers where practical,
4. validate that the temporary file can be read back,
5. replace or move the target atomically,
6. retain one last-known-good backup,
7. clean abandoned temporary files during startup.

Do not modify active JSON files in place.

## Corrupt-file recovery

When loading:

1. attempt the main file,
2. if malformed, attempt the backup,
3. if the backup succeeds, restore it or continue with it,
4. if both fail, create safe defaults,
5. surface a nonfatal status message,
6. do not crash the application.

Never silently discard a user profile without reporting recovery.

# Application settings

Create a persistent application settings model.

At minimum include:

```csharp
public sealed class ApplicationSettings
{
    public int SchemaVersion { get; set; } = 1;

    public bool SoundsEnabled { get; set; } = true;

    public string? ActiveProfileId { get; set; }

    public string? AudioOutputDeviceId { get; set; }

    public float GlobalVolume { get; set; } = 1.0f;

    public bool MinimizeOnClose { get; set; }

    public Dictionary<string, string> KeyboardDeviceProfileAssignments { get; set; }
}
```

Do not add auto-start or update settings in Phase 2 unless already present.

# Profile repository

Implement a profile repository supporting:

* list profiles,
* load profile,
* create profile,
* duplicate profile,
* save profile,
* rename profile,
* delete profile,
* restore default profile,
* validate profile,
* import profile JSON,
* export profile JSON.

Deletion rules:

* the active profile cannot disappear without selecting a replacement,
* the built-in fallback profile must remain available,
* profile IDs must be filesystem-safe,
* profile names may be user-friendly and need not equal IDs.

Use immutable or snapshot-style runtime profile data where practical.

The audio-routing path must not traverse mutable UI models.

# Compiled runtime profile

Do not deserialize or navigate complex JSON structures on every keypress.

Create a compiled profile representation.

Example:

```csharp
public sealed class CompiledSoundProfile
{
    public required string Id { get; init; }
    public required float MasterGain { get; init; }
    public required bool PlayOnKeyDown { get; init; }
    public required bool PlayOnKeyUp { get; init; }
    public required RepeatSoundMode RepeatMode { get; init; }

    public required IReadOnlyDictionary<KeySoundGroup, CompiledGroupMapping> Groups { get; init; }

    public required IReadOnlyDictionary<KeyIdentifier, CompiledKeyMapping> Overrides { get; init; }
}
```

When the user changes profile settings:

1. validate the editable model,
2. save it,
3. compile it,
4. atomically replace the active runtime profile reference.

Use a thread-safe profile snapshot.

The input-routing path should observe either the old complete profile or the new complete profile, never a partially edited profile.

# Sound sample cache

Replace the single cached click with a pack-aware cache.

The cache must:

* load all samples needed by the active profile,
* decode each WAV once,
* convert to the mixer format,
* store decoded PCM in memory,
* expose samples through stable IDs,
* unload samples no longer needed,
* avoid blocking the input callback,
* avoid disk reads during playback,
* enforce a decoded-memory limit,
* report missing or invalid samples.

Suggested stable sample identity:

```csharp
public readonly record struct SoundId(
    string PackId,
    string SoundSetId,
    string FileId);
```

## Cache switching

When switching profiles:

1. determine required sound packs and sound sets,
2. load required samples in the background,
3. validate the complete new runtime audio set,
4. only then activate the new profile,
5. preserve the previous profile if loading fails,
6. release old samples only after no active voices reference them.

Do not leave the application in a partially switched state.

# Variation selection

Implement deterministic variation-selection services.

## Random

Select any sample from the set.

Use a thread-safe random source.

## Round robin

Cycle through samples in order.

Maintain independent counters per sound set.

## Avoid immediate repeat

Select a sample different from the previous sample when the set has at least two samples.

For a set with one sample, select it normally.

Variation-selection state must be bounded and cleared when packs are unloaded.

# Pitch variation

Support small pitch variation per trigger.

Store configuration in semitones.

Recommended safe range:

```text
-3.0 to +3.0 semitones maximum configured variation
```

Each trigger selects a random pitch within:

```text
[-configuredVariation, +configuredVariation]
```

Convert semitones to playback-rate ratio:

```text
ratio = 2^(semitones / 12)
```

Use an appropriate NAudio resampling or sample-provider mechanism.

Requirements:

* pitch variation must not change the persistent output sample rate,
* no audio-device reinitialization,
* no disk reads,
* no unbounded allocation,
* reject unsupported extreme pitch values,
* retain correct end-of-voice behavior.

If the existing audio pipeline cannot support pitch safely without destabilization, implement volume variation first and add a clean pitch-provider abstraction with a clearly reported limitation. Do not fake pitch by altering metadata incorrectly.

# Volume model

The final trigger gain should be computed from:

```text
application global volume
× profile master volume
× group volume
× per-key override volume
× random volume variation
```

Clamp the final gain to a safe range.

Recommended model:

```text
0.0 to 1.5 internal gain
```

The UI should ordinarily constrain values to:

```text
0.0 to 1.0
```

Values above 1.0 should only be allowed if the mixer has reliable clipping protection.

If there is no limiter, clamp to 1.0.

## Volume variation

Allow a small random multiplicative variation.

Example configured range:

```text
0.0 to 0.20
```

A value of `0.10` may produce approximately:

```text
0.90 to 1.10 multiplier
```

Clamp after applying variation.

# Key-down, key-up, and repeat routing

Extend the existing pressed-key state logic.

## Key-down

On first transition from released to pressed:

* resolve active profile,
* resolve device-specific profile if assigned,
* resolve per-key override,
* resolve key group,
* select key-down sound,
* submit trigger.

## Key-up

On release:

* clear pressed state,
* optionally resolve key-up sound,
* submit a release trigger if enabled.

## Repeat

When another key-down arrives while the same physical key remains pressed:

### `Suppress`

Do nothing.

### `PlayEveryRepeat`

Play every repeat event.

### `Throttled`

Play only if the configured minimum interval has elapsed since the last repeat sound for that key.

Repeat tracking must use:

```text
device identity + scan code + extended flag
```

Keep the state bounded and remove it on key-up and device removal where available.

# Keyboard device identity

Raw Input device handles are not stable across sessions.

Create a Windows device identity service that resolves a device handle into a more stable descriptor using Raw Input device information.

Where possible capture:

* raw device name,
* vendor ID,
* product ID,
* usage information,
* normalized stable identity string.

Do not claim perfect permanence.

Store profile assignments using a normalized device identity, not the transient handle.

If identity resolution fails:

* use the active global profile,
* do not block input,
* do not throw on every event,
* cache failures temporarily.

Expose known keyboards in the UI.

Allow:

* default global profile,
* optional profile assignment per detected keyboard,
* clearing the assignment.

Do not attempt firmware-specific integrations.

# Audio output-device selection

Create an audio-device service that lists available render devices.

Expose:

* stable device ID,
* display name,
* default-device marker,
* active/unavailable state where available.

The UI should allow selecting:

* system default output,
* a specific output device.

## Switching devices

When output selection changes:

1. stop accepting new playback temporarily,
2. prepare a new WASAPI output instance,
3. initialize it fully,
4. swap to the new output only after successful initialization,
5. dispose the old output,
6. preserve the old output if new-device initialization fails,
7. update persistent settings only after success.

Do not dispose the working output before confirming the new device is usable unless NAudio limitations make this impossible.

If a true overlap-free swap is impractical, implement a short controlled pause and restore the previous device on failure.

All device switches must be serialized.

## Device loss

If the active output device becomes unavailable:

* stop playback safely,
* update audio status,
* attempt fallback to system default once,
* avoid infinite retry loops,
* keep the UI operational,
* persist the user’s explicit device choice separately from temporary fallback state.

# Audio trigger model

Replace a no-argument `PlayClick()` API with a trigger-based API.

Example:

```csharp
public readonly record struct SoundTrigger(
    SoundId SoundId,
    float Gain,
    float PitchRatio,
    float Pan,
    long Sequence);
```

The exact model may differ, but it must support:

* sample identity,
* volume,
* pitch,
* future pan support,
* diagnostic sequence if needed.

Do not include typed characters or textual key values.

# Audio engine requirements

Preserve:

* one persistent mixer,
* bounded active voices,
* no disk reads during playback,
* no output initialization per trigger,
* safe shutdown,
* thread-safe playback submission.

Extend the engine to:

* play arbitrary cached samples,
* apply trigger gain,
* apply pitch variation,
* support preview playback,
* support device switching,
* expose device/error status,
* preserve the 32-voice limit or make it configurable with a safe upper bound.

Preview playback may share the same mixer but should be tagged or separated enough to avoid breaking the voice limit.

# Preview functionality

Provide basic preview controls.

The user should be able to:

* preview a sound set,
* preview individual samples where practical,
* stop repeated preview requests from producing unbounded voices.

Preview must use the same cache and audio engine.

Do not load a file directly from disk on every preview click.

# UI structure

Refactor the Avalonia UI into a usable MVP.

At minimum provide these sections.

## Dashboard

Show:

* global sound enable/disable,
* active profile selector,
* global volume,
* current keyboard device,
* assigned profile for that device,
* active audio output,
* input status,
* audio status,
* loaded sample count,
* decoded sample-memory estimate,
* click/trigger counter,
* privacy notice.

## Profiles

Support:

* list profiles,
* create,
* duplicate,
* rename,
* delete,
* select active,
* save,
* revert unsaved changes,
* import,
* export.

## Profile editor

Support editing:

* profile master volume,
* key-down enabled,
* key-up enabled,
* repeat mode,
* repeat throttle interval,
* each key group,
* sound pack,
* key-down sound set,
* key-up sound set,
* group volume,
* pitch variation,
* volume variation,
* variation selection mode.

Per-key overrides may use a simple technical list.

A suitable Phase 2 flow:

1. click “Capture key,”
2. press one key,
3. display a human-friendly technical label,
4. create or edit the override,
5. never display accumulated typing.

Capture mode must:

* capture exactly one eligible physical key,
* automatically exit,
* not store a key sequence,
* allow cancellation,
* prevent the captured event from causing unintended profile-edit UI commands where practical.

## Sound packs

Support:

* list installed packs,
* show validation status,
* show author and description,
* show sound sets,
* show sample count,
* show estimated decoded memory,
* import local folder,
* remove non-built-in pack,
* preview sample or sound set.

Prevent removal when the pack is actively required unless the user first changes affected profiles or confirms a safe fallback migration.

## Audio settings

Support:

* output device selection,
* configured latency,
* current mixer format,
* active voice count,
* maximum voice count,
* device error state,
* test sound.

Do not expose advanced audio settings that cannot be validated.

# MVVM requirements

Continue using MVVM.

Requirements:

* no profile or audio logic in code-behind,
* asynchronous commands for I/O,
* cancellation where applicable,
* observable validation errors,
* unsaved-change tracking,
* prevent duplicate save/import/device-switch operations,
* marshal UI updates through Avalonia dispatcher,
* do not block the UI thread.

Use the existing MVVM library or pattern.

Do not introduce ReactiveUI unless already present or clearly required.

# Validation rules

Validate profile values.

Recommended limits:

```text
Master volume: 0.0–1.0
Group volume: 0.0–1.0
Pitch variation: 0.0–3.0 semitones
Volume variation: 0.0–0.20
Repeat throttle: 20–1000 ms
Profile name: 1–80 characters
Pack ID and profile ID: restricted filesystem-safe format
Maximum samples per sound set: reasonable bounded value
Maximum WAV duration: reasonable bounded value
Maximum decoded pack memory: configurable bounded value
```

Invalid profiles must not become active.

The editor should show actionable validation messages.

# Built-in fallback content

Ensure the application always has:

* one built-in or installed default sound pack,
* one default profile,
* one valid key-down sound set.

Use the existing Phase 1 click asset to construct the fallback pack if necessary.

The application must remain usable if:

* user settings are deleted,
* all custom profiles are invalid,
* an imported pack is removed,
* the selected audio device disappears.

# Runtime switching

Profile switching must not restart Raw Input.

Expected flow:

```text
User selects profile
    ↓
Validate profile
    ↓
Load required samples
    ↓
Compile runtime profile
    ↓
Atomically replace active profile snapshot
    ↓
Persist active profile ID
```

During loading:

* keep the previous profile active,
* show loading status,
* reject duplicate switches,
* allow cancellation where practical.

On failure:

* preserve previous active profile,
* report error,
* do not leave partially loaded cache entries incorrectly referenced.

# Concurrency requirements

Review all new mutable shared state.

At minimum synchronize:

* active profile snapshot,
* sample cache,
* profile switching,
* audio-device switching,
* active voice count,
* variation-selection state,
* settings save operations,
* sound-pack import/removal,
* coordinator shutdown.

Do not hold monitor locks across filesystem I/O or awaited operations.

Use immutable snapshots, short critical sections, `SemaphoreSlim`, and shared operation tasks where appropriate.

Shutdown must remain deterministic while any of these operations are active.

During shutdown:

1. reject new imports, saves, previews, profile switches, and device switches,
2. cancel pending operations,
3. await in-progress operations where required,
4. stop routing,
5. stop input,
6. stop audio,
7. flush pending settings writes safely,
8. release resources.

# Performance constraints

Phase 2 should target:

```text
Idle working set: preferably under 160 MB
Active small profile: preferably under 200 MB
Input-to-audio behavior: no meaningful regression from Phase 1
No disk access on keypress path
No JSON parsing on keypress path
No UI dispatch on every key event
No unbounded collections
No unbounded task creation
```

These are targets, not absolute pass/fail guarantees, but regressions must be measured and reported.

Only update UI counters periodically, such as every 100–250 ms, rather than dispatching every trigger individually.

# Privacy requirements

Continue to guarantee:

* no typed text is stored,
* no key sequence is stored,
* no scan-code history is written to disk,
* no keyboard event is transmitted,
* device identity is used only for local profile assignment,
* logs do not contain key identities,
* key-capture mode stores only the one explicitly selected physical-key identifier.

The UI privacy notice should be updated to mention device-specific local profile assignment.

# Logging

Log only operational information:

* profile load/save result,
* pack validation result,
* sample-cache load result,
* audio-device switch result,
* lifecycle failures,
* aggregate counts,
* storage recovery events.

Do not log:

* scan codes,
* virtual key values,
* physical key identities,
* key timing,
* captured override keys,
* typed characters.

# Tests

Expand the automated test suite substantially.

## Key-group resolution

Test:

* alpha key,
* number-row key,
* numpad number,
* main Enter,
* numpad Enter,
* left/right modifier,
* arrows,
* function key,
* unknown key,
* extended-key distinction.

## Routing

Test:

* group mapping selected,
* per-key override takes precedence,
* disabled group suppresses playback,
* disabled key override suppresses playback,
* key-down mapping,
* key-up mapping,
* missing key-up set,
* global enabled state,
* profile enabled state.

## Repeat behavior

Test:

* suppress mode,
* every-repeat mode,
* throttled mode before interval,
* throttled mode after interval,
* key-up clears state,
* device identities remain independent.

Use an injectable clock instead of real delays.

## Variation selection

Test:

* random returns valid sample,
* round robin cycles correctly,
* avoid-immediate-repeat avoids repetition with multiple samples,
* one-sample set behaves correctly,
* state does not grow without bounds.

## Volume and pitch

Test:

* gain composition,
* minimum clamp,
* maximum clamp,
* zero volume,
* pitch semitone-to-ratio conversion,
* configured variation bounds.

## Profile compilation

Test:

* valid profile compiles,
* invalid profile rejected,
* missing sound set rejected,
* missing pack rejected,
* overrides compiled,
* runtime snapshot is immutable or isolated from editor mutation.

## Persistence

Test:

* atomic settings save,
* atomic profile save,
* backup recovery,
* malformed main file with valid backup,
* both files malformed creates defaults,
* temporary-file cleanup,
* concurrent saves serialized,
* invalid profile cannot overwrite valid active profile.

Use temporary directories.

## Sound-pack validation

Test:

* valid manifest,
* missing manifest,
* duplicate sound-set ID,
* missing WAV,
* invalid WAV,
* absolute path rejection,
* traversal rejection,
* excessive duration,
* excessive decoded size,
* unsupported schema version,
* duplicate pack ID.

## Profile switching

Test:

* successful switch,
* loading failure preserves previous profile,
* concurrent switches serialized,
* cancellation preserves previous profile,
* old samples released only after switch,
* active settings persisted after success only.

## Audio-device selection

Use abstractions/fakes to test:

* successful device switch,
* failed switch preserves previous output,
* settings saved only after success,
* fallback to default on device loss,
* no infinite retry,
* concurrent switches serialized.

Do not require actual hardware audio devices in unit tests.

## Shutdown

Test shutdown while:

* profile is loading,
* profile is saving,
* pack is importing,
* audio device is switching,
* preview is active.

Confirm cleanup remains idempotent and awaitable.

# Migration

The application may already have simple settings from Phase 1.

Implement schema-aware migration.

Requirements:

* detect absent or older schema,
* migrate safely,
* preserve existing enabled state where possible,
* construct a default profile around the existing Phase 1 click sample,
* retain a backup before migration,
* never repeatedly migrate the same file,
* surface migration failure without crashing.

Do not build a generic migration framework beyond what is needed for current schemas.

# Manual verification

After implementation, manually verify:

1. Application starts with a valid default profile.
2. Existing Phase 1 click still plays.
3. Alpha keys use the alpha group.
4. Spacebar can use a different sound.
5. Enter can use a different sound.
6. Multiple variations do not repeat mechanically.
7. Profile switching works without restart.
8. Profile switching while typing does not crash.
9. Key-up sounds work when enabled.
10. Repeat suppression still works.
11. Every-repeat mode works.
12. Throttled repeat works.
13. Master volume updates immediately.
14. Group volume updates after save/apply.
15. Pitch variation is audible but controlled.
16. Output-device switching works.
17. Failed output-device selection preserves working audio.
18. Imported valid sound pack appears.
19. Invalid pack is rejected cleanly.
20. Preview works.
21. Restart preserves active profile and settings.
22. Corrupt settings recover from backup or defaults.
23. Closing during profile loading exits cleanly.
24. Closing during device switching exits cleanly.
25. Input still works with another application focused.
26. No orphan process or native thread remains.
27. No typed key data appears in logs or files.

# Build verification

After implementation:

1. Restore packages.
2. Build the complete solution in Release.
3. Run all automated tests.
4. Resolve warnings introduced by Phase 2.
5. Launch the application.
6. Run the manual checks possible in the environment.
7. Report checks that still require a human with audio hardware.
8. Inspect `%LocalAppData%\KeySounds` for correct storage structure.
9. Verify settings and profiles survive restart.
10. Verify no temporary files remain after successful saves.
11. Verify corrupt-file recovery using controlled test files.
12. Verify memory usage with the default profile.
13. Verify active sample count and decoded-memory estimate.

# Acceptance criteria

Phase 2 is complete only when:

* multiple profiles can be created, saved, selected, and deleted safely,
* active profile switches without application restart,
* key groups route to different sound sets,
* multiple variations are supported,
* immediate repetition can be avoided,
* key-down and key-up sounds are independently configurable,
* repeat behavior supports suppress, every-repeat, and throttled modes,
* master and group volume work,
* pitch and volume variation are bounded,
* sample data remains predecoded,
* no disk or JSON work occurs on the keypress path,
* local sound packs can be validated and imported,
* sound sets can be previewed,
* output devices can be listed and selected,
* failed device switching preserves or restores working audio,
* settings and profiles persist atomically,
* backups recover from malformed files,
* invalid profiles never replace a valid active runtime profile,
* device-specific profile assignment works where stable identity is available,
* all Phase 1 lifecycle guarantees remain intact,
* shutdown remains deterministic during Phase 2 operations,
* Release build succeeds,
* all tests pass,
* no keyboard data is logged or stored.

# Deliverables

When complete, provide:

1. Architecture summary.
2. New domain models.
3. New services.
4. Files added.
5. Files modified.
6. Packages added or updated.
7. Profile JSON schema.
8. Sound-pack manifest schema.
9. Settings schema.
10. Storage-directory layout.
11. Migration behavior.
12. Audio-device switching behavior.
13. Sample-cache behavior.
14. Test count and results.
15. Release build result.
16. Manual verification results.
17. Measured idle memory.
18. Measured memory with default profile loaded.
19. Known limitations.
20. Deferred items for Phase 3.
21. Any deviation from this specification and its justification.

Start by inspecting the current Phase 1 implementation. Implement Phase 2 incrementally, preserving a successful build and test state after each major subsystem.
