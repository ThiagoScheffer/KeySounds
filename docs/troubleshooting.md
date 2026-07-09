# KeySounds troubleshooting

## No sound when pressing keys

- Confirm Dashboard says sounds are enabled.
- Confirm Audio page shows a running output.
- Press a key and watch Dashboard → Keyboard path.
  - `input` not increasing: Raw Input is not reaching the app.
  - `input` increasing but `no route` increasing: active profile has no audible mapping.
  - `audio unavailable` increasing: audio engine is not running.
- Use Audio → Test sound. If preview/test works but keys do not, check profile playback settings.

## Wrong output device

Open Audio, refresh devices, select the desired output, and apply.

## Bluetooth latency

Increase shared-mode latency in Audio settings. Bluetooth devices may add unavoidable latency.

## Keyboard not detected

Open Keyboards and press Refresh. Some virtual/remote keyboards may not expose stable Raw Input metadata.

## Keyboard assignment not stable

If the page says session-only or ambiguous identity, Windows did not provide enough unique metadata for reliable persistent assignment.

## Sound pack invalid

Open Sound Packs, select the pack, and read validation issues. Re-import after fixing the manifest or WAV files.

## Profile cannot activate

Open Profiles and check the validation summary. Missing packs or sound sets must be repaired before activation.

## App starts minimized

Open Settings and enable “Show window on startup,” or use the tray menu → Open KeySounds.

## Tray icon is not visible

- Check the Windows hidden-icons overflow area.
- Confirm the app process is still running.
- Launch KeySounds again; the second launch should activate the existing instance.
- If the icon still does not appear, exit the process from Task Manager and start KeySounds again.

## App keeps running after close

This is expected when Settings → Close to tray is enabled. Use the tray menu → Exit for a full shutdown, or disable Close to tray in Settings if the window close button should exit the app.

## Minimize hides the window

This is expected when Settings → Minimize to tray is enabled. Disable Minimize to tray in Settings to keep normal minimize behavior.

## Start with Windows does not work

- Toggle Settings → Start with Windows off and on again.
- Confirm the per-user registry value exists at `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Confirm the command points to the current `KeySounds.exe` and includes `--startup`.
- No admin rights should be required.

## Second launch only focuses the existing app

KeySounds is intentionally single-instance. A second launch sends an activation request to the first process and then exits, so there should not be a second Raw Input listener or audio engine.

## Reset app data

Close KeySounds fully from the tray Exit command, then move or delete `%LocalAppData%\KeySounds`.

## Diagnostics export

Open Settings → Export diagnostics. The export is written under `%LocalAppData%\KeySounds\diagnostics`.
