You are working inside an existing Avalonia + C# Windows keyboard-sound application.

The Phase 2 MVP is functionally complete.

Current verified state:

* Release build passes with 0 warnings/errors.
* Test suite passes: 75/75.
* Dedicated Raw Input keyboard capture exists.
* Audio engine and output-device switching exist.
* Profiles, profile editor, key groups, key-up/key-down routing, repeat modes, per-key overrides, and keyboard-specific assignments exist.
* Sound-pack import, validation, preview, dependency graph, and protected removal exist.
* Dashboard, Profiles, Audio, Keyboards, and Sound Packs pages exist.
* Release checklist exists at `docs/release-checklist.md`.
* Privacy/static scan found no keyboard-data logging.
* No packages were added in the last slice.

Known remaining gaps:

* Real hardware keyboard identity behavior not manually verified.
* Actual Avalonia GUI folder picker/dialog interaction not manually verified.
* Audio-output preview on physical devices not manually verified.
* Manual shutdown stress cycles not fully verified.
* Runtime memory/startup timing measurements not completed.
* Check if keys are correct being captured to play sounds
Your task is to perform a **Phase 2 Release Candidate hardening pass**.

Do not add major new product features.

## Objectives

1. Execute and document manual verification paths where possible.
2. Add diagnostic tooling that helps verify behavior without collecting keyboard data.
3. Harden GUI interaction paths.
4. Harden shutdown during all user-facing operations.
5. Measure startup, memory, and operation timings.
6. Review persistence files produced by the app.
7. Improve the release checklist.
8. Fix any issues found.
9. Keep Release build clean.
10. Keep all tests passing.

## First action

Inspect:

* `docs/release-checklist.md`
* all ViewModels for Dashboard, Profiles, Audio, Keyboards, and Sound Packs
* shutdown/lifecycle services
* file/folder picker abstractions
* operation status handling
* logging
* settings and profile persistence
* sound-pack import/removal services
* audio-device switching
* keyboard identity service
* current test suite

Then run:

```bash
dotnet build KeySounds.csproj -c Release
dotnet test KeySounds.Tests\KeySounds.Tests.csproj -c Release
```

Do not proceed with large changes until you understand the current baseline.

## Manual verification harness

Add a development-only diagnostics page or command-line diagnostic mode if the project structure supports it cleanly.

It must not record keyboard data.

Allowed diagnostics:

* app startup duration
* window-ready timestamp
* Raw Input service state
* audio engine state
* active output name
* profile count
* sound-pack count
* invalid pack count
* loaded sample count
* decoded sample bytes
* connected keyboard count
* identity resolution count
* operation-status history
* shutdown duration
* aggregate trigger count
* aggregate suppressed trigger count

Forbidden diagnostics:

* scan-code sequences
* virtual-key sequences
* typed characters
* key timings per key
* raw transient device handles in persisted files
* raw HID paths in persisted files unless already justified

If a diagnostics page is too much, implement a developer-only diagnostic export command that writes a sanitized JSON report.

## GUI interaction hardening

Review every user-facing async command.

For each command, verify:

* double-click cannot start duplicate operation,
* cancellation is harmless,
* exceptions become `OperationStatus`,
* UI remains responsive,
* command disabled state updates,
* shutdown rejects or cancels operation,
* no raw exception text is shown unless in diagnostic/developer mode.

Cover at least:

* sound-pack import
* sound-pack remove
* sound-pack preview
* profile save
* profile save-and-activate
* profile import
* profile export
* profile delete
* key capture
* keyboard assignment apply/clear
* audio output refresh
* audio output apply
* audio retry
* test sound
* storage cleanup

## Folder picker and dialog verification

Harden picker abstractions.

Requirements:

* user cancellation returns a controlled result,
* inaccessible folder returns a controlled error,
* selecting a non-pack folder shows validation issues,
* duplicate pack conflict prompt cannot be bypassed accidentally,
* Replace action clearly indicates destructive replacement,
* replacing built-in fallback pack remains impossible,
* dialog services are mockable in tests,
* no picker logic is embedded in core services.

Add tests with fake picker/dialog services.

## Shutdown stress hardening

Add or improve automated stress tests using fake services.

Test shutdown during:

* sound-pack import waiting on picker
* sound-pack import copying files
* duplicate conflict prompt
* sound-pack removal
* sample preview
* profile save
* profile activation
* profile import/export
* keyboard identity resolution
* keyboard assignment preparation
* audio output switching
* audio endpoint refresh
* key capture mode

Requirements:

* no deadlock,
* no unobserved task exception,
* no partial final sound-pack install,
* no corrupted settings/profile files,
* no orphan background task in test harness,
* shutdown operation remains idempotent.

## Runtime measurement

Add a small internal measurement utility or scripted measurement instructions.

Measure and report:

* cold startup to window-ready
* warm startup to window-ready
* dashboard working set
* profiles page working set
* audio page working set
* keyboards page working set
* sound packs page working set
* fallback profile activation time
* sound-pack catalog refresh time
* small pack import time
* first preview preparation time
* warm preview time
* audio endpoint refresh time
* audio switch time where hardware allows
* shutdown duration

Use approximate measurements if necessary, but label them as approximate.

Do not add telemetry.

## Persistence audit

Using a temporary or clean app-data root, verify produced files.

Check:

* `settings.json`
* settings backup
* profile files
* sound-pack folders
* fallback pack materialization
* imported pack layout
* temp folder cleanup
* no transient Raw Input handles
* no key sequences
* no typed text
* no raw logs with keyboard data
* no absolute source import paths in pack manifests
* no source folder modifications after import

Add tests where practical.

## Keyboard hardware verification

If physical verification is possible, document results.

Verify:

1. connected keyboard appears on Keyboards page,
2. stable identity persists after app restart,
3. assigning a profile changes that keyboard’s sound,
4. clearing assignment restores global profile,
5. reconnect behavior,
6. two-keyboard behavior if available,
7. ambiguous-identical-device behavior if possible.

If not possible, update the release checklist with exact manual steps.

## Audio hardware verification

If physical verification is possible, document results.

Verify:

1. default output test sound,
2. specific output selection,
3. restart preserves configured output,
4. device switch preserves sample cache,
5. failed switch rollback where possible,
6. disconnect fallback,
7. retry configured device after reconnect,
8. latency setting changes.

If not possible, update the release checklist with exact manual steps.

## Sound-pack end-to-end verification

Create or use a tiny generated local test pack.

Verify:

1. import valid pack,
2. preview sound set,
3. preview sample,
4. create profile using imported pack,
5. save and activate profile,
6. play keyboard sound with imported pack,
7. removal blocked while active,
8. switch away,
9. removal succeeds,
10. profile validation updates after removal,
11. re-import repairs validation.

The test pack should use original/generated WAV files only.

Do not add copyrighted assets.

## Release checklist update

Update `docs/release-checklist.md`.

It must include:

* build command,
* test command,
* clean app-data test,
* upgrade/migration test,
* keyboard capture test,
* key-repeat test,
* key-up sound test,
* profile editor test,
* sound-pack import test,
* invalid pack test,
* sound-pack removal/dependency test,
* audio output switch test,
* disconnect/reconnect test,
* keyboard assignment test,
* shutdown stress test,
* privacy file audit,
* logging audit,
* known limitations,
* rollback plan,
* release-blocking issues.

## Bug fixing

Fix any issues found during this pass.

Prioritize:

1. shutdown/lifecycle failures,
2. data corruption,
3. privacy leaks,
4. crashes,
5. real-time path regressions,
6. audio-device failures,
7. profile/pack activation failures,
8. UI operation deadlocks,
9. confusing but nonfatal UX issues.

Do not add new advanced features.

## Performance constraints

Preserve:

* no disk access on prepared keypress path,
* no JSON parsing on prepared keypress path,
* no UI dispatch per key event,
* no sample redecoding during audio switch,
* no output reinitialization per trigger,
* bounded active voices,
* bounded caches,
* bounded operation histories,
* deterministic shutdown.

## Deliverables

When finished, provide:

1. Release-candidate hardening summary.
2. Bugs found and fixed.
3. Manual verification completed.
4. Manual verification not possible and why.
5. Runtime measurements.
6. Persistence audit result.
7. Privacy/logging audit result.
8. Shutdown stress result.
9. Files added.
10. Files modified.
11. Packages added or changed.
12. Test count and result.
13. Release build result.
14. Updated release checklist summary.
15. Remaining release blockers.
16. Recommended next phase.

## Acceptance criteria

This pass is complete only when:

* Release build passes with zero introduced warnings,
* all tests pass,
* release checklist is updated,
* GUI command paths are hardened,
* shutdown stress tests are expanded,
* persistence audit passes,
* privacy/logging audit passes,
* runtime measurements are reported,
* known manual hardware gaps are documented,
* no major new product scope is introduced.
