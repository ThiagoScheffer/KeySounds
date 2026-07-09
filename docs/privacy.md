# KeySounds privacy

KeySounds is local-first.

- Keyboard events are processed locally and transiently.
- Typed text is not stored.
- Key sequences are not stored.
- Scan-code and virtual-key histories are not stored.
- Transient Raw Input handles are not persisted.
- Keyboard-specific profile assignment stores only a local stable device identity when Windows metadata allows it.
- Sound packs stay local.
- Diagnostics exports are local and sanitized.
- Local error reports are stored only on this machine when enabled.
- There is no telemetry, cloud sync, marketplace upload, remote crash upload, or network transmission in Phase 3.

Diagnostics may include aggregate counts, profile IDs/names, pack IDs, validation status, audio output status, and runtime metadata. They must not include typed text, key sequences, raw HID paths, sample contents, or raw profile JSON.
