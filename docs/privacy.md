# KeySounds privacy

KeySounds is local-first.

- Keyboard events are processed locally and transiently.
- Typed text is not stored.
- Key sequences are not stored.
- Scan-code and virtual-key histories are not stored.
- Transient Raw Input handles are not persisted.
- Keyboard-specific profile assignment stores only a local stable device identity when Windows metadata allows it.
- Sound packs stay local.
- Diagnostics exports are local JSON files and sanitized.
- Local error reports are stored only on this machine when enabled, under `%LocalAppData%\KeySounds\reports`.
- There is no telemetry, cloud sync, marketplace upload, remote crash upload, or network transmission in Phase 3.

Diagnostics may include app/runtime version metadata, settings schema and preference flags, aggregate profile counts, aggregate sound-pack counts, aggregate keyboard-assignment counts, managed temporary-file counts, and process working set. They must not include typed text, key sequences, raw HID paths, transient Raw Input handles, sample contents, raw profile JSON, raw settings JSON, per-key override scan-code details, or stable keyboard identity strings.

Local error reports include timestamp, category, exception type, a sanitized/truncated exception message, and optional operation name. They do not add stack traces, typed text, key sequences, or keyboard identity fields. The current message sanitizer replaces the local username and truncates the message to 512 characters; it does not detect every possible path or secret embedded in an arbitrary exception message. Review reports before sharing them publicly.
