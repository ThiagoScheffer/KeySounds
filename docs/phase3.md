You are working inside an existing Avalonia + C# Windows keyboard-sound application.

The Phase 2 MVP is complete and release-candidate hardening has been finished.

Current application capabilities include:

* Avalonia desktop UI
* Dashboard, Profiles, Audio, Keyboards, and Sound Packs pages
* Dedicated hidden Win32 Raw Input window
* Global keyboard input outside application focus
* Deterministic startup and shutdown
* Bounded input pipeline
* Physical key grouping
* Key-down and key-up routing
* Repeat modes
* Per-key overrides
* Keyboard-specific profile assignment
* Stable Raw Input device identity where Windows metadata permits it
* Persistent NAudio WASAPI audio engine
* Audio output-device selection and switching
* Sound packs with strict validation
* Transactional sound-pack import/removal
* Profile CRUD and editor
* Pack-aware decoded sample cache
* Preview support
* Atomic persistence and migration
* Privacy protections
* Release checklist
* Passing Release build and automated tests

Your task is to implement:

# Phase 3 — Distribution, tray operation, startup integration, diagnostics, and production hardening

## Objective

Move the application from a local Windows MVP into a distributable desktop utility.

Phase 3 should focus on:

1. System tray behavior.
2. Minimize-to-tray behavior.
3. Start-with-Windows integration.
4. Single-instance behavior.
5. User-facing diagnostics export.
6. Crash/error reporting without keyboard data.
7. Installer/package preparation.
8. Portable build preparation.
9. Code-signing readiness.
10. Default bundled sound-pack strategy.
11. First-run experience.
12. Update strategy placeholder.
13. Documentation and operational hardening.

Do not implement cloud sync, online marketplace, procedural synthesis, AI sound generation, account systems, telemetry, cross-platform support, kernel drivers, or automatic updates unless explicitly scoped below.

---

# First action: inspect the repository

Before editing:

1. Inspect current application lifecycle and shutdown flow.
2. Inspect Avalonia application startup and window creation.
3. Inspect settings model and schema version.
4. Inspect navigation/view model composition.
5. Inspect persistence path services.
6. Inspect logging and operation status.
7. Inspect release checklist.
8. Inspect current build/publish configuration.
9. Inspect project file and package metadata.
10. Inspect test project and existing integration-style tests.

Preserve all existing real-time, privacy, persistence, and shutdown guarantees.

---

# Phase 3 scope

Implement:

```text id="1o582r"
Tray icon
Tray menu
Minimize/close-to-tray option
Start with Windows
Single-instance guard
Second-instance activation
Diagnostics export
Crash-safe local error report
Installer-ready publish profile
Portable publish profile
Bundled default sound-pack handling
First-run onboarding
Settings schema migration
Documentation updates
Release checklist expansion
```

Do not implement:

```text id="7s3mef"
Cloud telemetry
Remote crash upload
Auto-update download/install
Marketplace
User accounts
Online sound-pack repository
Procedural synthesis
Kernel driver
Firmware integration
macOS/Linux backend
Admin-required global hooks
```

---

# Settings schema

Increment the settings schema version.

Add settings such as:

```csharp id="0il4cf"
public bool StartWithWindows { get; set; }

public bool MinimizeToTray { get; set; } = true;

public bool CloseToTray { get; set; } = true;

public bool ShowWindowOnStartup { get; set; } = true;

public bool HasCompletedFirstRun { get; set; }

public bool EnableLocalCrashReports { get; set; } = true;

public int DiagnosticReportRetentionDays { get; set; } = 14;
```

Requirements:

* preserve all Phase 2 settings,
* migrate from previous schema safely,
* create backup before migration,
* migration is idempotent,
* failed migration does not destroy previous settings,
* defaults must be safe and unsurprising.

Recommended default behavior:

```text id="nv3n71"
StartWithWindows: false
MinimizeToTray: true
CloseToTray: true
ShowWindowOnStartup: true
Local crash reports: true
```

Do not enable Windows startup automatically without explicit user action.

---

# Tray behavior

Implement system tray operation using Avalonia tray APIs.

Tray icon requirements:

* visible while application is running,
* clear tooltip,
* menu item: Open KeySounds,
* menu item: Enable/Disable Sounds,
* menu item: Active Profile submenu or quick selector if practical,
* menu item: Audio Output status or open Audio page,
* menu item: Sound Packs,
* menu item: Diagnostics Export,
* menu item: Exit.

Minimum required tray menu:

```text id="2g3pyj"
Open
Sounds Enabled / Disabled
Active Profile: <name>
Open Sound Packs
Export Diagnostics
Exit
```

The tray toggle must update the same global sounds-enabled state used by the Dashboard.

The tray menu must reflect state changes from the UI.

Do not duplicate separate state.

---

# Minimize and close behavior

Implement settings-driven behavior.

## Minimize to tray

When enabled:

* minimizing main window hides it from taskbar or hides the window according to Avalonia/Windows best practice,
* tray icon remains,
* sounds continue running,
* clicking Open restores the window.

## Close to tray

When enabled:

* window close hides to tray instead of exiting,
* audio/input remain active,
* tray Exit performs actual shutdown.

## Actual exit

The app must truly exit when:

* user chooses Exit from tray,
* user chooses Exit from an in-app command if present,
* close-to-tray is disabled and the window is closed,
* shutdown is requested by OS/session ending.

Actual exit must await existing deterministic shutdown.

Do not reintroduce `async void` shutdown hazards.

---

# Single-instance behavior

Implement single-instance enforcement.

Requirements:

* only one instance of the app should run per user session,
* second launch should signal the existing instance to open/focus the main window,
* second instance should exit cleanly,
* no settings/profile corruption from two processes writing simultaneously,
* no duplicate Raw Input listeners,
* no duplicate audio engines.

Recommended approaches:

* named mutex plus local named pipe,
* named mutex plus Windows message activation,
* another robust per-user IPC mechanism.

Requirements:

* mutex name must be per-user or otherwise avoid cross-user conflict,
* second-instance IPC must not accept arbitrary commands beyond activation,
* handle stale abandoned mutex,
* preserve normal startup after crash,
* tests should cover single-instance logic through abstractions where practical.

---

# Start with Windows

Implement user-controlled startup registration.

Recommended method for normal desktop app:

```text id="kew9l2"
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

Requirements:

* per-user only,
* no admin privileges,
* setting can be enabled/disabled from UI,
* registration updates only after user changes setting,
* startup command points to the installed executable or current executable for portable builds,
* quote paths correctly,
* handle moved portable executable gracefully,
* failure surfaces clear status,
* setting rollback if registry update fails,
* no startup registration during tests unless using abstraction/fake.

Do not register scheduled tasks unless there is a strong reason.

---

# Startup behavior

Support startup modes.

When launched normally:

* show window if `ShowWindowOnStartup` is true,
* otherwise start minimized/tray-only.

When launched from Windows startup:

* ideally start tray-only if user selected that behavior,
* continue input/audio services.

When second instance launches:

* existing instance opens/focuses the window.

Do not require admin rights.

---

# First-run onboarding

Implement a lightweight first-run page/dialog.

It should explain:

* app runs locally,
* keyboard events are processed transiently,
* typed text is not stored,
* sound packs are local,
* global keyboard input is needed for background sounds,
* tray behavior,
* where to import custom sound packs,
* where to change audio output.

First-run actions:

* choose whether sounds are enabled,
* choose whether close-to-tray is enabled,
* choose whether start with Windows is enabled,
* open Sound Packs page,
* complete onboarding.

Requirements:

* onboarding appears only until completed,
* user can reopen it from Help/About if implemented,
* no dark patterns,
* start-with-Windows remains opt-in,
* completion persists atomically.

---

# Diagnostics export

Create a user-facing diagnostics export.

Purpose: support debugging without exposing keyboard data.

Export a ZIP or JSON folder report containing sanitized operational data.

Recommended output:

```text id="47qx9c"
diagnostics-YYYYMMDD-HHMMSS.zip
├─ report.json
├─ settings-redacted.json
├─ profiles-summary.json
├─ soundpacks-summary.json
├─ audio-summary.json
├─ keyboard-devices-summary.json
├─ recent-status.json
└─ app-log.txt
```

If ZIP support is not desirable, export a folder with these files.

## Diagnostics report may include

* app version,
* OS version,
* .NET runtime version,
* process architecture,
* uptime,
* settings schema version,
* enabled/disabled state,
* active profile ID/name,
* profile count,
* invalid profile count,
* sound-pack count,
* invalid sound-pack count,
* loaded sample count,
* decoded sample bytes,
* audio configured/effective output names,
* audio engine state,
* audio latency,
* keyboard device count,
* keyboard identity reliability categories,
* keyboard assignment count,
* recent operation statuses,
* recent exception summaries,
* storage root size,
* temp cleanup report,
* test sound result if user triggers it separately.

## Diagnostics report must not include

* typed text,
* key sequences,
* scan-code histories,
* virtual-key histories,
* per-key timing,
* transient Raw Input handles,
* raw HID device paths if avoidable,
* full stable keyboard identity hashes unless user explicitly enables detailed diagnostics,
* absolute source import folders,
* WAV file contents,
* profile override scan codes unless explicitly redacted or summarized.

For profile summaries, include:

* profile ID,
* profile name,
* validation status,
* referenced pack IDs,
* referenced sound-set IDs.

Do not include per-key override scan codes in ordinary diagnostics. Instead include override count.

---

# Local crash/error reporting

Implement local-only error capture.

Scope:

* unhandled managed exceptions,
* unobserved task exceptions,
* critical startup failures,
* critical shutdown failures,
* audio initialization failures,
* Raw Input initialization failures,
* persistence recovery failures.

Requirements:

* write local crash/error report only when enabled,
* store under managed app data, e.g.:

```text id="kzla86"
%LocalAppData%\KeySounds\reports\
```

* sanitize reports,
* no keyboard data,
* no key event values,
* no transient handles,
* no raw full profile JSON unless redacted,
* retention cleanup based on setting,
* no network transmission,
* UI can show “A local error report was created”,
* diagnostics export can include sanitized recent error summaries.

Do not implement remote crash upload.

---

# Logging

If logging exists, standardize it.

Requirements:

* structured enough for diagnostics,
* rolling or bounded local log,
* retention limit,
* no keyboard data,
* no key sequences,
* no scan-code values,
* no transient handles,
* no raw sample contents,
* no excessive profile JSON dumps,
* no unbounded log growth.

Allowed:

* app lifecycle events,
* operation status,
* pack/profile IDs,
* audio endpoint status,
* keyboard-device count and reliability category,
* sanitized exception type/message,
* aggregate counters.

---

# Installer-ready publishing

Add publish configuration and documentation for:

## Portable build

Self-contained folder or single-file build.

Recommended:

```bash id="bcx6ws"
dotnet publish KeySounds.csproj -c Release -r win-x64 --self-contained true
```

Decide whether single-file is reliable for Avalonia assets and current file access. If not, document folder deployment.

## Installer-ready build

Prepare output suitable for WiX, Inno Setup, or MSIX.

At minimum:

* stable executable name,
* application icon,
* version metadata,
* company/product metadata,
* clean output folder,
* required assets included,
* default bundled sound packs included or materialized,
* no test files included,
* no local user data included.

Do not implement full installer unless explicitly straightforward.

Recommended deliverable:

```text id="52ghr0"
docs/packaging.md
```

Include:

* portable publish command,
* installer input folder,
* upgrade behavior notes,
* app data location,
* registry startup behavior,
* code-signing placeholder,
* uninstallation data considerations.

---

# Code-signing readiness

Do not sign unless a certificate is available.

Add documentation:

* which outputs should be signed,
* why signing matters,
* how signing interacts with SmartScreen,
* where signing would occur in build pipeline,
* that private keys/certificates must never be committed.

Add placeholders only.

Do not create fake certificates.

---

# Default bundled sound-pack strategy

Define how default packs ship.

Current fallback pack exists. Phase 3 should clarify production handling.

Options:

1. Embedded asset materialized into app data on first run.
2. Installed read-only pack under application directory.
3. App-data managed fallback pack regenerated if missing.

Pick and document the current strategy.

Requirements:

* fallback pack always available,
* missing fallback can be repaired,
* user cannot remove protected fallback,
* app does not require write access to installation directory,
* upgrades can refresh default pack without destroying user packs.

---

# Documentation

Add/update:

```text id="wej5fd"
docs/release-checklist.md
docs/packaging.md
docs/privacy.md
docs/soundpack-format.md
docs/troubleshooting.md
```

## Privacy doc

Must clearly state:

* keyboard events are processed locally,
* typed text is not stored,
* no key sequences are stored,
* keyboard-specific profile assignment stores local device identity only,
* sound packs stay local,
* diagnostics are local and sanitized,
* no telemetry or network transmission.

## Sound-pack format doc

Include:

* folder layout,
* manifest schema,
* valid pack ID rules,
* path rules,
* WAV requirements,
* examples,
* import steps,
* troubleshooting validation errors.

## Troubleshooting doc

Include:

* no sound,
* wrong output device,
* Bluetooth latency,
* keyboard not detected,
* keyboard assignment not stable,
* sound pack invalid,
* profile cannot activate,
* app starts minimized,
* how to reset app data,
* where diagnostics export is.

---

# UI additions

Add Settings or Preferences section if not already present.

Minimum controls:

* Start with Windows
* Minimize to tray
* Close to tray
* Show window on startup
* Enable local crash reports
* Diagnostics export
* Open documentation folder or links if local docs are available
* Reset warnings/status history if supported

If a full Settings page is too large, add these to Dashboard or a new Settings tab.

Keep UI coherent.

---

# About page/dialog

Add simple About information:

* app name,
* version,
* build configuration if available,
* app data location,
* diagnostics export button,
* privacy summary,
* link/open docs if local.

Do not include machine-specific sensitive data by default.

---

# Tests

Expand automated tests.

## Tray/minimize behavior

Test with abstractions where UI automation is impractical:

1. minimize-to-tray enabled hides instead of exits,
2. close-to-tray enabled hides instead of exits,
3. close-to-tray disabled exits,
4. tray Exit performs real shutdown,
5. tray enabled toggle updates shared sounds setting,
6. Open restores/focuses window.

## Single instance

Test:

1. first instance acquires ownership,
2. second instance fails ownership,
3. second instance sends activation signal,
4. existing instance receives activation,
5. abandoned mutex recovery,
6. IPC rejects unknown command,
7. shutdown releases ownership.

Use abstractions/fakes where direct OS mutex/pipe testing is difficult.

## Start with Windows

Test with registry abstraction:

1. enable writes correct HKCU Run value,
2. disable removes value,
3. quoted executable path,
4. moved executable updates value,
5. registry failure rolls setting back,
6. no admin path used.

## First run

Test:

1. appears when not completed,
2. does not appear after completion,
3. start-with-Windows is opt-in,
4. settings persist,
5. onboarding can be reopened if implemented.

## Diagnostics export

Test:

1. report created,
2. expected files exist,
3. settings are redacted,
4. profile summaries exclude override scan codes,
5. keyboard summaries exclude transient handles,
6. app log included only sanitized content,
7. export cancellation harmless,
8. export failure controlled,
9. retention cleanup works.

## Crash/error reports

Test:

1. unhandled exception report format,
2. disabled reporting writes nothing,
3. retention cleanup,
4. report sanitization,
5. no scan-code marker,
6. no transient handle marker.

## Settings migration

Test:

1. previous schema migrates to Phase 3 schema,
2. Phase 2 settings preserved,
3. startup setting defaults false,
4. tray defaults true,
5. migration idempotent,
6. backup created.

## Packaging checks

Test or script-check:

1. Release publish includes required assets,
2. test assemblies excluded,
3. docs included if intended,
4. app icon present,
5. version metadata present.

## Privacy static scan

Add or update static tests for:

1. no scan-code logging patterns,
2. no virtual-key logging patterns,
3. no transient-handle persistence,
4. diagnostics redaction,
5. no raw HID path persistence unless explicitly allowed.

---

# Manual verification

Verify manually:

## Tray

1. tray icon appears,
2. Open restores window,
3. close-to-tray hides instead of exits,
4. tray Exit exits process,
5. sounds continue while hidden,
6. enable/disable from tray works.

## Startup

7. enable Start with Windows,
8. verify registry entry,
9. restart or simulate startup,
10. app starts correctly,
11. disable Start with Windows removes entry.

## Single instance

12. launch app twice,
13. second launch focuses first instance,
14. no second Raw Input/audio process remains.

## Diagnostics

15. export diagnostics,
16. inspect files,
17. verify no keyboard data,
18. verify no absolute source import folder,
19. verify summaries are useful.

## Crash/error reports

20. trigger controlled test error if diagnostic test hook exists,
21. report is created,
22. report is sanitized,
23. retention cleanup works.

## Packaging

24. publish portable build,
25. run from publish folder,
26. app data remains in LocalAppData,
27. fallback pack works,
28. custom sound pack import works,
29. no test artifacts included.

## Regression

30. keyboard sounds still work,
31. profile switching still works,
32. sound-pack preview still works,
33. audio-device switching still works,
34. keyboard assignment still works,
35. five immediate-close cycles pass,
36. shutdown while hidden in tray exits cleanly.

---

# Performance and reliability

Measure and report:

* cold startup to tray/window ready,
* warm startup,
* working set visible window,
* working set tray-hidden,
* diagnostics export time,
* portable publish size,
* reports/log storage size after cleanup,
* shutdown duration from tray Exit,
* second-instance activation time.

Preserve:

* no disk access on prepared keypress path,
* no JSON parsing on keypress path,
* no UI dispatch per key event,
* bounded logs,
* bounded reports,
* deterministic shutdown.

---

# Acceptance criteria

Phase 3 is complete only when:

* tray icon and menu work,
* close/minimize-to-tray behavior works,
* tray Exit performs true deterministic shutdown,
* Start with Windows is user-controlled and per-user,
* single-instance behavior prevents duplicate Raw Input/audio instances,
* second launch activates existing instance,
* diagnostics export is available and sanitized,
* local crash/error reports are sanitized and bounded,
* settings migration preserves all Phase 2 settings,
* default sound-pack strategy is documented and reliable,
* portable publish path is documented and verified,
* packaging documentation exists,
* privacy documentation exists,
* sound-pack format documentation exists,
* troubleshooting documentation exists,
* Release build succeeds,
* all tests pass,
* no keyboard data is logged, exported, or transmitted,
* no major Phase 2 regression occurs.

---

# Deliverables

When complete, provide:

1. Phase 3 architecture summary.
2. Tray behavior summary.
3. Single-instance design.
4. Start-with-Windows design.
5. Diagnostics export schema.
6. Crash/error report behavior.
7. Settings migration summary.
8. Packaging/publish summary.
9. Default sound-pack strategy.
10. Documentation files added/updated.
11. Files added.
12. Files modified.
13. Packages added or changed.
14. Test count and result.
15. Release build result.
16. Manual verification completed.
17. Performance measurements.
18. Privacy audit result.
19. Known limitations.
20. Recommended Phase 4 scope.

Start by implementing settings migration and single-instance protection, then tray lifecycle, then startup registration, then diagnostics/error reports, then docs and packaging.
