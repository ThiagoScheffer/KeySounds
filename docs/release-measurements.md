# Phase 2 release-candidate measurements

Measurements from this automated pass are approximate and were collected on the development machine through build/test execution and static inspection. Hardware/UI timings still need an interactive manual run.

## Automated measurements from this pass

- Release build command: `dotnet build KeySounds.csproj -c Release`
- Release build elapsed time: about 0.82 seconds after restore was warm in the final run.
- Release test command: `dotnet test KeySounds.Tests\KeySounds.Tests.csproj -c Release`
- Release test count: 78 tests.
- Release test duration: about 1 second test execution time in the RC run.
- Diagnostic export mode: `KeySounds.exe --diagnostics diagnostics.json`
- Diagnostic export content: aggregate counts and process/runtime metadata only.
- Diagnostic export duration: about 3 ms.
- Diagnostic export process working set: about 24.3 MB for command-line diagnostic mode.
- Diagnostic export counts from the development app-data root during this pass: 1 profile file, 1 sound-pack directory, 0 invalid sound-pack directories, 0 managed temp directories, 0 managed temp files.

## Measurements still requiring an interactive app run

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
- Hardware audio switch time.
- Shutdown duration.

## Hardware-dependent notes

- Keyboard identity persistence, reconnect behavior, two-keyboard routing, and ambiguous identical-device behavior require physical keyboard testing.
- Audio disconnect/reconnect fallback, specific endpoint switching, and latency stability require physical audio-device testing.
