# KeySounds Phase 2 MVP release checklist

## Build and automated tests

- Build: `dotnet build KeySounds.csproj -c Release`
- Tests: `dotnet test KeySounds.Tests\KeySounds.Tests.csproj -c Release`
- Expected result: zero build warnings/errors and all tests passing.

## Manual startup and persistence checks

- Start with a fresh app-data root.
- Confirm the built-in fallback sound pack and fallback profile materialize.
- Confirm Dashboard shows enabled state, active profile, audio output, keyboard count, sound-pack count, invalid-pack count, loaded sample count, and decoded-memory summary.
- Restart and confirm active profile, audio selection, sound packs, and keyboard assignments survive.
- Confirm malformed/invalid profiles or packs show recoverable warnings and do not crash startup.

## Sound-pack checks

- Open the Sound Packs page.
- Confirm the built-in fallback pack is marked built-in/protected and cannot be removed or replaced.
- Import a valid local folder sound pack.
- Import cancellation must leave catalog unchanged.
- Import an invalid folder and confirm validation issues are shown.
- Import a duplicate user pack and confirm Cancel/Replace workflow.
- Confirm replacing a user pack restores/keeps a valid catalog on failure.
- Preview a valid sound set.
- Preview individual sample rows when validation metadata is available.
- Remove an unreferenced user pack.
- Confirm active-profile, saved-profile, keyboard-assigned, and built-in dependencies block removal with remediation text.
- Confirm importing a missing referenced pack repairs profile validation after refresh.

## Profiles checks

- Confirm pack selectors update after importing/removing packs.
- Confirm missing pack/sound-set issues are visible.
- Confirm preview is disabled or fails gracefully for unresolved mappings.
- Confirm Save and Save and activate surface dependency errors.
- Confirm profile import/export remains stable.

## Audio checks

- Confirm configured output and currently active output are distinct.
- Confirm Test sound uses the selected/effective output.
- Confirm output switch does not redecode samples unnecessarily.
- Confirm unavailable output/fallback state does not crash the UI.

## Keyboard checks

- Confirm connected keyboards appear without showing raw HID paths.
- Confirm assigned, effective, and global fallback profile labels are understandable.
- Confirm assigned profile preparation failures are visible.
- Confirm keyboard-assigned profiles block referenced pack removal.
- Confirm clearing an assignment updates dependency/removal state.

## Shutdown stress checks

- Close during sound-pack import.
- Close during sound-pack replacement.
- Close during sound-pack removal.
- Close during catalog refresh.
- Close during preview.
- Close during profile save/activation/import/export.
- Close during audio device switch.
- Close during keyboard refresh/identity resolution/assignment preparation.
- Run five immediate-close cycles.
- Confirm no orphan process remains and no corrupted settings/profiles/packs are left behind.

## Privacy checks

- Typed text must not appear in logs or persisted files.
- Key sequences, scan-code histories, virtual-key histories, transient Raw Input handles, and raw HID paths must not be persisted.
- Keyboard assignments may store only stable keyboard identity IDs mapped to profile IDs.
- Profile key overrides may store only explicit single-key identities.
- Pack/profile IDs and audio endpoint IDs may appear in bounded diagnostic text.
- Do not include raw JSON blobs, sample file contents, or private user data in reports.

## Storage cleanup and reset

- Cleanup may remove managed temporary folders/files such as `.import-*`, `.replace-*`, `.trash-*`, `.cache-*`, `.atomic-*`, and `*.tmp` under the managed app root.
- Cleanup must not remove valid installed packs, user profiles, settings backups, source import folders, or unknown files outside the managed root.
- To reset local development state, close KeySounds and delete the KeySounds app-data directory for the development user, then restart.

## Known limitations / non-goals

- No marketplace, online downloads, cloud sync, account system, automatic updater, installer, or code signing in Phase 2.
- No ZIP import unless added later as a separate workflow.
- No procedural synthesis, MIDI, firmware integration, tray behavior, per-application foreground rules, telemetry, or cross-platform input backends.
- Hardware-dependent keyboard identity behavior should be verified on real Windows keyboards before release.
