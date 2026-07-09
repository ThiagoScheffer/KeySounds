You are working inside an existing Avalonia + C# Windows keyboard-sound application.

Phase 3 core distribution/hardening has been implemented.

Current verified state:

* Release build passes with 0 warnings/errors.
* Tests pass: 84/84.
* Settings schema upgraded to v4.
* Single-instance guard implemented with per-user mutex and named-pipe activation.
* Tray icon/menu implemented.
* Close-to-tray and minimize-to-tray implemented.
* Tray Exit performs deterministic shutdown.
* Start with Windows implemented using HKCU Run through an abstraction.
* Settings/About UI added.
* Sanitized diagnostics export implemented as JSON.
* Local sanitized error-report service implemented.
* Documentation added:

  * `docs/packaging.md`
  * `docs/privacy.md`
  * `docs/soundpack-format.md`
  * `docs/troubleshooting.md`
  * updated `docs/release-checklist.md`
* No packages changed.
* Privacy scan found no keyboard-data logging calls.

Known remaining limitations:

* Tray behavior needs manual Windows verification.
* Startup registration needs manual Windows verification.
* Second-instance focus/activation needs manual Windows verification.
* Portable publish output needs manual publish-run verification.
* Diagnostics export is JSON, not multi-file ZIP.
* Installer/MSIX/WiX/Inno package is not implemented.

Your task is to perform a **Phase 3 verification and packaging-readiness pass**.

Do not add new product features unless required to fix verification failures.

## Objectives

1. Verify tray behavior on Windows.
2. Verify close-to-tray and minimize-to-tray.
3. Verify tray Exit performs real shutdown.
4. Verify single-instance behavior and second-instance activation.
5. Verify HKCU Start with Windows registration.
6. Verify `--startup` launch behavior.
7. Verify sanitized diagnostics export.
8. Verify local error-report behavior where practical.
9. Verify portable publish output.
10. Fix any bugs found.
11. Update documentation with actual verified commands and known limitations.
12. Keep Release build clean.
13. Keep all tests passing.

## First action

Run:

```bash id="mej7y3"
dotnet build KeySounds.csproj -c Release
dotnet test KeySounds.Tests\KeySounds.Tests.csproj -c Release
```

Then inspect:

* `App.axaml.cs`
* single-instance guard implementation
* named-pipe activation code
* tray service/view model
* close/minimize window lifecycle code
* startup registration service
* settings migration v4
* diagnostics export service
* error-report service
* publish configuration
* `docs/packaging.md`
* `docs/release-checklist.md`

Do not rewrite stable systems unless a concrete issue is found.

---

# Manual verification checklist

Perform the following checks and record results.

## Tray behavior

Verify:

1. Tray icon appears after app starts.
2. Tray tooltip is correct.
3. Tray menu opens.
4. Open menu item shows/focuses main window.
5. Sounds Enabled/Disabled toggles the same global sound state as Dashboard.
6. Active Profile item displays correct profile name.
7. Open Sound Packs opens/focuses the Sound Packs page.
8. Export Diagnostics works from tray.
9. Exit performs true deterministic shutdown.
10. Process exits after tray Exit.

If any behavior cannot be manually verified in the execution environment, update `docs/release-checklist.md` with exact manual verification steps.

## Close/minimize behavior

Verify:

1. With `CloseToTray = true`, closing window hides it instead of exiting.
2. Sounds/input continue while hidden.
3. Open from tray restores window.
4. With `CloseToTray = false`, closing window exits.
5. With `MinimizeToTray = true`, minimizing hides to tray.
6. With `MinimizeToTray = false`, minimizing behaves normally.
7. Settings changes apply without restart if intended.
8. No duplicate shutdown begins during hide-to-tray.

## Single-instance behavior

Verify:

1. Launch first instance.
2. Launch second instance.
3. Second instance exits.
4. First instance receives activation signal.
5. First instance window opens/focuses.
6. No second Raw Input listener remains.
7. No second audio engine remains.
8. No second process remains after a short delay.
9. If first instance is tray-hidden, second launch restores it.
10. Abandoned mutex scenario is handled if practically testable.

If focus stealing is limited by Windows policy, document the exact observed behavior.

## Start with Windows

Verify:

1. Enabling setting writes HKCU Run value.
2. Value points to current executable.
3. Path is quoted correctly.
4. Value includes `--startup` if designed.
5. Disabling removes Run value.
6. Registry failure rolls back setting if testable through fake service.
7. App launched with `--startup` follows intended startup visibility behavior.
8. No admin rights are required.

Do not leave unwanted startup registration enabled after testing.

## Diagnostics export

Verify:

1. Export creates JSON report.
2. Report opens as valid JSON.
3. Report includes useful operational summaries.
4. Report excludes typed text.
5. Report excludes key sequences.
6. Report excludes transient Raw Input handles.
7. Report excludes raw HID paths.
8. Report excludes full per-key override scan-code details.
9. Report includes app version/build info.
10. Report includes settings schema version.
11. Report includes profile/sound-pack/audio/keyboard summaries.
12. Export failure is shown as a controlled status.

## Local error reports

Where practical, trigger a controlled test exception through a dev/test path.

Verify:

1. Error report is created only if enabled.
2. Disabled setting prevents report creation.
3. Report is sanitized.
4. Retention cleanup removes old reports.
5. No keyboard data appears.

Do not introduce a user-facing crash button in production UI unless clearly developer-only.

## Portable publish

Run a portable publish.

Suggested command:

```bash id="67sdhe"
dotnet publish KeySounds.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish\win-x64-portable
```

Then verify:

1. Publish succeeds.
2. Output launches.
3. Avalonia assets load.
4. Tray icon loads.
5. Fallback sound pack materializes/loads.
6. Settings are stored in `%LocalAppData%\KeySounds`, not publish folder.
7. Sound packs import.
8. Diagnostics export works.
9. No test assemblies are included unless unavoidable.
10. Publish folder does not include source-only files.
11. App version metadata appears correctly where applicable.

Measure publish folder size.

---

# Automated test additions

Add tests only where they provide value without brittle UI automation.

## Single-instance tests

Add or strengthen tests for:

1. first instance ownership acquired,
2. second instance detected,
3. second instance sends activation signal,
4. activation handler invoked,
5. unknown named-pipe command rejected,
6. disposal releases ownership,
7. abandoned ownership recovery behavior where practical.

## Startup registration tests

With fake registry abstraction, verify:

1. enable writes correct Run value,
2. disable removes value,
3. quoted path with spaces,
4. `--startup` argument included,
5. write failure rolls settings back,
6. no machine-wide registry path used.

## Tray lifecycle tests

Using abstractions where possible, verify:

1. close-to-tray prevents actual shutdown,
2. tray Exit requests actual shutdown,
3. open restores window state,
4. global enable toggle uses shared settings/service.

## Diagnostics tests

Verify:

1. diagnostics JSON is valid,
2. forbidden fields are redacted,
3. report includes required sections,
4. profile override count is allowed but scan-code values are omitted,
5. keyboard summaries omit transient handles and raw HID paths,
6. export cancellation is harmless,
7. export failure returns controlled `OperationStatus`.

## Publish sanity script

If practical, add a script or documented command, not necessarily a unit test:

```text id="gp5fqc"
scripts/publish-portable.ps1
```

It should:

* clean previous publish output,
* run Release publish,
* print output size,
* print executable path.

Do not add a complex installer pipeline yet.

---

# Documentation updates

Update `docs/release-checklist.md` with actual observed results or unchecked manual steps.

Update `docs/packaging.md` with:

* exact portable publish command,
* output folder,
* expected executable,
* known publish-size range if measured,
* app data location,
* startup registry key,
* uninstall cleanup notes,
* note that no installer is currently provided.

Update `docs/troubleshooting.md` with:

* tray icon not visible,
* app appears to keep running after close,
* disabling close-to-tray,
* startup launch troubleshooting,
* second launch only focuses existing app,
* diagnostics export location.

Update `docs/privacy.md` if diagnostics/error-report behavior needs more exact wording.

---

# Bug-fix priorities

If issues are found, prioritize:

1. duplicate process/Raw Input/audio instances,
2. shutdown not actually exiting,
3. startup registry writing wrong path,
4. tray menu state desynchronization,
5. settings corruption,
6. diagnostics privacy leakage,
7. portable publish missing assets,
8. crash on hidden/tray state,
9. UI operation exceptions.

Avoid adding new features.

---

# Performance and measurements

Measure and report:

* normal startup to window visible,
* startup with `--startup`,
* second-instance activation time,
* tray Exit shutdown time,
* portable publish folder size,
* diagnostics export time,
* working set after startup visible,
* working set hidden to tray.

Approximate measurements are acceptable if clearly labeled.

---

# Acceptance criteria

This pass is complete only when:

* Release build still passes,
* all tests still pass,
* tray behavior is manually verified or documented as requiring external verification,
* close/minimize-to-tray behavior is verified or documented,
* single-instance behavior is verified or documented,
* Start with Windows behavior is verified or documented,
* portable publish succeeds or blockers are documented,
* diagnostics export is verified sanitized,
* documentation reflects actual behavior,
* no keyboard data is logged or exported,
* no Phase 2 regression is introduced.

# Deliverables

When complete, provide:

1. Verification summary.
2. Bugs found and fixed.
3. Manual checks completed.
4. Manual checks not possible.
5. Files added.
6. Files modified.
7. Packages added or changed.
8. Test count and result.
9. Release build result.
10. Portable publish result.
11. Publish folder size.
12. Runtime measurements.
13. Privacy/diagnostics audit result.
14. Updated documentation summary.
15. Remaining release blockers.
