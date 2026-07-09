You are working inside an existing Avalonia + C# Windows keyboard-sound application.

The application has completed:

* Phase 1 technical proof
* Phase 1 lifecycle hardening
* Phase 2 runtime foundations
* Phase 2B sound-pack infrastructure
* Phase 2C profile management/editor UI
* Phase 2D audio-device selection and resilient switching
* Phase 2E stable Raw Input device identity and per-keyboard profile assignment

Your task is to implement:

# Phase 2F — Sound-pack management UI and end-to-end release hardening

## Objective

Finish the Phase 2 MVP by implementing the full user-facing sound-pack management workflow and performing an end-to-end release-hardening pass across profiles, sound packs, audio, keyboard identity, persistence, shutdown, privacy, and runtime behavior.

After this phase, the application should feel like a coherent Windows MVP rather than a collection of technical slices.

This phase must deliver:

1. Complete Sound Packs UI.
2. Import local sound-pack folder workflow.
3. Replace/cancel conflict handling UI.
4. Remove-pack workflow with dependency protection.
5. Pack validation issue display.
6. Sound-set/sample preview UI.
7. Profile dependency visibility.
8. Invalid/missing pack recovery paths.
9. End-to-end startup/restart persistence validation.
10. End-to-end shutdown stress validation.
11. Storage cleanup.
12. Logging/privacy audit.
13. Performance and memory measurement.
14. UX polish across Dashboard, Profiles, Audio, and Keyboards.
15. Test expansion for complete user workflows.

Do not implement cloud sync, marketplace, online downloads, ZIP import unless already trivial, updater, installer, code signing, procedural synthesis, cross-platform support, telemetry, or tray behavior unless already present.

---

# First action: inspect the repository

Before editing:

1. Inspect the current SoundPack services:

   * validation
   * catalog
   * import
   * removal
   * preview
   * cache
2. Inspect existing Dashboard, Profiles, Audio, and Keyboards pages.
3. Inspect current navigation structure.
4. Inspect current file/folder picker abstractions.
5. Inspect current profile dependency checks.
6. Inspect pack-removal safeguards.
7. Inspect persistent storage path handling.
8. Inspect settings/profile migration services.
9. Inspect current logging.
10. Inspect all tests.
11. Run or inspect current Release build assumptions.

Do not weaken any existing lifecycle, privacy, or real-time guarantees.

---

# Scope

Implement and harden:

```text
Sound Packs page
Import folder UI
Conflict resolution UI
Pack validation display
Pack removal UI
Pack preview UI
Pack dependency display
Storage cleanup commands/services
Cross-page dependency/status updates
End-to-end hardening tests
Startup/restart/persistence validation
Shutdown stress validation
Privacy/logging audit
Performance measurement
UI polish
```

Do not implement:

```text
Online pack marketplace
Account system
Cloud sync
Automatic updates
Native installer
Code signing
Procedural synthesis
Firmware integration
MIDI
Per-application foreground rules
Cross-platform device backends
```

---

# Sound Packs page

Create or complete a dedicated `Sound Packs` page.

The page must display:

* installed pack list,
* built-in versus user-installed marker,
* pack ID,
* pack name,
* author,
* description,
* validation status,
* schema version,
* sound-set count,
* sample count,
* estimated decoded memory,
* currently loaded sample count,
* whether the pack is required by:

  * active global profile,
  * keyboard-assigned profiles,
  * saved profiles,
  * current preview/cache,
* invalid pack warning,
* removal eligibility.

## Pack list behavior

The pack list should support:

* refresh,
* select pack,
* import folder,
* remove selected pack,
* preview selected sound set,
* preview selected sample where practical,
* open referencing profiles where practical,
* show validation issues.

Do not decode all samples merely to populate the list. Use catalog metadata and validation estimates.

---

# Selected pack details

When a pack is selected, show:

```text
Name
ID
Author
Description
Schema version
Location type: Built-in / User-installed
Validation status
Sample count
Estimated decoded memory
Loaded sample count
Referencing profiles
Referencing keyboard assignments
Sound sets
Validation issues
```

For invalid packs:

* show issues grouped by severity,
* show actionable message,
* allow removal if user-installed and safe,
* prevent use in profiles,
* do not crash the page.

For built-in fallback pack:

* clearly mark as protected,
* removal disabled,
* replacement disabled.

---

# Sound-set list

For selected valid pack, display sound sets.

For each sound set:

* sound-set ID,
* name,
* sample count,
* referenced files,
* loaded/unloaded state,
* preview button.

Preview must use the existing `SoundPreviewService` and existing cache/mixer.

Do not read WAV files directly from the view model.

---

# Sample preview

Where practical, display individual samples for a selected sound set.

For each sample:

* relative sample path or friendly file name,
* duration if available from validation metadata,
* decoded-size estimate if available,
* preview button.

If individual sample metadata is not currently stored, implement it in the validation/catalog result rather than rereading the file repeatedly from the UI.

Preview behavior:

* first preview may prepare the sample or set,
* subsequent preview should use cache,
* preview is disabled while importing/removing/shutting down,
* preview failures are controlled and visible,
* rapid clicks must not create unbounded voices or tasks.

---

# Import folder workflow

Implement user-facing folder import through Avalonia storage-provider APIs.

Requirements:

1. User clicks `Import sound pack`.
2. Folder picker opens.
3. User cancellation is harmless.
4. Selected folder is passed to existing import service.
5. Source folder is never modified.
6. Validation issues are shown if import fails.
7. If pack ID already exists:

   * show conflict dialog/prompt,
   * allow Cancel,
   * allow Replace if not built-in,
   * prevent replacing built-in fallback pack.
8. Import uses the existing transactional service.
9. UI shows progress/busy state.
10. Catalog refreshes after success.
11. Imported pack appears selected after import.
12. Any profiles that can now resolve missing pack references update their validation status.

No import logic belongs in code-behind.

Use a file/folder picker abstraction so view models remain testable.

---

# Conflict resolution

Use the existing service-level conflict policies.

UI must support:

```text
Cancel
Replace
```

If import-as-copy already exists for profile import, do not confuse it with sound-pack replacement unless the service explicitly supports it.

Behavior:

* duplicate pack ID defaults to Cancel,
* Replace disabled for built-in pack,
* Replace warns that existing user-installed pack files will be replaced,
* replacement failure restores previous pack,
* success refreshes catalog and sample cache state.

---

# Pack removal workflow

Implement user-facing removal.

Rules:

* built-in pack cannot be removed,
* active global profile dependency blocks removal,
* keyboard-assigned profile dependency blocks removal,
* saved profile dependency blocks removal by default,
* invalid user-installed pack can be removed if no active runtime dependency exists,
* removal must use the existing transactional remove service,
* catalog refreshes after removal,
* cache releases pack entries when safe,
* UI selects a safe neighboring pack after removal.

When removal is blocked, show:

* blocking profiles,
* blocking keyboard assignments,
* active/global dependency,
* clear remediation:

  * change profile sound sets,
  * activate a different profile,
  * clear keyboard assignment,
  * delete/edit saved profile.

Do not silently modify profiles to permit removal.

---

# Dependency graph

Create a service or view-model projection that answers:

```text
Which profiles reference this pack?
Which profiles reference this sound set?
Is this pack required by the active global profile?
Is this pack required by any keyboard-assigned runtime profile?
Is this pack referenced only by inactive saved profiles?
Which keyboard assignments depend on it?
```

Use existing profile repositories and assignment settings.

Do not parse JSON on the keypress path.

This dependency graph is UI/service-layer only.

---

# Invalid/missing pack recovery

Profiles may reference packs that:

* are missing,
* are invalid,
* have missing sound sets,
* were removed,
* failed validation.

The UI should make this recoverable.

At minimum:

* Profiles page shows validation issue.
* Sound Packs page shows missing referenced pack IDs where applicable.
* User can import a pack with the missing ID to repair profiles.
* After catalog refresh/import, affected profiles revalidate.
* Active runtime profile is not replaced by invalid profile.
* Keyboard-assigned profiles fall back safely until repaired.

Do not silently rewrite profile references.

---

# Storage cleanup

Implement startup cleanup and optionally a user-facing maintenance command.

Clean:

* stale import temp directories,
* stale replacement backup directories when safe,
* abandoned atomic-write temp files,
* old cache temp files,
* invalid empty pack directories created by failed operations.

Do not delete:

* valid installed packs,
* user profiles,
* settings backups,
* unknown user files outside managed storage root,
* source folders used for import.

All cleanup must be constrained to the managed application root.

Expose a diagnostic result:

```text
Removed temp directories: N
Removed temp files: N
Skipped unsafe paths: N
```

---

# Cross-page synchronization

Ensure updates propagate coherently:

## After importing a pack

* Sound Packs page refreshes.
* Profiles page sound-pack selectors include the new pack.
* Invalid profile references may become valid.
* Dashboard pack/sample counts update.
* Keyboard-assigned profile preparation may retry if applicable.

## After removing a pack

* Sound Packs page refreshes.
* Profiles page validation updates.
* Dashboard warnings update.
* Active/assigned runtime profiles are preserved or fall back safely.
* Cache releases unused samples.

## After editing a profile

* Sound Packs page dependency view updates.
* Pack removal eligibility updates.
* Dashboard active-profile status updates.

## After assigning keyboard profile

* Sound Packs dependency view updates.
* Removal protection updates.

Use existing eventing or observable state patterns. Do not introduce global mutable static state.

---

# Dashboard polish

Update Dashboard to show a concise system summary:

* global sounds enabled,
* active global profile,
* active audio output,
* fallback audio status if applicable,
* connected keyboard count,
* keyboard-specific assignments count,
* installed sound-pack count,
* invalid pack count,
* loaded sample count,
* decoded sample memory,
* recent nonfatal warning, if any.

Keep it readable. Do not overload Dashboard with editor controls.

---

# Profiles page polish

Ensure Profiles page reflects pack-state changes:

* missing pack warning,
* missing sound-set warning,
* invalid pack warning,
* preview disabled if unresolved,
* save blocked on fatal validation,
* Save and Activate failure surfaces exact dependency issue,
* profile import/export remains stable.

Add “Open Sound Pack” action from a group mapping if easy and safe.

---

# Audio page polish

Ensure Audio page:

* displays effective output and configured output distinctly,
* test sound uses current selected output,
* shows loaded sample count and decoded memory,
* handles audio unavailable state gracefully,
* does not allow test sound during switch/unavailable states.

---

# Keyboards page polish

Ensure Keyboards page:

* shows effective profile,
* assigned profile,
* global fallback profile,
* assignment readiness,
* missing assigned profile warning,
* profile preparation error,
* profile assignment dependencies reflected in pack-removal blocking.

---

# Error and status model

Unify user-facing operation statuses where practical.

Do not show raw exceptions in the main UI.

Preferred structure:

```csharp
OperationStatus
{
    Severity
    Code
    Message
    Details?
}
```

Use for:

* import failure,
* validation failure,
* preview failure,
* remove blocked,
* activation failure,
* audio switch failure,
* identity resolution warning,
* persistence recovery.

Avoid creating a huge new framework if existing status patterns suffice.

---

# Logging and privacy audit

Perform a deliberate audit.

Requirements:

* no typed text logged,
* no key sequences logged,
* no scan-code history logged,
* no virtual-key history logged,
* no transient Raw Input handles persisted,
* no key-capture history persisted,
* no raw HID path persisted unless already justified,
* pack/profile IDs may be logged,
* audio endpoint IDs may be logged,
* stable keyboard identity IDs may be logged only bounded or intentionally,
* logs do not include sample file contents or raw JSON blobs.

Add tests or static scans where practical for known logging patterns.

---

# Persistence audit

Verify:

* settings schema migration is current,
* profiles survive restart,
* sound packs survive restart,
* keyboard assignments survive restart,
* audio settings survive restart,
* active profile survives restart,
* invalid profiles do not crash startup,
* invalid packs do not crash startup,
* missing active profile falls back safely,
* missing fallback pack is rematerialized,
* temp files are cleaned safely.

Add integration-style tests using temporary storage roots.

---

# Shutdown hardening

Stress shutdown during:

* sound-pack import,
* sound-pack replacement,
* sound-pack removal,
* catalog refresh,
* sample preview,
* profile save,
* profile activation,
* audio device switch,
* keyboard identity resolution,
* keyboard assignment preparation,
* settings save,
* profile export/import.

The application must:

* reject new operations after shutdown begins,
* cancel or await in-flight operations,
* not deadlock,
* not leave orphan threads,
* not leave corrupted settings/profiles,
* not leave partially installed packs as final packs,
* not crash from late UI updates.

---

# Test expansion

Expand automated tests across the full MVP.

## Sound Packs UI view models

Test:

1. catalog loads into list,
2. invalid packs appear with issues,
3. selected pack details populate,
4. built-in pack removal disabled,
5. user pack removal enabled when unreferenced,
6. removal blocked when referenced,
7. import success refreshes list,
8. import cancellation harmless,
9. duplicate conflict prompts policy,
10. replace blocked for built-in pack,
11. preview command disabled for invalid set,
12. preview command calls preview service for valid set,
13. busy state disables duplicate import/remove/preview.

## Import workflow

Test:

1. valid folder import through picker abstraction,
2. picker cancellation,
3. validation failure shown,
4. duplicate cancel,
5. duplicate replace,
6. replace failure restores old catalog,
7. imported pack selected after success,
8. profile validation refreshed after import.

## Removal workflow

Test:

1. unreferenced pack removed,
2. active-profile pack blocked,
3. keyboard-assigned pack blocked,
4. saved-profile pack blocked,
5. invalid user pack removable when safe,
6. built-in pack blocked,
7. catalog refresh after removal,
8. cache release requested after removal.

## Dependency graph

Test:

1. pack referenced by active profile,
2. pack referenced by inactive profile,
3. pack referenced by keyboard assignment,
4. missing pack reference detected,
5. sound-set-level dependency detected,
6. dependency updates after profile edit,
7. dependency updates after assignment clear.

## Cross-page synchronization

Test:

1. import updates profile selector options,
2. removal updates profile validation,
3. profile edit updates pack dependency view,
4. keyboard assignment updates pack dependency view,
5. dashboard counters update after catalog refresh.

## Persistence integration

Using temporary app roots, test:

1. first run materializes fallback pack/profile,
2. restart loads installed user pack,
3. restart preserves active profile,
4. restart preserves audio selection,
5. restart preserves keyboard assignment,
6. malformed pack does not prevent startup,
7. malformed profile does not prevent startup,
8. temp import directory cleaned,
9. atomic temp file cleaned,
10. settings backup recovery still works.

## Shutdown stress tests

Using fakes or controlled services, test shutdown during:

1. import,
2. replacement,
3. removal,
4. preview,
5. profile activation,
6. audio switch,
7. identity resolution,
8. assignment preparation.

Verify all operations complete/cancel predictably.

## Privacy/static scan tests

Add tests or validation scripts where practical to check:

1. settings do not contain transient handles,
2. keyboard assignments store stable IDs only,
3. logs do not include known test scan-code marker,
4. key-capture stores only single explicit override identity,
5. no sequence collection is persisted.

## End-to-end routing regression

Test:

1. global profile still works,
2. assigned keyboard profile still works,
3. missing assigned profile falls back,
4. removed pack cannot break active runtime,
5. imported repaired pack restores validation,
6. audio switching does not redecode samples,
7. preview uses selected output and cache.

---

# Manual verification

After implementation, manually verify:

## General startup

1. Fresh app data start works.
2. Fallback pack materializes or loads.
3. Fallback profile works.
4. Dashboard shows coherent counts.
5. Restart preserves settings.

## Sound packs

6. Sound Packs page opens.
7. Built-in fallback pack appears protected.
8. Valid pack imports.
9. Invalid pack shows validation errors.
10. Duplicate pack prompts conflict.
11. Replace works for user pack.
12. Replace blocked for built-in pack.
13. Preview sound set works.
14. Preview individual sample works if supported.
15. Unreferenced user pack removes.
16. Active-profile pack removal is blocked.
17. Keyboard-assigned pack removal is blocked.
18. Saved-profile pack removal is blocked.
19. Missing pack profile can be repaired by import.

## Profiles

20. Profile selectors update after import.
21. Profile validation updates after removal.
22. Save and activate works with imported pack.
23. Invalid references are visible and actionable.

## Audio

24. Test sound works after pack import.
25. Test sound works after output switch.
26. Audio unavailable state does not crash UI.

## Keyboards

27. Keyboard assignment still works.
28. Assigned profile dependency blocks pack removal.
29. Clearing assignment updates dependency view.

## Shutdown

30. Close during import.
31. Close during preview.
32. Close during removal.
33. Close during profile activation.
34. Close during audio switch.
35. Close during keyboard refresh.
36. Five immediate-close cycles pass.
37. No orphan process remains.

## Privacy

38. No typed text appears in logs.
39. No scan-code sequence appears in logs.
40. No transient Raw Input handle appears in settings.
41. No key-capture history appears in files.

Report any hardware-dependent checks that cannot be completed.

---

# Performance and memory measurements

Measure and report approximate values:

* cold startup to window-ready,
* fallback pack discovery time,
* catalog refresh time with installed packs,
* import time for a small pack,
* first preview preparation time,
* warm preview time,
* profile activation time using imported pack,
* audio switch time if already measured,
* idle working set on Dashboard,
* working set on Sound Packs page,
* working set after importing/loading a small pack,
* decoded sample bytes,
* loaded sample count.

Preserve targets:

```text
No disk access on prepared keypress path
No JSON parsing on prepared keypress path
No UI dispatch per key event
No unbounded collections
No unbounded task creation
No output reinitialization per trigger
No sample redecoding during audio-device switch
```

---

# Release-readiness checklist

Create or update a developer-facing checklist file, for example:

```text
docs/release-checklist.md
```

Include:

* build command,
* test command,
* manual audio checks,
* manual keyboard checks,
* sound-pack import checks,
* shutdown stress checks,
* privacy checks,
* storage reset instructions,
* known limitations,
* non-goals.

Do not include secrets, machine-specific paths, or private user data.

---

# UX polish requirements

Do a minimal but coherent polish pass:

* consistent page headings,
* consistent button labels,
* consistent spacing,
* clear empty states,
* clear busy states,
* destructive actions visually separated,
* validation messages readable,
* no raw enum names when user-friendly labels are appropriate,
* no unhandled exception text in ordinary UI,
* no confusing distinction between active/global/assigned/effective profiles,
* no confusing distinction between configured/effective audio output.

Do not spend excessive time on branding or animations.

---

# Accessibility basics

Ensure:

* buttons have clear text,
* input fields have labels,
* key controls are keyboard navigable,
* destructive buttons are not adjacent to safe primary actions without separation,
* validation does not rely on color alone,
* disabled controls have explanatory text or status where needed.

---

# Acceptance criteria

Phase 2F is complete only when:

* Sound Packs page is implemented,
* valid packs can be imported through UI,
* invalid packs display validation issues,
* duplicate pack conflicts are handled by UI,
* user packs can be removed when safe,
* protected/referenced packs cannot be removed unsafely,
* sound sets and samples can be previewed through existing audio/cache path,
* pack dependencies are visible,
* missing pack/profile problems are recoverable,
* cross-page state updates work,
* startup/restart persistence is verified,
* cleanup of stale temp files is implemented,
* shutdown stress remains deterministic,
* privacy/logging audit passes,
* release checklist exists,
* Release build succeeds with zero introduced warnings,
* all tests pass,
* no real-time path regression occurs,
* no keyboard data is logged or persisted beyond explicit single-key override identity.

---

# Deliverables

When finished, provide:

1. Sound Packs UI summary.
2. Import workflow behavior.
3. Conflict-resolution behavior.
4. Removal/dependency-protection behavior.
5. Preview behavior.
6. Invalid/missing pack recovery behavior.
7. Cross-page synchronization behavior.
8. Storage cleanup behavior.
9. UX polish summary.
10. Privacy/logging audit result.
11. Persistence audit result.
12. Shutdown stress result.
13. Files added.
14. Files modified.
15. Packages added or changed.
16. Test count and results.
17. Release build result.
18. Manual verification completed.
19. Performance and memory measurements.
20. Known limitations.
21. Remaining post-Phase-2 work.

Start by inspecting the existing sound-pack services and UI navigation. Implement the Sound Packs view model first, then UI, then dependency graph integration, then import/remove/preview command wiring, then cross-page refresh, then release-hardening tests and checklist.
