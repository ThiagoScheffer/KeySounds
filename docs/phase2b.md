You are working inside an existing Avalonia + C# Windows keyboard-sound application.

Phase 1 and its lifecycle hardening are complete.

The first foundational slice of Phase 2 is also complete.

## Current implemented state

The repository already includes:

* Serializable sound profiles
* Group mappings
* Per-key overrides
* Application settings
* Physical scan-code key grouping
* Compiled immutable runtime profile snapshots
* Key-down routing
* Key-up routing
* Repeat modes:

  * suppressed
  * every repeat
  * throttled
* Variation modes:

  * random
  * round robin
  * avoid immediate repeat
* Global/profile/group/override volume composition
* Per-trigger pitch variation
* Per-trigger volume variation
* Atomic settings JSON persistence
* Atomic profile JSON persistence
* Settings backup recovery
* Persistent global enabled state
* Persistent active profile
* Persistent global volume
* Minimal dashboard profile and volume controls
* Built-in fallback profile preserving Phase 1 behavior
* Release build with 0 warnings/errors
* 27 passing tests
* Successful startup and normal shutdown

Do not recreate or redesign these systems.

Your task is to implement the next Phase 2 slice:

# Phase 2B — Sound packs and pack-aware sample cache

Implement:

1. Sound-pack domain models.
2. Sound-pack manifest loading.
3. Strict sound-pack validation.
4. Managed local sound-pack storage.
5. Transactional folder import.
6. Pack-aware sample identities.
7. Pack-aware decoded sample cache.
8. Runtime profile sample preparation.
9. Safe profile activation only after required samples are ready.
10. Sound-set and sample preview support at the service layer.
11. Sound-pack removal safeguards.
12. Tests for all new infrastructure.

Do not implement the full profile editor UI, audio-device switching, keyboard-device assignment, or ZIP import in this slice.

## Primary objective

Replace the current built-in-only or single-sample loading model with a safe local sound-pack system.

After this slice, the application must be able to:

* discover installed packs,
* validate them,
* import a local pack folder,
* load samples referenced by a profile,
* predecode those samples,
* route playback using stable pack-aware sample IDs,
* preview a sound set,
* switch profiles without reading files during key events,
* reject malformed packs safely,
* prevent removal of packs currently required by active profiles.

The existing fallback profile and Phase 1 click asset must remain functional.

# First action: inspect the repository

Before editing:

1. Inspect the current profile model.
2. Inspect compiled runtime profile structures.
3. Inspect the existing audio trigger model.
4. Inspect the current cached sound implementation.
5. Inspect profile activation and switching logic.
6. Inspect current storage path handling.
7. Inspect atomic persistence utilities.
8. Inspect current dashboard view model.
9. Inspect all 27 tests.
10. Identify how the fallback sound currently enters the audio engine.

Preserve existing namespaces, naming conventions, lifecycle guarantees, and threading model.

Do not weaken:

* deterministic shutdown,
* immutable active profile snapshots,
* bounded channel processing,
* no disk access on the input path,
* no JSON parsing on the input path,
* thread-safe audio shutdown,
* bounded voice count,
* privacy guarantees.

# Scope boundaries

Implement only:

```text
Sound-pack models
Manifest parsing
Pack validation
Managed storage
Folder import
Pack discovery
Pack-aware sample cache
Profile sample preparation
Preview services
Pack removal protection
Tests
Minimal dashboard status integration
```

Do not implement:

```text
ZIP import
Online download
Marketplace
Audio-device selection
Per-keyboard profile assignment
Full profile editor
Visual keyboard editor
Installer
Updater
Procedural synthesis
Cloud synchronization
Cross-platform pack storage
```

# Sound-pack domain model

Create a versioned manifest model.

Recommended structure:

```csharp
public sealed class SoundPackManifest
{
    public int SchemaVersion { get; init; } = 1;

    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Author { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<SoundSetDefinition> SoundSets { get; init; }
        = Array.Empty<SoundSetDefinition>();
}
```

```csharp
public sealed class SoundSetDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<string> Files { get; init; }
        = Array.Empty<string>();
}
```

Add optional metadata only where immediately useful.

Do not place absolute filesystem paths into the serialized manifest model.

## Stable sample identity

Create or finalize a stable pack-aware identity.

Recommended:

```csharp
public readonly record struct SoundId(
    string PackId,
    string SoundSetId,
    string SampleId);
```

`SampleId` should be stable within a pack.

It may be:

* a normalized manifest-relative path, or
* a generated stable ID derived from that path.

Do not use an absolute path as the public identity.

The keypress path must resolve a `SoundId` directly to cached PCM without filesystem access.

# Manifest format

Support schema version 1.

Example:

```json
{
  "schemaVersion": 1,
  "id": "mechanical-linear",
  "name": "Mechanical Linear",
  "author": "Local",
  "description": "Linear mechanical keyboard samples",
  "soundSets": [
    {
      "id": "alpha-down",
      "name": "Alpha Key Down",
      "files": [
        "samples/alpha/alpha-01.wav",
        "samples/alpha/alpha-02.wav"
      ]
    },
    {
      "id": "space-down",
      "name": "Spacebar Down",
      "files": [
        "samples/space/space-01.wav"
      ]
    }
  ]
}
```

The pack root contains:

```text
manifest.json
samples/
```

Do not require a fixed sample-folder layout beyond manifest-relative file references.

# Validation rules

Create a dedicated validation service.

The validator must return structured results rather than only throwing.

Suggested model:

```csharp
public sealed record ValidationIssue(
    string Code,
    string Message,
    ValidationSeverity Severity);
```

```csharp
public sealed class SoundPackValidationResult
{
    public bool IsValid { get; init; }

    public IReadOnlyList<ValidationIssue> Issues { get; init; }

    public long EstimatedDecodedBytes { get; init; }

    public int SampleCount { get; init; }
}
```

## Manifest validation

Validate:

* manifest file exists,
* JSON is readable,
* schema version equals 1,
* pack ID exists,
* pack ID uses a restricted safe format,
* pack name exists,
* pack name length is bounded,
* sound-set collection is nonempty,
* sound-set IDs are nonempty,
* sound-set IDs are unique using ordinal-ignore-case comparison,
* each sound set contains at least one file,
* duplicate file references within a sound set are rejected or deduplicated explicitly,
* total sample count is bounded.

Recommended pack ID format:

```text
lowercase letters
numbers
hyphen
underscore
```

Recommended expression:

```text
^[a-z0-9][a-z0-9_-]{0,63}$
```

Do not silently normalize an invalid imported pack ID into a different ID.

## Path validation

For every referenced file:

* reject empty path,
* reject absolute path,
* reject rooted path,
* reject drive-qualified path,
* reject UNC path,
* reject `..` traversal,
* normalize separators,
* combine with pack root,
* resolve full path,
* verify resulting full path remains inside the pack root,
* reject paths pointing to directories,
* reject symlinks or reparse points where practical,
* require `.wav`,
* use case-insensitive extension comparison on Windows.

The final containment check must use canonical full paths, not string concatenation alone.

## File validation

For each WAV:

* file exists,
* file is readable,
* length is nonzero,
* file length is below a configured maximum,
* WAV headers are valid,
* audio can be decoded by the chosen NAudio reader,
* duration is above a minimum useful duration,
* duration is below a configured maximum,
* channel count is supported,
* sample rate is within a reasonable supported range,
* decoded size can be estimated safely,
* arithmetic uses checked operations where appropriate.

Recommended limits:

```text
Maximum samples per pack: 512
Maximum samples per sound set: 128
Maximum individual WAV duration: 5 seconds
Maximum individual file size: 10 MB
Maximum estimated decoded pack memory: 256 MB
Minimum useful duration: approximately 5 ms
```

Make limits configurable through an internal options object.

Do not accept an imported pack whose estimated decoded size exceeds the configured maximum.

## Security behavior

Imported content is untrusted.

Requirements:

* no script execution,
* no external file references,
* no network access,
* no environment-variable expansion,
* no symbolic path escape,
* no archive processing in this slice,
* no arbitrary plugin loading,
* no DLL loading,
* no executable files copied unless they are ignored or the entire pack is rejected according to a documented policy.

Prefer copying only:

* `manifest.json`
* referenced WAV files

Do not blindly recursively copy every source file.

# Managed storage

Use the existing application storage root.

Expected location:

```text
%LocalAppData%\KeySounds\soundpacks\
```

Add a dedicated path service if not already present.

Installed pack layout:

```text
soundpacks/
    fallback/
        manifest.json
        samples/
            key-click.wav
    mechanical-linear/
        manifest.json
        samples/
            ...
```

The built-in fallback pack must be represented through the same logical APIs as imported packs where practical.

It may be:

* materialized into managed storage on first run, or
* exposed as a read-only built-in pack backed by application assets.

Choose the simpler robust implementation, but ensure profile compilation and cache loading treat it consistently.

# Pack discovery

Implement a repository or catalog service that:

* enumerates installed pack directories,
* reads manifests,
* validates each pack,
* returns valid and invalid pack records,
* does not crash because one pack is malformed,
* distinguishes built-in and user-installed packs,
* reports validation issues,
* avoids loading PCM during simple discovery,
* supports refresh after import or removal.

Suggested result:

```csharp
public sealed class InstalledSoundPack
{
    public required SoundPackManifest Manifest { get; init; }

    public required string RootDirectory { get; init; }

    public required bool IsBuiltIn { get; init; }

    public required SoundPackValidationResult Validation { get; init; }
}
```

Do not expose mutable internal collections.

# Transactional folder import

Implement import from a user-selected folder.

UI file/folder picker integration may remain minimal, but the application service must be complete.

## Import flow

1. Receive source folder.
2. Reject nonexistent source.
3. Validate source pack fully.
4. Determine destination from validated pack ID.
5. Detect existing installed pack with the same ID.
6. Require an explicit conflict policy:

   * cancel,
   * replace.
7. Create a unique temporary destination under the managed sound-pack root.
8. Copy `manifest.json`.
9. Copy only referenced WAV files.
10. Preserve relative directory structure.
11. Validate the copied temporary pack again.
12. Flush written files where practical.
13. If replacing:

    * move existing pack to a temporary backup location,
    * move new temporary pack into the final location,
    * restore old pack if finalization fails.
14. Atomically or transactionally finalize installation.
15. Remove temporary data after success.
16. Refresh the catalog.
17. Return a structured import result.

Never modify the source folder.

## Conflict policy

Create an explicit enum:

```csharp
public enum SoundPackConflictPolicy
{
    Cancel,
    Replace
}
```

Do not silently overwrite installed packs.

Do not permit replacing the built-in fallback pack.

## Import cleanup

At startup or repository initialization:

* clean stale import temporary directories,
* clean stale replacement backup directories only when safe,
* do not delete valid installed packs,
* use recognizable internal temporary prefixes.

# Pack removal

Implement removal of user-installed packs.

Rules:

* built-in packs cannot be removed,
* a pack required by the active runtime profile cannot be removed,
* a pack referenced by any saved profile should be reported before removal,
* default behavior should block removal when referenced,
* optional forced removal is not required in this slice,
* removal should move the directory to a temporary trash location first,
* refresh catalog after removal,
* clear cached samples belonging to the pack only when safe,
* return structured failure reasons.

Suggested result:

```csharp
public sealed record SoundPackRemovalResult(
    bool Success,
    string? ErrorCode,
    string? Message,
    IReadOnlyList<string> ReferencingProfileIds);
```

Do not leave the active profile pointing to missing samples.

# Pack-aware sample cache

Replace or extend the existing single-sample cache.

Create a cache with stable `SoundId` lookup.

Suggested contract:

```csharp
public interface ISoundSampleCache
{
    bool TryGet(SoundId id, out CachedSound sample);

    Task<SamplePreparationResult> PrepareAsync(
        IReadOnlyCollection<SoundSetReference> requiredSets,
        CancellationToken cancellationToken);

    Task ReleaseUnusedAsync(
        IReadOnlyCollection<SoundSetReference> requiredSets,
        CancellationToken cancellationToken);

    SampleCacheSnapshot GetSnapshot();
}
```

Adapt to existing architecture.

## Cache requirements

The cache must:

* decode each WAV once,
* convert to the mixer format,
* store PCM in memory,
* expose stable samples by `SoundId`,
* avoid disk reads during key events,
* avoid JSON parsing during key events,
* track decoded byte usage,
* enforce a maximum decoded-memory budget,
* serialize mutations,
* allow concurrent reads,
* preserve samples currently referenced by active voices,
* support profile switching,
* support preview,
* support removal of unused packs,
* remain safe during shutdown.

Use immutable cache snapshots or another clear concurrency model.

Do not hold a monitor lock across asynchronous file I/O.

## Cache state

Expose aggregate state:

```csharp
public sealed record SampleCacheSnapshot(
    int LoadedSampleCount,
    long DecodedBytes,
    int LoadedPackCount,
    bool IsPreparing);
```

Do not expose keyboard metadata.

# Sample decoding

Use the existing NAudio path where possible.

Each sample must be converted into the persistent mixer format:

```text
48,000 Hz
stereo
32-bit floating point
```

Handle:

* mono input,
* stereo input,
* supported sample-rate conversion,
* PCM WAV,
* IEEE float WAV where supported.

Reject unsupported channel counts or formats cleanly.

Do not leave file streams open after decoding.

Use checked decoded-size calculations.

# Sound-set runtime representation

Compile pack sound sets into runtime sample collections.

Suggested:

```csharp
public sealed class RuntimeSoundSet
{
    public required string PackId { get; init; }

    public required string SoundSetId { get; init; }

    public required IReadOnlyList<SoundId> Samples { get; init; }
}
```

Variation selection should operate over these stable `SoundId` values.

Do not store absolute file paths in the compiled active profile.

# Profile compilation integration

Extend current profile validation and compilation.

A profile referencing:

* a nonexistent pack,
* an invalid pack,
* a nonexistent sound set,
* an empty sound set

must not become active.

Compilation may validate symbolic references, but actual activation must also ensure samples are prepared successfully.

## Required profile activation sequence

Implement:

```text
Editable profile
    ↓
Validate profile model
    ↓
Resolve installed packs and sound sets
    ↓
Compile candidate runtime profile
    ↓
Determine required sound sets
    ↓
Prepare all required samples
    ↓
Verify all SoundIds resolve in cache
    ↓
Atomically replace active runtime profile
    ↓
Persist active profile setting
    ↓
Release samples no longer needed
```

Requirements:

* keep the previous runtime profile active while preparation occurs,
* do not partially activate a profile,
* do not persist the new active profile ID before successful activation,
* on failure, preserve previous active profile and cache state where practical,
* return a structured activation failure,
* support cancellation,
* serialize profile activation operations,
* reject activation after shutdown begins.

Do not stop Raw Input during profile switching.

# Cache reference safety

Old active voices may still reference samples from the previous profile.

Choose one safe strategy:

## Preferred simple strategy

Let each voice hold a strong reference to its `CachedSound` PCM data.

After profile switching:

* remove unused samples from lookup dictionaries,
* allow old voice references to remain alive until playback completes,
* let garbage collection reclaim old PCM after no references remain.

This is acceptable for Phase 2 if memory remains bounded by:

* cache budget,
* voice limit,
* maximum sample duration.

Do not dispose or invalidate PCM still in use by active voices.

# Audio-engine integration

Replace direct fallback-click selection with arbitrary `SoundId` playback.

The audio engine should receive a trigger containing:

* `SoundId`,
* gain,
* pitch ratio,
* optional pan,
* optional preview marker.

The engine should resolve the `SoundId` through the cache or receive the resolved `CachedSound`, depending on the existing architecture.

Preferred separation:

```text
Router selects SoundId
Audio facade resolves CachedSound
Wasapi engine plays CachedSound
```

The keypress path must remain:

```text
compiled mapping lookup
variation selection
cache dictionary lookup
voice submission
```

No file reads, manifest reads, or asynchronous work may occur on this path.

If a sample unexpectedly cannot be resolved:

* suppress that trigger,
* increment an aggregate missing-sample diagnostic,
* do not throw repeatedly,
* do not log key identity.

# Preview service

Implement service-layer preview support.

Required operations:

```csharp
Task PreviewSoundSetAsync(
    string packId,
    string soundSetId,
    CancellationToken cancellationToken);

Task PreviewSampleAsync(
    SoundId soundId,
    CancellationToken cancellationToken);
```

Behavior:

* ensure the target sample or sound set is prepared,
* use the same cache,
* use the same persistent mixer,
* apply a safe preview gain,
* do not read from disk on every repeated preview after preparation,
* do not exceed the 32-voice bound,
* prevent rapid preview clicks from creating unbounded work,
* serialize or debounce preparation operations,
* allow preview cancellation before playback submission where practical.

For sound-set preview:

* select one sample using avoid-immediate-repeat or round robin,
* do not play all samples simultaneously.

# Minimal UI integration

Do not build the complete sound-pack management screen yet.

Add only enough UI to validate the slice.

The existing dashboard may display:

* installed valid pack count,
* invalid pack count,
* loaded sample count,
* decoded sample memory,
* active profile required pack count,
* sound-pack status,
* refresh packs command,
* import folder command,
* preview active profile sound command where practical.

If folder-picker support would create substantial UI work, expose the import service completely and add a simple development-facing path input or defer only the picker wiring.

However, the service layer and tests must be complete.

Do not place validation, copying, decoding, or import logic in code-behind.

# Status and error reporting

Add structured operational status for:

* discovering packs,
* validating packs,
* importing pack,
* replacing pack,
* preparing samples,
* activating profile,
* previewing sound,
* removing pack.

Errors should be actionable.

Examples:

```text
Manifest missing
Unsupported schema version
Invalid pack ID
Path escapes pack directory
Referenced WAV missing
Decoded memory limit exceeded
Pack already installed
Pack is required by active profile
Profile references unknown sound set
Sample preparation failed
```

Do not expose stack traces directly in the main UI.

# Persistence interaction

Existing profile JSON may reference the fallback sample in an earlier format.

Add only the migration required for this slice.

Requirements:

* preserve current users’ active fallback profile,
* migrate old single-sample references into:

  * fallback pack ID,
  * fallback sound-set ID,
* back up profile files before migration,
* do not repeatedly migrate,
* do not build a generic migration framework beyond current needs.

The fallback profile should resolve through the new pack-aware system after migration.

# Concurrency

Review all new shared state.

Serialize:

* pack imports,
* pack replacement,
* pack removal,
* catalog refresh,
* profile sample preparation,
* profile activation,
* cache mutation.

Concurrent cache reads for playback must remain efficient.

Suggested tools:

* `SemaphoreSlim` for asynchronous mutation serialization,
* immutable dictionaries for read snapshots,
* atomic reference replacement,
* cancellation tokens,
* shared operation tasks where idempotency is required.

Do not hold locks across disk I/O.

## Shutdown

During shutdown:

1. reject new imports,
2. reject new removals,
3. reject new previews,
4. reject new profile activations,
5. cancel active import/preparation operations,
6. await active operations,
7. stop routing,
8. stop input,
9. stop audio,
10. release cache references,
11. complete deterministic shutdown.

Preserve all existing shutdown tests.

# Logging and privacy

Allowed logs:

* pack ID,
* pack validation result,
* sound-set ID,
* aggregate sample counts,
* decoded byte totals,
* import success/failure,
* cache preparation success/failure,
* profile activation success/failure.

Do not log:

* scan codes,
* virtual keys,
* key timing,
* key identities,
* typed text,
* captured key sequences.

Pack and sound-set identifiers are not keyboard data and may be logged.

# Testing requirements

Expand the test suite substantially.

## Manifest parsing

Test:

1. valid schema version 1 manifest,
2. missing manifest,
3. malformed JSON,
4. unsupported schema,
5. missing pack ID,
6. invalid pack ID,
7. duplicate sound-set IDs,
8. empty sound-set files,
9. duplicate file references.

## Path security

Test:

1. valid nested relative path,
2. absolute path rejection,
3. drive-root path rejection,
4. UNC path rejection,
5. `../` traversal rejection,
6. mixed separator traversal,
7. canonical path escape,
8. missing referenced file,
9. directory referenced as WAV,
10. unsupported extension.

## WAV validation

Generate small WAV fixtures in tests.

Test:

1. valid mono PCM WAV,
2. valid stereo PCM WAV,
3. valid supported float WAV if supported,
4. corrupt header,
5. zero-length file,
6. excessive duration,
7. excessive file size,
8. unsupported channel count,
9. estimated decoded memory overflow handling.

Do not depend on external test assets when fixtures can be generated programmatically.

## Import

Test:

1. valid folder import,
2. source remains unchanged,
3. only manifest and referenced files copied,
4. duplicate pack with cancel policy,
5. duplicate pack with replace policy,
6. built-in pack cannot be replaced,
7. validation failure leaves no final destination,
8. failure during replacement restores old pack,
9. stale temporary directory cleanup,
10. imported pack validates after copy.

Use temporary directories.

## Catalog

Test:

1. valid pack discovered,
2. invalid pack reported but does not break discovery,
3. built-in pack identified,
4. refresh reflects import,
5. refresh reflects removal,
6. returned collections are immutable or isolated.

## Sample cache

Test:

1. sample decoded once,
2. repeated prepare does not decode again,
3. multiple sound sets load,
4. missing sample fails preparation,
5. decoded byte count correct,
6. memory budget enforced,
7. concurrent prepare serialized,
8. cache lookup is successful after preparation,
9. release-unused removes lookup entries,
10. active voice reference remains valid after cache removal,
11. shutdown rejects new preparation.

Use a fake decoder where appropriate and integration tests for actual WAV decoding.

## Profile activation

Test:

1. valid profile activates after sample preparation,
2. unknown pack rejected,
3. unknown sound set rejected,
4. empty sound set rejected,
5. preparation failure preserves previous active profile,
6. cancellation preserves previous profile,
7. active profile ID persists only after success,
8. concurrent activation serialized,
9. old samples released after activation,
10. fallback profile remains valid.

## Preview

Test:

1. preview sample uses cache,
2. first preview prepares sample,
3. subsequent preview does not reread disk,
4. preview sound set selects one sample,
5. preview respects voice bound,
6. preview rejected during shutdown,
7. invalid pack or set returns controlled failure.

## Removal

Test:

1. built-in pack removal rejected,
2. active-profile pack removal rejected,
3. saved-profile reference reported,
4. unreferenced user pack removed,
5. catalog refreshes after removal,
6. cache entries removed when safe,
7. removal failure does not leave inconsistent catalog state.

## Migration

Test:

1. old fallback profile migrates to pack-aware references,
2. migration creates backup,
3. migrated profile loads,
4. migration is not repeated,
5. failed migration preserves original file.

# Performance verification

Verify:

* no file open occurs during routing after cache preparation,
* no JSON parsing occurs during routing,
* cache lookup is dictionary or equivalent constant-time lookup,
* profile switching does not block the Raw Input callback,
* UI counters are updated periodically rather than per key event,
* installed-pack discovery does not decode all samples,
* default fallback cache remains small.

Report:

* fallback pack decoded bytes,
* loaded sample count,
* cold preparation duration,
* warm preparation duration,
* working-set change after fallback load.

Measurements may be approximate but must be identified as such.

# Manual verification

After implementation, manually verify:

1. Application starts with fallback pack.
2. Existing fallback click still plays.
3. Sound-pack catalog reports fallback pack.
4. Valid local pack folder imports successfully.
5. Invalid pack folder is rejected.
6. Traversal path in manifest is rejected.
7. Imported pack appears after refresh.
8. Profile referencing imported pack activates.
9. Multiple sample variations play.
10. Switching back to fallback works.
11. Preview sample works.
12. Preview sound set works.
13. Restart preserves installed pack.
14. Restart preserves active profile.
15. Removing an unused pack works.
16. Removing the active pack is blocked.
17. Closing during import exits cleanly.
18. Closing during sample preparation exits cleanly.
19. No orphan process or thread remains.
20. No disk access occurs for ordinary prepared keypress playback.

If audible testing is unavailable, clearly identify the remaining hardware-dependent checks.

# Acceptance criteria

This slice is complete only when:

* sound-pack manifest schema version 1 exists,
* installed packs are discovered safely,
* malformed packs do not crash discovery,
* folder import is transactional,
* path traversal is blocked,
* only referenced WAV files are copied,
* duplicate pack conflicts require explicit policy,
* built-in fallback pack remains available,
* pack-aware `SoundId` values are used,
* samples are decoded once and cached,
* cache memory is bounded,
* profiles activate only after required samples are prepared,
* failed preparation preserves the previous active profile,
* profile activation persists only after success,
* preview uses the same cache and mixer,
* active or referenced packs cannot be removed unsafely,
* no disk or JSON access occurs during prepared key routing,
* deterministic shutdown remains intact,
* Release build succeeds,
* all tests pass,
* no keyboard data is logged or persisted.

# Deliverables

When finished, provide:

1. Architecture summary.
2. Sound-pack schema.
3. Validation rules implemented.
4. Import transaction design.
5. Pack discovery behavior.
6. `SoundId` design.
7. Sample-cache lifecycle.
8. Profile-activation sequence.
9. Preview behavior.
10. Pack-removal rules.
11. Migration behavior.
12. Files created.
13. Files modified.
14. Packages added or changed.
15. Test count and result.
16. Release build result.
17. Manual verification completed.
18. Performance measurements.
19. Known limitations.
20. Remaining Phase 2 work.

Start by inspecting the existing profile compiler, audio cache, profile activation path, and persistence layer. Implement the pack model and validation first, then import/storage, then cache integration, then activation and preview.
