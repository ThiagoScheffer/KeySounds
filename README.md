# KeySounds

[![Build and test](https://github.com/ThiagoScheffer/KeySounds/actions/workflows/ci.yml/badge.svg)](https://github.com/ThiagoScheffer/KeySounds/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![Platform: Windows](https://img.shields.io/badge/Platform-Windows-0078D4)

KeySounds is a Windows desktop application that adds configurable sound feedback to keyboard input. Users can assign sound packs to keyboard groups, create profiles, configure per-key overrides, select audio output devices, and optionally assign profiles to individual keyboards.

**Free and open source under the MIT license.** Keyboard processing, profiles, and sound packs stay on your machine.

This is also a portfolio example of practical software engineering: a native Windows integration boundary, a UI separated from application services, local persistence, validation and migration, resource-aware audio playback, privacy-conscious diagnostics, and automated tests.

## Download and use

Download `KeySounds-win-x64-portable.zip` from [GitHub Releases](https://github.com/ThiagoScheffer/KeySounds/releases) when a release is available. Extract the entire ZIP to a folder and run `KeySounds.exe`. The self-contained build includes the .NET runtime; you do not need an SDK to use it. Keep the supporting files beside the executable.

The current release format is a portable Windows x64 folder. There is no installer or code signing yet. Normal use does not require administrator rights. Closing the window keeps the app in the tray; use **Exit** in the tray menu to stop it.

## Features

- Listens for keyboard input through Windows Raw Input.
- Plays configurable sounds on key-down and/or key-up events.
- Supports profiles, key groups, per-key overrides, repeat modes, volume, pitch, and variation settings.
- Imports, validates, previews, edits, and removes local sound packs.
- Caches decoded samples to reduce repeated disk and decode work.
- Supports keyboard-specific profile assignments when stable Windows device metadata is available.
- Reports input, audio, cache, and trigger status in the dashboard.
- Prevents multiple primary instances and signals an existing instance when launched again.
- Exports local diagnostics and keeps keyboard processing local and transient.

## Technology

- C# with .NET 10 (`net10.0`)
- Avalonia 12 for the desktop UI
- NAudio 2.2.1 for audio playback and decoding
- XAML with compiled Avalonia bindings
- xUnit v3 for automated tests
- `System.Text.Json` for local settings and profile persistence

## Run locally

Requirements: Windows, the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), Git, and access to NuGet for the initial restore. Clone the repository, then run the development commands from its root:

```powershell
git clone https://github.com/ThiagoScheffer/KeySounds.git
cd KeySounds
dotnet restore KeySounds.Tests/KeySounds.Tests.csproj
dotnet build KeySounds.Tests/KeySounds.Tests.csproj -c Release --no-restore
dotnet test KeySounds.Tests/KeySounds.Tests.csproj -c Release --no-build --no-restore
dotnet run --project KeySounds.csproj
```

Runtime data is stored under `%LocalAppData%\KeySounds`. Normal use does not require administrator rights.

The test project builds the application through its project reference. Tests use controlled doubles for platform and audio dependencies; real keyboard capture, tray behavior, and audio-device switching also require [manual Windows verification](docs/release-checklist.md).

## Portable release

```powershell
.\scripts\publish-portable.ps1
```

The script publishes a self-contained Release build and includes the license and privacy notice. The executable is `artifacts/publish/win-x64-portable/KeySounds.exe`. See [packaging and release instructions](docs/packaging.md) for deployment, startup, upgrades, and signing notes.

GitHub Actions builds and tests pull requests and pushes to `main` on Windows. Pushing a version tag such as `v1.0.0` runs those checks, packages the portable build, and publishes a GitHub Release with a ZIP and SHA-256 checksum. Release publication happens only after the checks pass.

## Architecture at a glance

```text
Avalonia UI / ViewModels
          |
Application services and orchestration
          |
Core domain models and boundary contracts
          |
Platform adapters       Audio adapters       Persistence adapters
Windows Raw Input       NAudio/cache         JSON/atomic files
```

Important areas:

- `Platform/Windows/Input/` isolates Windows Raw Input, native calls, parsing, and keyboard metadata.
- `Application/Routing/` resolves key groups, variations, and the final sound to play.
- `Application/Profiles/` manages profile editing, compilation, assignment, and migrations.
- `Application/SoundPacks/` handles cataloguing, validation, importing, conflicts, dependencies, drafts, and cleanup.
- `Application/Audio/` coordinates output-device selection and audio behavior.
- `Audio/` contains sample decoding and caching around NAudio.
- `Infrastructure/Persistence/` handles storage paths, JSON repositories, and atomic file writes.
- `ViewModels/` exposes state and commands to the Avalonia views.
- `KeySounds.Tests/` tests parsing, routing, persistence, migrations, lifecycle behavior, audio selection, and sound-pack workflows.

The entry point is [Program.cs](Program.cs), the composition root is [App.axaml.cs](App.axaml.cs), and the primary UI is [MainWindow.axaml](MainWindow.axaml). These folders share one application assembly; their boundaries are organizational rather than enforced by separate projects. Application components use constructor injection, interfaces at external boundaries, and fakes in tests to keep platform and audio behavior testable.

Read the [current system design](.sdlc/design/SYSTEM-DESIGN.md) for component diagrams, runtime flows, persistence, lifecycle, and trade-offs. The [documentation index](docs/README.md) distinguishes current guidance from earlier phase plans.

## Software concepts demonstrated

| Concept | Evidence |
| --- | --- |
| Separation of concerns | UI, application services, core behavior, infrastructure, platform adapters, and audio are separate areas. |
| MVVM | Avalonia XAML binds to view models such as `MainWindowViewModel`. |
| Dependency inversion | Services depend on interfaces for audio engines, decoders, pickers, and conflict resolvers. |
| Domain modeling | Profiles, key groups, sound sets, manifests, IDs, validation results, and operation statuses are explicit types. |
| Adapter pattern | Windows Raw Input and NAudio are behind application-facing abstractions. |
| Deterministic routing | Keyboard events are parsed, resolved to a profile/group/override, and mapped to a sound variation. |
| Defensive persistence | IDs and paths are validated; JSON writes use an atomic writer; migrations preserve compatibility. |
| Resource management | Async disposal, cancellation, cache limits, and cleanup services address long-running desktop behavior. |
| Testing with doubles | Platform, audio, folder-picker, and conflict-resolution dependencies can be replaced in tests. |
| Privacy by design | Key events are transient; typed text and key sequences are not stored or transmitted. |

## For recruiters: engineering walkthrough

A reviewer can verify these claims directly in the repository:

1. **Full-application development:** start at [Program.cs](Program.cs), follow [MainWindow.axaml](MainWindow.axaml), then trace into `ViewModels/`, `Application/`, `Core/`, and `Infrastructure/`.
2. **Integration work:** read `Platform/Windows/Input/RawInputParser.cs` and `WindowsRawInputSource.cs` alongside `Application/Routing/`.
3. **Maintainable workflows:** the sound-pack feature separates draft editing, validation, importing, conflict resolution, dependency checks, and cleanup.
4. **Failure-mode thinking:** invalid manifests, unsafe paths, missing samples, duplicate IDs, missing devices, migrations, and partial writes have explicit handling and tests.
5. **Shipping discipline:** the repository includes a publish script, release checklist, packaging notes, startup behavior, app-data rules, diagnostics, and privacy documentation.
6. **Trade-off awareness:** the application is intentionally local-first and Windows-focused because global keyboard capture and per-device metadata require platform-specific integration.

Useful interview material includes the [current system design](.sdlc/design/SYSTEM-DESIGN.md), [packaging](docs/packaging.md), [privacy](docs/privacy.md), and the tests in `KeySounds.Tests/`.

## Development tools

AI and developer tools were used as development assistance for exploration, implementation support, refactoring, and documentation. They are not a substitute for understanding or verification. The proof is the repository itself:

- Requirements and design decisions are recorded in the documents under `docs/`.
- Behavior is expressed as executable C# and validated with automated tests.
- Integration-heavy code is isolated behind interfaces so it can be checked with controlled test doubles.
- Build, test, and publish commands are reproducible from the command line.
- Privacy, packaging, diagnostics, migration, and failure behavior are documented explicitly.

When presenting this project, describe AI assistance accurately: tools accelerated development, while the developer remained responsible for understanding the architecture, reviewing changes, resolving integration issues, and running the build and tests. A strong walkthrough is to trace one feature end-to-end, such as keyboard input to sound playback or sound-pack import to profile activation.

## Sound-pack format

```text
my-pack/
  manifest.json
  samples/
    click.wav
```

See [docs/soundpack-format.md](docs/soundpack-format.md) for the manifest schema and validation rules.

## Privacy

KeySounds is local-first. Keyboard events are processed transiently, typed text and key sequences are not stored, sound packs remain local, and there is no telemetry or cloud synchronization in the current implementation. Diagnostic and error reports are local and sanitized. See [docs/privacy.md](docs/privacy.md).

## Current limitations

- Windows-specific integration because the app uses Windows Raw Input and keyboard metadata.
- No installer yet; releases are portable folders.
- Single-file publishing and code signing require separate release validation.
- Audio and input behavior should be tested on target Windows machines and output devices.

## Contributing

Bug reports and pull requests are welcome through [GitHub Issues](https://github.com/ThiagoScheffer/KeySounds/issues) and pull requests. For bugs, include your Windows version, reproduction steps, and expected versus actual behavior. Review any diagnostic export before sharing it.

For code changes, describe the behavior changed and how you verified it. Run the Release build and existing tests above, keep platform and audio integrations behind their existing boundaries, and update the relevant documentation. Build output, logs, test results, credentials, and signing keys belong outside version control.

## License

Copyright © 2026 Thiago Scheffer. Licensed under [MIT](LICENSE). KeySounds is free to use, modify, and distribute, including commercially, under the license terms. Third-party dependencies retain their own licenses.
