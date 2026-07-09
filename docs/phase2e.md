You are working inside an existing Avalonia + C# Windows keyboard-sound application.

Phase 1, lifecycle hardening, Phase 2 foundations, Phase 2B sound packs, Phase 2C profile management, and Phase 2D audio-device selection are complete.

## Current implemented state

The application already includes:

* Avalonia desktop UI
* Dashboard, Profiles, and Audio navigation
* Dedicated hidden Win32 Raw Input window
* Global keyboard input outside application focus
* Bounded input channel
* Per-device pressed-key tracking using transient Raw Input handles
* Key-down and key-up routing
* Suppressed, every-event, and throttled repeat modes
* Physical scan-code key grouping
* Immutable compiled profile snapshots
* Per-trigger volume and pitch variation
* Persistent NAudio WASAPI output
* 48 kHz stereo floating-point mixer
* Bounded voice playback
* Versioned sound-pack manifests
* Strict pack validation and transactional import
* Immutable pack catalog
* Pack-aware decoded sample cache
* Constant-time playback lookup
* Safe profile activation pipeline
* Preview through the existing cache and mixer
* Full profile CRUD and editor workflow
* Sparse per-key overrides
* One-key capture
* Profile import/export
* Audio endpoint enumeration
* System-default and specific audio output selections
* Configured-versus-effective output state
* Serialized audio switching and rollback
* Device-loss fallback
* Audio settings schema version 2
* Deterministic shutdown
* Release build with 0 warnings/errors
* 69 passing tests
* Clean immediate-close cycles
* No orphan processes
* No keyboard-data logging

Do not recreate or weaken any existing system.

Your task is to implement:

# Phase 2E — Stable Raw Input device identity and per-keyboard profile assignment

## Objective

After this slice, the application must be able to:

1. Identify connected keyboards beyond their transient Raw Input handles.
2. Resolve each Raw Input device handle to a stable local device identity where possible.
3. Display detected keyboards in the UI.
4. Assign an optional sound profile to each keyboard.
5. Use the assigned profile when an event originates from that keyboard.
6. Fall back to the global active profile when no assignment exists.
7. Persist keyboard-to-profile assignments safely.
8. Handle keyboard disconnects and reconnects.
9. Avoid blocking the Raw Input callback while resolving identity.
10. Preserve all real-time, privacy, and shutdown guarantees.
11. Distinguish stable identity, session handle, and display metadata.
12. Handle indistinguishable identical devices explicitly rather than pretending perfect identity.

Do not implement vendor firmware integrations, HID output, lighting, QMK/VIA support, kernel drivers, cloud sync, device telemetry, or per-application profile rules.

# First action: inspect the repository

Before editing:

1. Inspect the normalized keyboard input event.
2. Inspect the Raw Input message handling path.
3. Inspect current use of `DeviceHandle`.
4. Inspect pressed-key state and repeat suppression.
5. Inspect profile routing and active-profile resolution.
6. Inspect application settings schema version 2.
7. Inspect profile activation and sample-cache preparation.
8. Inspect dashboard and profile selectors.
9. Inspect shutdown and device-notification infrastructure.
10. Inspect all 69 tests.

Identify every place where transient Raw Input handles are currently treated as device identity.

Preserve existing behavior until the new identity service successfully resolves a device.

# Identity model

Create a clear distinction between three concepts.

## Session device handle

The transient `RAWINPUTHEADER.hDevice` value.

Properties:

* valid only within the current Windows session/process context,
* used for immediate event correlation,
* not persisted,
* may change after reconnect or reboot.

## Stable device identity

A normalized local identifier derived from Windows device metadata.

Recommended value object:

```csharp
public readonly record struct KeyboardDeviceId(string Value)
{
    public override string ToString() => Value;
}
```

Requirements:

* nonempty,
* deterministic for the same resolvable device metadata,
* safe for JSON persistence,
* not derived from transient `nint` handles,
* not claimed to be globally unique when Windows metadata cannot distinguish devices.

## Display descriptor

User-facing metadata:

```csharp
public sealed record KeyboardDeviceDescriptor(
    KeyboardDeviceId Id,
    string DisplayName,
    string? Manufacturer,
    string? ProductName,
    ushort? VendorId,
    ushort? ProductId,
    string? DevicePath,
    bool IsConnected,
    bool IsIdentityAmbiguous,
    DateTimeOffset LastSeenUtc);
```

Do not persist raw session handles.

Consider whether storing the full Windows device path is necessary. Prefer storing a normalized hash or parsed identity components in settings, while retaining the raw device path only in memory if possible.

# Windows Raw Input metadata

Use Raw Input device APIs.

Inspect and use:

```text
GetRawInputDeviceInfo
RIDI_DEVICENAME
RIDI_DEVICEINFO
```

For each keyboard device handle, retrieve where available:

* device name/path,
* device type,
* keyboard information,
* vendor ID,
* product ID,
* usage page,
* usage,
* other stable path components.

Validate:

* result sizes,
* character counts,
* terminating null handling,
* device type,
* malformed responses,
* `uint.MaxValue` failures,
* reconnect races.

Do not perform these calls inside the latency-sensitive `WM_INPUT` processing path beyond enqueueing a resolution request.

# Identity derivation

Implement a deterministic identity derivation strategy.

Recommended priority:

## Tier 1 — Stable device path identity

When a normalized Raw Input device path is available:

1. normalize case,
2. normalize separator representation,
3. remove volatile session-only suffixes only when clearly documented and safe,
4. combine with vendor/product information where available,
5. hash the normalized identity source using a stable cryptographic hash,
6. encode a bounded local ID.

Example conceptual output:

```text
kbd-v1-4f8c0d...
```

Do not use `string.GetHashCode()` because it is not stable across processes.

Use a stable hash such as SHA-256 and truncate only to a sufficiently collision-resistant length.

## Tier 2 — Hardware metadata identity

When the path is unavailable but vendor/product/usage metadata exists:

```text
vendor ID
product ID
usage page
usage
normalized product name
```

Mark this identity as potentially ambiguous.

## Tier 3 — Session-only fallback

When no stable metadata exists:

* create an in-memory session identity linked to the transient handle,
* mark it ambiguous and nonpersistent,
* do not save profile assignments against it unless explicitly supported as session-only,
* fall back to the global profile after restart.

The UI must clearly show when a device cannot be identified persistently.

# Identical keyboards and ambiguity

Two identical keyboard units may expose indistinguishable metadata.

Do not claim reliable physical-unit distinction unless Windows exposes unique path components.

Requirements:

* detect when two connected handles resolve to the same stable identity,
* mark descriptors as ambiguous,
* preserve separate session handles for pressed-key tracking,
* use the same profile assignment for all devices sharing the stable identity,
* display a warning such as:

  * “Two connected keyboards expose the same hardware identity and cannot be assigned independently.”

Do not create fake stable uniqueness from enumeration order.

Enumeration order is not persistent.

# Identity service

Create a platform-independent contract and Windows implementation.

Suggested contract:

```csharp
public interface IKeyboardDeviceIdentityService : IAsyncDisposable
{
    KeyboardDeviceResolution ResolveCached(nint deviceHandle);

    ValueTask<KeyboardDeviceResolution> ResolveAsync(
        nint deviceHandle,
        CancellationToken cancellationToken);

    IReadOnlyList<KeyboardDeviceDescriptor> GetConnectedDevicesSnapshot();

    event EventHandler<KeyboardDevicesChangedEventArgs>? DevicesChanged;
}
```

Suggested resolution:

```csharp
public sealed record KeyboardDeviceResolution(
    KeyboardDeviceId? StableId,
    bool IsPersistent,
    bool IsAmbiguous,
    string DisplayName,
    string? ErrorCode);
```

Exact API may differ.

Requirements:

* constant-time cached lookup for routed events,
* asynchronous metadata resolution,
* bounded resolution work,
* no repeated native query on every key event,
* negative-result caching with expiry,
* thread-safe immutable snapshots,
* shutdown-aware behavior,
* no raw keyboard metadata logging beyond operational device descriptors.

# Integration with Raw Input

The Raw Input callback must remain minimal.

On each event:

1. preserve transient `DeviceHandle`,
2. normalize key data,
3. enqueue event,
4. optionally enqueue a lightweight identity-resolution request if handle is unknown,
5. return.

Do not call slow device metadata APIs directly inside the window procedure.

A background resolver should:

* deduplicate unknown handles,
* resolve metadata once,
* cache the result,
* publish device-list changes,
* avoid unbounded task creation.

Use a bounded queue or serialized resolution service.

# Device enumeration

At startup, enumerate currently connected Raw Input keyboard devices using:

```text
GetRawInputDeviceList
```

Requirements:

* query required count safely,
* handle device-list changes between count and read,
* retry a bounded number of times,
* filter to keyboard devices,
* resolve each device asynchronously or serially,
* do not block application startup indefinitely,
* expose partial results,
* continue safely when one device fails.

Refresh enumeration on relevant device-change notifications where available.

# Device-change handling

Use appropriate Windows device-change mechanisms.

Potential sources include:

* Raw Input `WM_INPUT_DEVICE_CHANGE` with registration flags where applicable,
* general Windows device notifications,
* periodic manual refresh command.

Preferred approach:

* register for Raw Input device notifications if compatible with existing registration,
* process arrival/removal notifications minimally,
* schedule identity catalog reconciliation,
* provide a manual refresh command.

Requirements:

* no expensive work in native callbacks,
* debounce rapid changes,
* remove disconnected session handles from connected snapshots,
* preserve persisted assignments,
* clear pressed-key state for disconnected handles,
* avoid stale repeat state,
* remain safe during shutdown.

# Registration changes

If enabling device-change notifications requires modifying Raw Input registration flags, preserve:

* background input delivery,
* existing keyboard registration,
* deterministic `RIDEV_REMOVE` cleanup,
* current lifecycle guarantees.

Audit all flag combinations against existing behavior.

Do not introduce duplicate Raw Input registrations from multiple components.

# Profile assignment model

Extend application settings.

Recommended:

```csharp
public Dictionary<string, string> KeyboardProfileAssignments { get; set; }
    = new(StringComparer.Ordinal);
```

Mapping:

```text
KeyboardDeviceId.Value → ProfileId
```

Consider replacing the mutable dictionary in runtime use with an immutable snapshot.

Requirements:

* stable keyboard ID only,
* profile ID must exist before assignment becomes active,
* fallback profile always remains available,
* invalid assignments are retained for recovery or reported explicitly,
* session-only device identities are not persisted,
* assignment changes are atomic,
* settings are saved only after validation succeeds.

Increment settings schema version from 2 to 3.

# Settings migration

Implement schema version 3 migration.

Existing version 2 settings should migrate by:

* adding an empty keyboard assignment dictionary,
* preserving audio output settings,
* preserving active profile,
* preserving global volume and enable state,
* preserving all existing settings.

Requirements:

* backup before migration,
* idempotent migration,
* safe fallback on failure,
* no loss of version 2 audio settings,
* no repeated migration.

# Profile selection rules

Implement profile resolution in this order:

```text
Resolved persistent keyboard assignment
    ↓
Assigned profile exists and is runtime-ready
    ↓
Use assigned profile

Otherwise
    ↓
Use global active profile

Otherwise
    ↓
Use built-in fallback profile
```

Do not allow an invalid device assignment to break playback.

# Runtime profile readiness

A keyboard-assigned profile may differ from the global active profile.

Therefore, the runtime must support multiple prepared compiled profiles concurrently.

Implement a bounded prepared-profile registry.

Suggested model:

```csharp
public sealed record PreparedProfileSnapshot(
    string ProfileId,
    CompiledSoundProfile Profile,
    IReadOnlySet<SoundSetReference> RequiredSoundSets);
```

Requirements:

* global active profile remains prepared,
* profiles assigned to currently connected keyboards are prepared,
* samples required by all active assignments remain cached,
* assigning a profile first validates and prepares it,
* assignment persists only after preparation succeeds,
* unassigning a profile may release samples no longer required by:

  * global active profile,
  * another keyboard assignment,
  * preview or active voices.

Do not compile or prepare a profile on the keypress path.

# Assignment transaction

Required assignment flow:

```text
User selects keyboard and profile
    ↓
Validate persistent keyboard identity
    ↓
Validate profile exists
    ↓
Compile candidate profile
    ↓
Prepare required samples
    ↓
Verify cache
    ↓
Publish assignment runtime snapshot
    ↓
Persist settings
    ↓
Release no-longer-required samples
```

On failure:

* preserve previous assignment,
* preserve current playback,
* do not persist partial state,
* return structured error.

If settings persistence fails:

* restore previous runtime assignment,
* preserve previous cache requirements where practical,
* report failure.

# Routing integration

Extend the keyboard sound router.

Current routing likely resolves one global compiled profile.

New routing must use:

```text
event.DeviceHandle
    ↓
cached device resolution
    ↓
stable KeyboardDeviceId if available
    ↓
assignment snapshot lookup
    ↓
assigned compiled profile or global profile
    ↓
existing key/group/override routing
```

Requirements:

* constant-time dictionary lookups,
* no native metadata queries,
* no disk access,
* no JSON parsing,
* no asynchronous work,
* no UI dispatch,
* no locks with unbounded contention.

Use atomic immutable snapshots.

# Repeat-state behavior

Pressed and repeat state must remain keyed by the transient physical event source:

```text
DeviceHandle
+ ScanCode
+ IsExtended
```

Do not replace transient handle tracking with stable device ID.

Reason:

* two connected devices may share one stable identity,
* pressed-state tracking must remain independent,
* disconnect cleanup requires handle-level state.

Profile resolution and key-state identity are separate concerns.

# Connected device registry

Maintain immutable snapshots of connected keyboards.

Each entry should expose:

* stable ID where available,
* display name,
* persistent/ambiguous status,
* connected session count,
* assigned profile ID,
* assigned profile name,
* runtime readiness,
* last-seen timestamp,
* status/error.

Do not expose transient handles directly in normal UI.

A developer diagnostic may show count or masked handle information, but not persist it.

# UI

Add a `Keyboards` navigation section or a focused dashboard panel.

Preferred new page:

```text
Keyboards
```

Display one row/card per resolved device identity.

Show:

* display name,
* manufacturer/product where available,
* connected status,
* identity reliability:

  * persistent,
  * ambiguous,
  * session-only,
* number of connected instances sharing identity,
* assigned profile,
* runtime status,
* last-seen time where useful.

Controls:

* profile selector,
* apply assignment,
* clear assignment,
* refresh devices,
* retry identity resolution,
* preview assigned profile,
* open selected profile in editor.

For session-only identities:

* disable persistent assignment,
* explain why,
* continue using global profile.

# Assignment UX

The UI must clearly distinguish:

```text
Global active profile
Keyboard-specific assigned profile
Effective profile for this keyboard
```

Example:

```text
Global profile: Mechanical Linear
Assigned profile: Typewriter
Effective profile: Typewriter
```

For no assignment:

```text
Assigned profile: None
Effective profile: Mechanical Linear (global)
```

For invalid saved assignment:

```text
Assigned profile: Missing profile “xyz”
Effective profile: Mechanical Linear (global)
```

Do not silently delete invalid assignments.

# Device naming

Use the best available display name.

Priority:

1. product name,
2. manufacturer + product,
3. normalized device-path-derived label,
4. `Keyboard device`,
5. `Unknown keyboard`.

Avoid exposing long raw HID paths in the primary UI.

A details section may expose limited technical metadata.

# Device details

Optional details panel may show:

* vendor ID,
* product ID,
* usage page,
* usage,
* persistent identity status,
* ambiguity warning,
* stable ID prefix.

Do not display the full stable hash unless useful for diagnostics.

Do not display transient device handles.

# Privacy

Keyboard device metadata is local hardware information.

Requirements:

* no device metadata transmission,
* no telemetry,
* no scan-code logging,
* no key sequence logging,
* no key timing history,
* assignment storage contains only stable device ID and profile ID,
* raw device paths should not be persisted unless required and justified,
* UI privacy notice should mention local keyboard identification for profile assignment.

# Logging

Allowed:

* stable device ID prefix or bounded ID,
* display name,
* vendor/product IDs,
* resolution success/failure,
* arrival/removal,
* assignment success/failure,
* aggregate connected count,
* ambiguity warnings.

Do not log:

* transient handle values unless strictly needed in debug-only diagnostics,
* scan codes,
* virtual keys,
* key timing,
* key sequences,
* captured key identity from profile overrides,
* typed text.

# Concurrency

Synchronize:

* handle-to-identity cache mutation,
* connected-device catalog refresh,
* assignment changes,
* prepared-profile registry,
* sample-cache requirement recalculation,
* device notification reconciliation,
* shutdown.

Requirements:

* no monitor lock across native queries or filesystem I/O,
* use immutable snapshots for routing,
* serialize assignment transactions,
* deduplicate concurrent resolution for the same handle,
* bound negative-result cache,
* bound disconnected-device history,
* reject new assignments during shutdown.

# Disconnected assignments

Persisted assignments should survive disconnects.

When a keyboard disconnects:

* remove it from connected snapshot,
* preserve assignment in settings,
* release prepared profile if no connected instance or other requirement needs it,
* clear pressed-state entries for its transient handle,
* do not delete the assignment.

When it reconnects with the same stable identity:

* assignment should apply automatically,
* prepare the assigned profile if not already prepared,
* use global profile until preparation completes,
* switch atomically once ready.

# Preparation during reconnect

Do not block the input callback.

Recommended flow:

```text
Keyboard reconnects
    ↓
Identity resolves
    ↓
Persisted assignment found
    ↓
Background profile preparation begins
    ↓
Global profile remains effective temporarily
    ↓
Prepared assignment snapshot published
```

Expose `Preparing` status in UI.

# Missing assigned profile

If settings reference a profile that no longer exists:

* retain assignment entry,
* mark invalid,
* use global profile,
* show repair action,
* allow user to select replacement or clear assignment,
* do not repeatedly throw or retry.

# Profile deletion integration

Update profile deletion rules.

Before deleting a profile:

* find keyboard assignments referencing it,
* block deletion by default,
* report affected keyboard identities,
* allow the user to clear or reassign them first.

Do not leave dangling assignments silently.

Fallback profile deletion remains prohibited.

# Profile import integration

When importing a profile with an ID matching an invalid assignment reference:

* assignment may become valid after refresh,
* prepare it for connected assigned keyboards,
* update status atomically.

Do not auto-activate globally.

# Sound-pack removal integration

A sound pack required only by a keyboard-assigned profile is still in use.

Update pack-removal safeguards to inspect:

* global active profile,
* connected prepared assigned profiles,
* saved profiles,
* persisted keyboard assignments where applicable.

Do not remove a pack that would break an effective assigned profile.

# Audio interaction

No new audio output behavior is required.

All keyboard-assigned profiles must use:

* current effective audio endpoint,
* current latency,
* current mixer,
* existing cache,
* existing voice limit.

Do not create one audio engine per keyboard.

# Lifecycle

## Startup

1. Load settings schema version 3.
2. Initialize global profile.
3. Initialize audio.
4. Start Raw Input.
5. Enumerate connected keyboards.
6. Resolve device identities.
7. Load persisted assignments.
8. Prepare assigned profiles for connected keyboards.
9. Publish assignment snapshot.
10. Update UI.

The UI may appear before identity enumeration completes.

## Shutdown

1. Reject new assignment operations.
2. Cancel identity resolutions.
3. Cancel device reconciliation.
4. Cancel assigned-profile preparations.
5. Await active assignment transactions.
6. unsubscribe device notifications,
7. stop routing,
8. stop Raw Input,
9. stop audio,
10. release identity-service resources,
11. preserve deterministic process exit.

Maintain immediate-close behavior.

# Tests

Expand the suite substantially.

## Identity derivation

Test:

1. stable path produces deterministic ID,
2. same normalized path produces same ID,
3. case differences normalize correctly,
4. vendor/product metadata participates where intended,
5. stable hash does not use `GetHashCode`,
6. path unavailable falls back to hardware metadata,
7. hardware-only identity is marked ambiguous,
8. no metadata produces session-only identity,
9. identity IDs are filesystem/JSON safe,
10. distinct paths produce distinct IDs.

## Native metadata parsing

Using abstractions or constructed buffers, test:

1. valid device name,
2. required size query failure,
3. truncated name result,
4. valid device info,
5. non-keyboard device rejected,
6. malformed metadata contained,
7. device removed during query,
8. cancellation.

Do not require physical hardware for ordinary unit tests.

## Handle cache

Test:

1. unknown handle schedules one resolution,
2. concurrent events deduplicate resolution,
3. resolved handle uses cached identity,
4. negative result cached temporarily,
5. negative cache expires,
6. cache remains bounded,
7. disconnected handle removed,
8. shutdown rejects new resolution.

## Device catalog

Test:

1. startup enumeration finds keyboards,
2. one invalid device does not break catalog,
3. duplicate stable identity merges connected instances,
4. ambiguity flag set for duplicate identity,
5. device arrival updates catalog,
6. device removal updates catalog,
7. rapid notifications debounce,
8. manual refresh works,
9. immutable snapshot isolation.

## Settings migration

Test:

1. schema 2 migrates to schema 3,
2. assignments initialize empty,
3. audio settings preserved,
4. active profile preserved,
5. migration backup created,
6. migration idempotent,
7. failed migration preserves previous file.

## Assignment transaction

Test:

1. valid assignment prepares profile,
2. assignment publishes after preparation,
3. assignment persists after success,
4. preparation failure preserves old assignment,
5. settings failure rolls back runtime assignment,
6. clearing assignment restores global profile,
7. session-only identity assignment rejected,
8. ambiguous persistent identity assignment allowed with warning,
9. concurrent assignments serialize,
10. shutdown rejects assignment.

## Routing

Test:

1. assigned keyboard uses assigned profile,
2. unassigned keyboard uses global profile,
3. unknown handle uses global profile,
4. invalid assignment uses global profile,
5. missing prepared profile uses global profile,
6. assigned profile key override applies,
7. two session handles sharing identity use same assigned profile,
8. pressed-state tracking remains independent by handle,
9. routing performs no async work.

## Reconnect

Test:

1. disconnect preserves assignment,
2. reconnect same identity reapplies assignment,
3. global profile used while preparation runs,
4. assigned profile takes over after preparation,
5. failed preparation leaves global profile active,
6. multiple reconnect notifications do not duplicate preparation.

## Profile deletion

Test:

1. profile referenced by assignment cannot be deleted,
2. affected identities reported,
3. clearing assignment permits deletion,
4. fallback deletion still prohibited,
5. invalid dangling assignment remains repairable.

## Pack removal

Test:

1. pack used by assigned profile is protected,
2. disconnected persisted assignment behavior follows chosen policy,
3. unreferenced pack remains removable,
4. prepared registry updates after assignment cleared.

## UI view models

Test:

1. device list updates,
2. global/assigned/effective profile labels correct,
3. session-only assignment command disabled,
4. ambiguous warning shown,
5. apply command gated,
6. clear command works,
7. refresh state updates,
8. invalid assignment shown,
9. preparing state shown.

## Shutdown

Test shutdown while:

* enumerating devices,
* resolving identity,
* applying assignment,
* preparing assigned profile,
* reconciling arrival/removal,
* refreshing catalog.

Confirm:

* no deadlock,
* no orphan resolution task,
* no native callback after disposal,
* settings remain consistent,
* all lifecycle operations complete.

# Manual verification

After implementation, manually verify:

1. Keyboards page opens.
2. Connected keyboard appears.
3. Display name is reasonable.
4. Vendor/product metadata appears where available.
5. Stable identity remains the same after application restart.
6. Assign a different profile to keyboard.
7. Assigned keyboard uses assigned profile.
8. Another unassigned keyboard uses global profile, if two keyboards are available.
9. Clear assignment restores global profile.
10. Disconnect keyboard.
11. Assignment remains saved.
12. Reconnect keyboard.
13. Assignment reapplies.
14. Assigned profile preparation does not freeze input.
15. Two identical keyboards show ambiguity if indistinguishable.
16. Session-only identity cannot be persistently assigned.
17. Missing assigned profile falls back safely.
18. Profile deletion is blocked when assigned.
19. Pack removal is blocked when needed by assigned profile.
20. Close during identity resolution exits cleanly.
21. Close during assignment preparation exits cleanly.
22. Five immediate-close cycles pass.
23. No orphan process remains.
24. No keyboard data appears in logs.
25. No transient device handle appears in persisted settings.

Hardware-dependent multi-keyboard and reconnect checks must be reported separately if only one keyboard is available.

# Performance requirements

Preserve:

* no native identity query on the keypress path,
* no disk access on the keypress path,
* no JSON parsing on the keypress path,
* constant-time assignment lookup,
* immutable routing snapshots,
* bounded resolution queue,
* bounded identity cache,
* no UI dispatch per key event.

Measure and report:

* startup keyboard enumeration duration,
* identity resolution duration,
* reconnect reconciliation duration,
* working-set change after Keyboards page use,
* routing lookup overhead if benchmark infrastructure exists.

# Acceptance criteria

This slice is complete only when:

* Raw Input handles resolve asynchronously to stable identities where possible,
* session handles are never persisted,
* device identity resolution does not run in `WM_INPUT`,
* connected keyboards are visible in UI,
* persistent profile assignments can be made,
* assigned profile preparation occurs before runtime publication,
* settings persist only after successful assignment,
* failed assignment preserves previous behavior,
* unassigned and unresolved devices use global profile,
* reconnect restores persisted assignment,
* identical ambiguous devices are represented honestly,
* pressed-state tracking remains handle-specific,
* profile deletion and pack removal respect assignments,
* schema version 3 migration preserves existing settings,
* shutdown remains deterministic,
* Release build succeeds with zero introduced warnings,
* all tests pass,
* no keyboard values, sequences, or transient handles are logged or persisted.

# Deliverables

When complete, provide:

1. Identity architecture summary.
2. Stable-ID derivation strategy.
3. Ambiguity behavior.
4. Native metadata APIs used.
5. Handle-cache behavior.
6. Device catalog behavior.
7. Assignment transaction behavior.
8. Runtime prepared-profile registry behavior.
9. Routing resolution order.
10. Reconnect behavior.
11. Settings schema version 3 migration.
12. Profile deletion and pack-removal integration.
13. UI additions.
14. Files added.
15. Files modified.
16. Packages added or changed.
17. Test count and results.
18. Release build result.
19. Manual verification results.
20. Enumeration and identity timings.
21. Working-set measurement.
22. Known limitations.
23. Remaining Phase 2 work.

Start by inspecting the existing Raw Input event model, routing service, profile activation service, and settings migration. Implement identity models and native metadata resolution first, then cached resolution and enumeration, then assignment transactions and routing, then persistence and UI.
