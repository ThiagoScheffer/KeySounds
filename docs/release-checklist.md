# KeySounds Phase 3 release-candidate checklist

## Build and automated tests

- Build: `dotnet build KeySounds.csproj -c Release`
- Tests: `dotnet test KeySounds.Tests\KeySounds.Tests.csproj -c Release`
- Diagnostics export: Settings → Export diagnostics, or tray menu → Export Diagnostics.
- Portable publish: `dotnet publish KeySounds.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish\win-x64-portable`
- Helper script: `.\scripts\publish-portable.ps1`
- Expected result: zero build warnings/errors, all tests passing, and a sanitized diagnostic JSON report with aggregate counts only.

## Phase 3 verification status

- Automated Release build and test verification is required for every pass.
- Portable publish must be run before tagging an RC; record output size and executable path in the release notes.
- GUI/tray behavior requires an interactive Windows desktop and is not considered verified by headless CI alone.
- Registry startup behavior must be checked on the target Windows user account and the `KeySounds` Run value must be removed again if this is only a test.

## Phase 3 distribution checks

- Tray icon appears while the app is running.
- Tray Open restores/focuses the window.
- Tray Sounds Enabled/Disabled toggles the same Dashboard setting.
- Close-to-tray hides without shutting down input/audio.
- Tray Exit performs true shutdown.
- Start with Windows writes/removes the HKCU Run value only after user action.
- Launching a second instance activates the first and exits.
- Diagnostics export from Settings is sanitized.
- Local error reports are created only when enabled and retained according to settings.
- Published portable folder runs without test assemblies.

## Manual tray verification steps

1. Start `KeySounds.exe`.
2. Confirm the tray icon appears and tooltip says `KeySounds`.
3. Open the tray menu.
4. Use Open KeySounds and confirm the main window shows/focuses.
5. Toggle Sounds Enabled/Disabled and confirm Dashboard reflects the same state.
6. Confirm Active Profile displays the current active profile name.
7. Use Open Sound Packs and confirm the Sound Packs tab is selected.
8. Use Export Diagnostics and confirm a JSON file appears under `%LocalAppData%\KeySounds\diagnostics`.
9. Use Exit and confirm the process exits.

## Manual close/minimize verification steps

1. Enable Close to tray, close the window, and confirm the process remains running.
2. Restore from tray and confirm keyboard sounds still work.
3. Disable Close to tray, close the window, and confirm the process exits.
4. Enable Minimize to tray, minimize the window, and confirm it hides.
5. Disable Minimize to tray and confirm normal minimize behavior.

## Manual startup verification steps

1. Enable Start with Windows.
2. Confirm `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\KeySounds` is written.
3. Confirm the value quotes the current executable path and includes `--startup`.
4. Disable Start with Windows and confirm the value is removed.
5. Launch `KeySounds.exe --startup` and confirm startup visibility follows Settings → Show window on startup.

## Clean app-data test

- Close KeySounds.
- Move or delete the local KeySounds app-data directory for the development user.
- Start KeySounds.
- Confirm fallback sound pack materializes.
- Confirm fallback profile materializes.
- Confirm Dashboard opens without warnings other than expected first-run status.
- Restart and confirm the same state loads.

## Upgrade and migration test

- Start from a Phase 1/early Phase 2 settings file if available.
- Confirm settings migrate to the current schema.
- Confirm audio output selection, active profile, keyboard assignments, and global volume survive.
- Confirm corrupt settings recover from backup or safe defaults.
- Confirm malformed profiles are skipped with a recoverable status.

## Keyboard capture and routing test

- Open Profiles.
- Use Capture key for a single explicit override.
- Press exactly one key.
- Confirm only that single key identity is stored as an override.
- Confirm following typed text is ignored.
- Test key-repeat behavior:
  - Suppress: held key should not repeatedly trigger.
  - Play every repeat: held key may repeat.
  - Throttled: repeats respect the configured interval.
- Test key-up sound:
  - Enable key-up playback.
  - Select a key-up sound set.
  - Confirm key-down and key-up are distinguishable.
- Confirm normal global profile routing still works.

## Profile editor test

- Create, duplicate, edit, save, save-and-activate, revert, import, export, and delete profiles.
- Confirm invalid pack/sound-set references are visible.
- Confirm save is blocked on fatal validation issues.
- Confirm active and fallback profiles cannot be deleted unsafely.

## Sound-pack import and validation test

- Import a valid generated local test pack.
- Cancel folder picker and confirm no catalog change.
- Select a non-pack folder and confirm validation issues are shown.
- Import an invalid pack and confirm grouped validation messages.
- Import duplicate user pack:
  - Cancel keeps existing pack.
  - Replace clearly warns that the managed installed copy is replaced.
- Attempt to replace built-in fallback pack; it must remain impossible.
- Confirm source folder is not modified.
- Confirm imported manifests do not contain absolute source paths.

## Sound-pack preview and dependency test

- Preview a sound set.
- Preview individual sample rows where metadata is present.
- Create a profile that uses the imported pack.
- Save and activate it.
- Confirm removal is blocked while the pack is active.
- Switch to a different profile.
- Confirm saved-profile references still block removal by default.
- Remove or edit references and confirm removal succeeds.
- Re-import the pack and confirm missing-profile validation can be repaired.

## Audio output switch test

- Test sound on system default output.
- Select a specific output and apply.
- Restart and confirm configured output persists.
- Confirm effective output and configured output are displayed distinctly.
- Confirm sample cache is not re-decoded by audio-device switching.
- Disconnect/reconnect where possible:
  - fallback activates on disconnect,
  - retry restores configured device after reconnect.
- Test latency changes on available hardware.

## Keyboard assignment test

- Confirm connected keyboards appear on the Keyboards page.
- Confirm no raw HID path is displayed in the main UI.
- Assign a profile to a persistent keyboard identity.
- Confirm that keyboard uses the assigned profile.
- Clear assignment and confirm global profile fallback.
- Restart and confirm stable identity/assignment persists.
- Reconnect the keyboard and confirm behavior.
- If two keyboards are available, confirm independent routing.
- If identical keyboards are available, document ambiguous identity behavior.

## Shutdown stress test

- Close during sound-pack import picker.
- Close during sound-pack replacement prompt.
- Close during sound-pack removal.
- Close during catalog refresh.
- Close during sample preview.
- Close during profile save/activation/import/export.
- Close during key capture mode.
- Close during audio device refresh/switch/test sound.
- Close during keyboard refresh/identity resolution/assignment preparation.
- Run five immediate-close cycles.
- Confirm no orphan process remains.
- Confirm no corrupted settings, profiles, or partially installed packs remain.

## Persistence file audit

- Inspect `settings.json`.
- Inspect settings backup, if present.
- Inspect profile JSON files.
- Inspect sound-pack folders and manifests.
- Confirm fallback pack materialization.
- Confirm imported pack layout contains only manifest and referenced samples.
- Confirm stale temp folders/files are cleaned.
- Confirm no transient Raw Input handles.
- Confirm no scan-code or virtual-key sequences.
- Confirm no typed text.
- Confirm no raw HID paths.
- Confirm no absolute source import paths.

## Privacy and logging audit

- Search source and logs for keyboard-data logging patterns.
- Typed text must not appear in logs or persisted files.
- Key sequences, scan-code histories, virtual-key histories, transient Raw Input handles, and raw HID paths must not be persisted.
- Keyboard assignments may store only stable keyboard identity IDs mapped to profile IDs.
- Profile key overrides may store only explicit single-key identities.
- Pack/profile IDs and audio endpoint IDs may appear in bounded diagnostic text.
- Diagnostic export must contain aggregate counts only.

## Runtime measurement checklist

- Cold startup to window-ready.
- Warm startup to window-ready.
- Dashboard working set.
- Profiles page working set.
- Audio page working set.
- Keyboards page working set.
- Sound Packs page working set.
- Fallback profile activation time.
- Sound-pack catalog refresh time.
- Small pack import time.
- First preview preparation time.
- Warm preview time.
- Audio endpoint refresh time.
- Audio switch time where hardware allows.
- Shutdown duration.

Record approximations in `docs/release-measurements.md`. Label hardware-dependent or not-yet-run measurements clearly.

## Storage cleanup and reset

- Cleanup may remove managed temporary folders/files such as `.import-*`, `.replace-*`, `.trash-*`, `.cache-*`, `.atomic-*`, and `*.tmp` under the managed app root.
- Cleanup must not remove valid installed packs, user profiles, settings backups, source import folders, or unknown files outside the managed root.
- To reset local development state, close KeySounds and delete the KeySounds app-data directory for the development user, then restart.

## Rollback plan

- Keep the previous Release build artifact available until RC verification is complete.
- Before manual RC testing, back up the local KeySounds app-data directory.
- If RC startup fails, close the app, restore the previous app-data backup, and run the previous build.
- If a sound-pack import/replacement fails, verify `.import-*`, `.replace-*`, and `.trash-*` directories are cleaned or recoverable before another attempt.

## Release-blocking issues

- Any Release build warning/error.
- Any failing automated test.
- Any typed text, key sequence, transient handle, raw HID path, or raw source import path persisted unexpectedly.
- Any reproducible startup crash on clean app data.
- Any reproducible settings/profile/sound-pack corruption.
- Any shutdown deadlock or orphan process.
- Any active profile or keyboard assignment that can be broken by unsafe pack removal.
- Any audio switch that leaves the engine unrecoverable without app restart.

## Known limitations / non-goals

- No marketplace, online downloads, cloud sync, account system, automatic updater, installer, or code signing in Phase 2.
- No ZIP import unless added later as a separate workflow.
- No procedural synthesis, MIDI, firmware integration, tray behavior, per-application foreground rules, telemetry, or cross-platform input backends.
- Hardware-dependent keyboard identity and audio-device behavior must be manually verified on the target Windows machine before release.
