You are working inside an existing Avalonia + C# Windows keyboard-sound application.

Phase 1, lifecycle hardening, Phase 2 foundational runtime work, and Phase 2B sound-pack infrastructure are complete.

## Current implemented state

The application already includes:

* Avalonia desktop UI
* Dedicated hidden Win32 Raw Input window
* Global background keyboard input
* Bounded channel event pipeline
* Per-device pressed-key tracking
* Key-down, key-up, suppress/every/throttled repeat routing
* Physical scan-code key grouping
* Immutable compiled profile snapshots
* Global/profile/group/override gain composition
* Per-trigger pitch and volume variation
* Persistent NAudio WASAPI output
* 48 kHz stereo float mixer
* 32-voice bound
* Versioned sound-pack manifests
* Strict sound-pack validation
* Transactional local folder import services
* Immutable pack catalog snapshots
* Stable `SoundId(packId, soundSetId, relativeSamplePath)`
* Serialized bounded decoded-sample cache
* Constant-time playback lookup
* Profile activation flow:

  * prepare
  * verify
  * activate
  * persist
  * release
* Preview services
* Protected pack removal
* Legacy fallback migration
* Atomic profile/settings persistence
* Backup recovery
* Built-in fallback pack and profile
* Release build with 0 warnings/errors
* 52 passing tests
* Clean startup and immediate-close cycles
* No orphan process
* Approximate working set around 114 MiB

Do not recreate, replace, or weaken these systems.

Your task is to implement:

# Phase 2C — Profile management and editor UI

Implement the complete user-facing profile workflow on top of the existing runtime, persistence, validation, sound-pack, preview, and activation services.

## Objective

After this slice, the user must be able to:

1. View all profiles.
2. Create a new profile.
3. Duplicate a profile.
4. Rename a profile.
5. Delete a profile safely.
6. Select and activate a profile.
7. Edit all supported profile settings.
8. Save changes.
9. Revert unsaved changes.
10. See validation errors before activation.
11. Configure each key group.
12. Configure key-down and key-up sound sets.
13. Configure repeat behavior.
14. Configure pitch and volume variation.
15. Manage sparse per-key overrides through a capture workflow.
16. Preview selected sound sets and samples.
17. Import and export profile JSON.
18. See unsaved-change state.
19. Recover gracefully from invalid or missing referenced packs.
20. Preserve immutable runtime snapshots and real-time path guarantees.

Do not implement audio-device switching, stable keyboard-device assignment, installer, updater, tray behavior, cloud sync, or procedural synthesis in this slice.

# First action: inspect the repository

Before editing:

1. Inspect the current profile model.
2. Inspect profile repository APIs.
3. Inspect profile validation and compiler APIs.
4. Inspect active-profile activation flow.
5. Inspect current dashboard view model and navigation structure.
6. Inspect Avalonia styles and current MVVM conventions.
7. Inspect sound-pack catalog and preview services.
8. Inspect existing file/folder picker usage, if any.
9. Inspect all 52 tests.
10. Preserve current namespaces and project layout.

Do not move core runtime logic into view models.

# UI architecture

Implement a focused profile-management area.

Recommended navigation:

```text
Dashboard
Profiles
Sound Packs
Audio
```

For this slice, fully implement only:

```text
Dashboard
Profiles
```

Keep Sound Packs minimal if already present, and leave Audio as current status-only behavior.

The Profiles section should contain:

```text
Profile list
    |
    +--> Create
    +--> Duplicate
    +--> Rename
    +--> Delete
    +--> Import
    +--> Export
    +--> Activate

Profile editor
    |
    +--> General
    +--> Repeat behavior
    +--> Key groups
    +--> Per-key overrides
```

Use existing MVVM conventions.

# Editing model

Do not bind the UI directly to the active runtime profile.

Create an editable profile session.

Suggested shape:

```csharp
public sealed class EditableProfileSession
{
    public required SoundProfile Original { get; init; }

    public required SoundProfile WorkingCopy { get; init; }

    public bool HasUnsavedChanges { get; }

    public IReadOnlyList<ProfileValidationIssue> ValidationIssues { get; }

    public bool IsValid { get; }
}
```

Exact design may differ, but maintain these invariants:

1. Editing never mutates the active compiled runtime profile.
2. The working model is isolated from repository snapshots.
3. Save validates first.
4. Activation occurs only after successful save and preparation.
5. Revert restores the last saved model.
6. Switching away from a dirty profile requires an explicit decision:

   * save,
   * discard,
   * cancel.
7. The active runtime remains unchanged until activation succeeds.

# Profile list behavior

Display:

* profile name,
* active state,
* validation state,
* modified/dirty state,
* referenced pack count,
* fallback/default marker where applicable.

Support:

* select,
* activate,
* create,
* duplicate,
* rename,
* delete,
* import,
* export.

## Create

Create from one of:

* built-in fallback template,
* currently selected profile.

Use a filesystem-safe generated ID.

Profile name may be user-facing and need not match ID.

## Duplicate

Duplicate the selected saved profile.

Requirements:

* assign a new unique ID,
* append a user-friendly name suffix,
* copy group mappings and overrides deeply,
* do not copy active status,
* save atomically.

## Rename

Rename the display name only unless current repository semantics explicitly require ID changes.

Prefer stable immutable profile IDs.

Do not rename files solely because the display name changes.

## Delete

Rules:

* built-in fallback profile cannot be deleted,
* active profile cannot be deleted without first activating another valid profile,
* if deleting a non-active profile, remove it atomically,
* refresh profile list,
* select a safe neighboring profile,
* do not delete referenced sound packs,
* surface clear errors.

# General profile editor

Support:

* profile name,
* profile enabled,
* profile master volume,
* play on key-down,
* play on key-up,
* repeat mode,
* repeat minimum interval,
* optional description only if already supported or trivial to add.

Recommended controls:

* text box for name,
* toggle for enabled,
* slider plus numeric display for master volume,
* toggles for key-down and key-up,
* combo box for repeat mode,
* numeric input for throttle interval.

Validation ranges must match existing runtime validation.

# Key-group editor

Display every supported `KeySoundGroup`.

At minimum:

```text
Alpha
NumberRow
Modifier
Spacebar
Enter
Backspace
Tab
Navigation
Arrow
Function
Numpad
Escape
Other
```

For each group, support:

* enabled,
* sound pack,
* key-down sound set,
* optional key-up sound set,
* volume,
* pitch variation semitones,
* volume variation,
* variation mode,
* preview key-down,
* preview key-up where configured.

Use dependent selection:

1. User selects pack.
2. Sound-set options update to sets from that pack.
3. Existing invalid selection remains visible as invalid rather than silently replaced.
4. User must choose a valid replacement before save.

Do not automatically erase invalid references during load.

# Sound-set selection

For each group mapping:

* pack selector lists valid installed packs,
* sound-set selector lists sets from selected pack,
* invalid or missing referenced pack/set is shown clearly,
* preview uses existing preview service,
* preview command is disabled during active save, activation, import, or shutdown.

The UI must not decode or load WAV files directly.

# Per-key override workflow

Implement a practical technical override editor without a full visual keyboard.

## Required workflow

1. User clicks `Capture key`.
2. Application enters one-key capture mode.
3. Next eligible physical key event is captured.
4. Capture stores only:

   * scan code,
   * extended flag.
5. Capture mode exits automatically.
6. The captured key is displayed using a human-friendly technical label.
7. User can create or edit an override.
8. User can cancel capture.
9. No key sequence or history is stored.

## Override fields

Support:

* override enabled/disabled,
* suppress sound entirely,
* optional pack override,
* optional key-down sound-set override,
* optional key-up sound-set override,
* optional volume override,
* optional pitch variation override,
* optional volume variation override,
* optional variation mode override if model supports it.

Keep overrides sparse.

Do not require every field to be set.

## Capture safety

While capture mode is active:

* capture exactly one physical key,
* do not append to a sequence,
* do not log the key,
* do not persist before the user saves the profile,
* prevent accidental capture of mouse or non-keyboard input,
* ignore auto-repeat after first captured key,
* allow Escape to cancel only if that does not conflict with capturing Escape; provide an explicit cancel button regardless,
* do not disrupt the existing global routing pipeline permanently.

Prefer a temporary capture subscription or coordinator mode rather than bypassing the established input architecture.

# Human-readable key labels

Create a presentation-only formatter.

Examples:

```text
A
Left Shift
Right Ctrl
Enter
Numpad Enter
Space
Arrow Left
F5
ScanCode 0xNN
```

Use scan code and extended state.

Do not use the label as persistent identity.

Unknown keys must still display a stable technical fallback label.

# Dirty-state tracking

Track unsaved changes accurately.

Requirements:

* changes to any general field mark dirty,
* changes to any group mapping mark dirty,
* adding/editing/removing override marks dirty,
* reverting clears dirty,
* successful save clears dirty,
* failed save keeps dirty,
* activation alone does not clear unrelated unsaved edits,
* loading a different profile resets against that profile’s saved snapshot.

Avoid comparing serialized JSON on every property change if a cleaner change-tracking strategy exists.

A deep semantic comparison at controlled checkpoints is acceptable.

# Validation UX

Expose structured validation issues.

Group by:

* general,
* repeat,
* group mapping,
* override,
* missing pack,
* missing sound set.

Each issue should include:

```csharp
Code
Message
Severity
Path or field identifier
```

The UI should:

* show a summary,
* highlight relevant fields where practical,
* disable save or activation on fatal validation errors,
* permit warning-only saves if current backend semantics allow,
* never hide an invalid existing reference.

Do not surface raw exceptions as validation messages.

# Save behavior

Required flow:

```text
Working copy
    ↓
Validate
    ↓
Persist atomically
    ↓
Refresh repository snapshot
    ↓
Clear dirty state
```

If the profile is currently active and changes affect runtime behavior, offer or automatically perform:

```text
Save and apply
```

Recommended commands:

* Save
* Save and activate
* Revert

If current selected profile is active:

* Save should persist only.
* Save and activate should persist and run the full existing activation flow.
* A dedicated `Apply` command is acceptable if clearer.

Do not mutate the active runtime before successful activation.

# Activation behavior

When user activates a profile:

1. validate profile,
2. ensure saved state is current,
3. prompt for unsaved changes if needed,
4. prepare required samples,
5. verify cache,
6. activate compiled snapshot,
7. persist active profile ID,
8. refresh UI state,
9. preserve previous profile on failure.

Show activation progress.

Disable duplicate activation commands while one is running.

# Import profile JSON

Implement profile import through a file picker or complete service integration.

Required flow:

1. select JSON file,
2. read safely,
3. deserialize using supported schema,
4. validate profile,
5. reject malformed or unsupported schema,
6. handle duplicate profile ID explicitly:

   * cancel,
   * import as copy,
   * replace non-built-in profile.
7. never replace built-in fallback profile,
8. persist atomically,
9. refresh list,
10. do not activate automatically unless explicitly chosen.

If importing as copy:

* assign a new ID,
* preserve profile name with a suffix,
* deep-copy all mappings and overrides.

# Export profile JSON

Allow exporting the selected saved profile.

Requirements:

* export a stable versioned JSON schema,
* do not include transient runtime/cache state,
* do not include absolute sound-pack paths,
* do not include device handles,
* write atomically where practical,
* allow user-selected destination,
* preserve the repository’s serialization settings.

# File-picker integration

Use Avalonia storage-provider APIs.

Requirements:

* no platform-specific file dialog code in view models,
* expose picker abstraction where practical,
* handle user cancellation without error,
* keep paths out of domain models,
* use async commands,
* do not block UI thread.

# View-model structure

Suggested structure:

```text
ProfilesPageViewModel
    ├── ObservableCollection<ProfileListItemViewModel>
    ├── SelectedProfile
    ├── Editor
    ├── CreateCommand
    ├── DuplicateCommand
    ├── RenameCommand
    ├── DeleteCommand
    ├── ImportCommand
    ├── ExportCommand
    └── ActivateCommand

ProfileEditorViewModel
    ├── General settings
    ├── Repeat settings
    ├── Group editors
    ├── Override editors
    ├── Validation issues
    ├── SaveCommand
    ├── SaveAndActivateCommand
    └── RevertCommand
```

Adapt to current MVVM style.

Do not create giant monolithic view models if current architecture supports smaller composed ones.

# Concurrency and command gating

Serialize or gate:

* save,
* save-and-activate,
* activation,
* import,
* export,
* delete,
* duplicate,
* preview,
* key capture.

Requirements:

* no duplicate execution,
* cancel where practical,
* shutdown rejects new operations,
* in-flight operations complete or cancel deterministically,
* no monitor lock held across await,
* no UI deadlock.

Use existing lifecycle and operation-gating patterns.

# Dashboard integration

Update dashboard to show:

* active profile name,
* saved profile count,
* current dirty editor state if relevant,
* profile validation status,
* currently loaded pack/sample count,
* decoded sample memory.

Keep dashboard concise.

# UI quality

The UI should be usable and coherent.

Requirements:

* consistent spacing,
* clear section hierarchy,
* compact but readable controls,
* no raw enum names when human-friendly labels are better,
* tooltips for technical settings,
* disabled controls visibly disabled,
* loading/progress state,
* validation summary,
* empty-state messages,
* confirmation dialogs for destructive actions.

Do not spend excessive effort on final branding or animation.

# Accessibility

At minimum:

* keyboard navigable controls,
* meaningful labels,
* no critical information conveyed by color alone,
* descriptive button text,
* sensible tab order,
* validation text readable by assistive technologies where Avalonia support permits.

# Persistence and recovery

Use existing atomic repositories.

Expand recovery behavior where needed:

* malformed profile file does not crash list loading,
* invalid profile appears with invalid status or is reported separately,
* valid profiles still load,
* backup recovery is attempted if supported,
* built-in fallback remains available,
* deleting a malformed user profile is still possible through a safe administrative path.

Do not silently discard invalid profiles.

# Testing requirements

Expand the test suite.

## Editable session

Test:

1. working copy isolated from original,
2. nested group change marks dirty,
3. override add marks dirty,
4. override remove marks dirty,
5. revert restores saved state,
6. successful save clears dirty,
7. failed save retains dirty,
8. switching profile resets baseline.

## Profile list operations

Test:

1. create from fallback,
2. create from selected profile,
3. duplicate deep-copies mappings,
4. duplicate deep-copies overrides,
5. rename changes display name but not ID,
6. built-in fallback delete rejected,
7. active profile delete rejected,
8. non-active profile delete succeeds,
9. selection updates after delete.

## Save and activation

Test:

1. valid save persists,
2. invalid save rejected,
3. save does not activate automatically,
4. save-and-activate uses activation pipeline,
5. activation failure preserves previous active profile,
6. active profile ID persists only after success,
7. concurrent activation prevented,
8. shutdown rejects activation.

## Validation

Test:

1. missing pack shown,
2. missing sound set shown,
3. invalid repeat interval shown,
4. out-of-range volume shown,
5. invalid override shown,
6. warning-only profile behavior,
7. validation paths identify correct group/field.

## Group editor

Test:

1. changing pack refreshes sound sets,
2. invalid existing set remains visible,
3. selecting valid set clears validation,
4. preview command uses selected set,
5. preview disabled when selection invalid.

## Key capture

Test:

1. captures one key,
2. stores scan code and extended flag only,
3. ignores subsequent events,
4. cancel exits capture,
5. repeat event does not create multiple captures,
6. no sequence retained,
7. capture does not write logs or persistence.

Use fake input-source events.

## Overrides

Test:

1. add override,
2. edit override,
3. remove override,
4. suppress override,
5. optional fields remain optional,
6. duplicate same key updates existing override rather than duplicating,
7. label formatting for known and unknown keys.

## Import/export

Test:

1. valid import,
2. malformed JSON rejected,
3. unsupported schema rejected,
4. duplicate ID cancel,
5. duplicate ID import-as-copy,
6. built-in replacement rejected,
7. export excludes runtime state,
8. export round-trips,
9. import cancellation harmless,
10. export cancellation harmless.

Use temporary directories and fake picker abstractions.

## Command concurrency

Test:

1. double-save prevented,
2. double-activate prevented,
3. save during delete prevented,
4. preview during shutdown rejected,
5. key capture during shutdown rejected,
6. concurrent profile switches serialized.

## View-model state

Test:

1. active profile indicator updates,
2. progress state updates,
3. validation summary updates,
4. dirty indicator updates,
5. command enabled states update.

Do not require full Avalonia rendering tests for all behavior; keep most logic testable in view models and services.

# Manual verification

After implementation, manually verify:

1. Profiles page opens.
2. Fallback profile appears.
3. New profile can be created.
4. Profile can be duplicated.
5. Profile can be renamed.
6. Non-active profile can be deleted.
7. Active profile deletion is blocked.
8. Built-in fallback deletion is blocked.
9. General settings can be edited.
10. Repeat mode can be changed.
11. Every key group can be edited.
12. Sound-pack selector works.
13. Sound-set selector updates by pack.
14. Missing pack/set is shown clearly.
15. Preview key-down works.
16. Preview key-up works.
17. Per-key capture captures one key.
18. Override can be saved.
19. Override affects playback after activation.
20. Dirty indicator appears.
21. Revert restores saved values.
22. Save persists after restart.
23. Save and activate changes runtime behavior.
24. Failed activation preserves old profile.
25. Import valid profile works.
26. Import duplicate handling works.
27. Exported profile can be re-imported.
28. Closing during save exits cleanly.
29. Closing during activation exits cleanly.
30. No orphan process remains.
31. No keyboard sequence or key data appears in logs.

# Performance requirements

Preserve:

* no disk access on keypress path,
* no JSON parsing on keypress path,
* no UI dispatch per key event,
* immutable runtime snapshot,
* bounded cache and voices,
* current working-set target.

Profile editor operations may use normal async disk I/O.

Do not preload every pack solely to populate selectors.

Catalog metadata is sufficient.

# Acceptance criteria

This slice is complete only when:

* full profile CRUD is available,
* active and fallback deletion rules are enforced,
* editable profiles are isolated from runtime snapshots,
* dirty tracking works,
* save and revert work,
* validation is visible and actionable,
* all key groups can be configured,
* key-down and key-up sets can be selected,
* repeat settings can be edited,
* pitch and volume variation can be edited,
* one-key override capture works,
* profile preview works,
* import and export work,
* activation uses existing prepare/verify/activate/persist/release flow,
* failed activation preserves previous runtime state,
* no real-time path regression occurs,
* shutdown remains deterministic,
* Release build succeeds,
* all tests pass,
* no keyboard data is logged or persisted beyond explicit one-key override identity.

# Deliverables

When complete, provide:

1. UI architecture summary.
2. Editable-session design.
3. Profile CRUD behavior.
4. Validation UX.
5. Key-group editor behavior.
6. Per-key capture behavior.
7. Import/export behavior.
8. Files added.
9. Files modified.
10. Packages added or changed.
11. Test count and results.
12. Release build result.
13. Manual verification results.
14. Working-set measurement.
15. Known limitations.
16. Remaining Phase 2 work.

Start by inspecting the current profile repository, compiler, activation service, and Avalonia MVVM structure. Implement the editable session and profile CRUD services first, then view models, then views, then key capture, then import/export.
