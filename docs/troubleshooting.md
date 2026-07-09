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

## Reset app data

Close KeySounds fully from the tray Exit command, then move or delete `%LocalAppData%\KeySounds`.

## Diagnostics export

Open Settings → Export diagnostics. The export is written under `%LocalAppData%\KeySounds\diagnostics`.
