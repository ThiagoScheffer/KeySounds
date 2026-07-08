You are working inside an existing Avalonia + C# Windows keyboard-sound application.

Phase 1 is already implemented and currently passes:

* Release build with 0 warnings/errors
* 4 xUnit tests
* Basic startup smoke test

Do not redesign the application or begin Phase 2. Your task is to perform a focused **Phase 1 hardening pass** based on the audit findings below.

## Objective

Fix the confirmed lifecycle, shutdown, concurrency, Raw Input safety, and native cleanup issues while preserving the existing architecture and observable application behavior.

The implementation must continue to use:

* Avalonia UI
* C#
* Dedicated hidden Win32 Raw Input window
* Background native message-loop thread
* Bounded `Channel<T>` pipeline
* Per-device auto-repeat suppression
* NAudio `WasapiOut`
* Persistent 48 kHz stereo float mixer
* Preloaded WAV sample
* 32-voice limit
* MVVM UI
* Existing project and namespace structure

Do not add new product features.

## First action: inspect the current implementation

Before editing:

1. Open and inspect:

   * `Platform/Windows/Input/WindowsRawInputSource.cs`
   * related native methods and native structures
   * `Audio/WasapiAudioEngine.cs`
   * `Application/KeyboardSoundCoordinator.cs`
   * `App.axaml.cs`
   * existing tests
2. Confirm the current startup and shutdown sequence.
3. Identify every shared field accessed from multiple threads.
4. Identify ownership of:

   * native input thread
   * hidden window handle
   * native window class
   * Raw Input registration
   * channel producer and consumer
   * WASAPI output
   * mixer
   * cached sample
5. Preserve existing public contracts unless a change is necessary for correctness.

Implement the fixes directly after inspection.

---

# Required fixes

## 1. Eliminate the Raw Input startup/shutdown race

### Existing failure mode

Shutdown may occur after the native thread has started but before the hidden window is assigned.

Current behavior appears to be:

1. input thread starts,
2. `StopAsync()` runs,
3. `_window` is still null,
4. no close message is posted,
5. join times out,
6. native thread later creates the window,
7. thread enters `GetMessage()` permanently.

This must be fixed.

### Required lifecycle model

Implement an explicit lifecycle state machine or equivalent synchronized model.

Suggested states:

```csharp
private enum InputLifecycleState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}
```

Use a synchronization mechanism such as:

* a private lock plus shared completion tasks, or
* `SemaphoreSlim` for serialized start/stop transitions

The exact mechanism is flexible, but it must guarantee:

1. `StartAsync()` cannot race another `StartAsync()`.
2. `StopAsync()` cannot miss an in-progress startup.
3. If stop is requested during startup, the native thread must observe it before entering a permanent message loop.
4. Once a native window exists, shutdown must post a close or quit message.
5. `StopAsync()` must not return until the native thread has actually terminated or a clearly reported fatal timeout occurs.
6. Startup failure must complete the startup task with the original exception.
7. Shutdown must remain possible after partial startup failure.
8. No background native thread may remain alive after successful shutdown.

### Recommended coordination primitives

Consider using:

```csharp
TaskCompletionSource<nint> windowReady
TaskCompletionSource<object?> threadExited
CancellationTokenSource lifetimeCancellation
```

Use:

```csharp
TaskCreationOptions.RunContinuationsAsynchronously
```

The native thread should:

1. register its window class,
2. create its hidden window,
3. publish the `HWND`,
4. check whether stop was already requested,
5. register Raw Input,
6. enter the message loop only when startup remains valid,
7. perform deterministic cleanup in `finally`,
8. signal thread exit exactly once.

`StopAsync()` should:

1. atomically transition to stopping,
2. signal cancellation,
3. wait for either:

   * the window handle to become available, or
   * startup/thread failure,
4. post `WM_CLOSE` or `WM_QUIT` when appropriate,
5. await thread termination,
6. expose timeout or cleanup failure explicitly.

Do not rely only on `Thread.Join(2000)` and then continue silently.

A bounded timeout is acceptable, but timeout must produce a controlled error and must not be presented as successful cleanup.

---

## 2. Replace unreliable `async void` shutdown handling

### Existing problem

Avalonia application shutdown currently uses an `async void` event handler.

The framework may continue process teardown before:

* Raw Input has stopped,
* the hidden native window has been destroyed,
* the native thread has exited,
* WASAPI output has stopped,
* coordinator disposal has completed.

### Required fix

Refactor application shutdown so that cleanup is awaited through a controlled application lifecycle path.

Preferred approaches include:

### Option A — Explicit asynchronous exit command

Use an explicit application shutdown service or method:

```csharp
public async Task ExitAsync()
{
    await coordinator.StopAsync();
    desktopLifetime.Shutdown();
}
```

Ensure UI close requests are intercepted while cleanup is running.

### Option B — Window close interception

Cancel the first close request, await cleanup, then perform final shutdown only after cleanup completes.

Possible conceptual flow:

```text
User closes window
    ↓
Cancel close
    ↓
Await coordinator shutdown
    ↓
Mark shutdown complete
    ↓
Call desktopLifetime.Shutdown()
```

### Requirements

* No `async void` cleanup path except unavoidable UI event adapters.
* If an event adapter must remain `async void`, it must call one centralized shutdown task and contain all exceptions.
* Multiple close/exit requests must await the same shutdown task.
* The application must not call `.Wait()` or `.Result` on the Avalonia UI thread.
* Shutdown exceptions must be caught, surfaced, and must not cause an unobserved task exception.
* The final process exit must occur only after cleanup completes or after a clearly handled fatal shutdown failure.

---

## 3. Synchronize audio playback and shutdown

### Existing problem

`PlayClick()` uses `_gate`, but `ShutdownAsync()` does not.

A race can occur where one thread:

* checks initialization,
* obtains the mixer,
* begins adding a voice,

while another thread:

* nulls fields,
* stops output,
* disposes WASAPI resources.

`IsInitialized` is also accessed across threads without synchronization.

### Required fix

Define one coherent synchronization strategy for:

* initialization,
* playback submission,
* shutdown,
* state reads,
* output/mixer disposal.

A straightforward implementation may use a private lock:

```csharp
private readonly object _gate = new();
```

All access to these fields must use the same synchronization policy:

* `_output`
* `_mixer`
* `_cachedSound`
* `_isInitialized`
* `_isShuttingDown`
* active voice collection, if shared

### Playback requirement

`PlayClick()` must:

1. acquire synchronization,
2. reject playback when uninitialized or shutting down,
3. obtain stable references,
4. add the voice safely,
5. release synchronization quickly.

### Shutdown requirement

`ShutdownAsync()` must:

1. serialize against initialization and other shutdown calls,
2. set shutdown state before disposing resources,
3. prevent new playback submissions,
4. detach callbacks if present,
5. stop and dispose output exactly once,
6. clear state deterministically,
7. allow concurrent callers to await the same shutdown operation.

Do not hold a monitor lock across arbitrary awaited operations.

A pattern using a shared shutdown `Task` plus short critical sections is preferred.

### `IsInitialized`

Make it thread-safe through one of:

* lock-protected getter,
* `Volatile.Read`,
* immutable state snapshot,
* atomic lifecycle enum

Do not use a plain unsynchronized Boolean shared between threads.

---

## 4. Make coordinator shutdown genuinely idempotent

### Existing problem

A second caller currently returns immediately while the first shutdown remains in progress.

That is not sufficient idempotency because the second caller cannot rely on cleanup being complete when its call returns.

### Required behavior

All concurrent `StopAsync()` callers must await the same underlying shutdown task.

Use a pattern conceptually similar to:

```csharp
private readonly object _lifecycleGate = new();
private Task? _stopTask;

public Task StopAsync()
{
    lock (_lifecycleGate)
    {
        return _stopTask ??= StopCoreAsync();
    }
}
```

Adapt this safely to the existing coordinator.

Requirements:

1. Shutdown work starts once.
2. Every caller receives the same task or an equivalent task representing the same cleanup.
3. The task completes only when:

   * event consumption has stopped,
   * input source has stopped,
   * audio engine has stopped,
   * resources have been disposed.
4. Exceptions are consistently observable by every caller.
5. Calls after successful shutdown complete immediately.
6. Calls during startup are handled deterministically.
7. Start must not be allowed after terminal disposal unless explicitly supported by the existing contract.

Apply the same principle to input and audio lifecycle methods where needed.

---

## 5. Remove per-`WM_INPUT` unmanaged allocation

### Existing problem

Every keyboard message currently performs:

```csharp
Marshal.AllocHGlobal(...)
Marshal.FreeHGlobal(...)
```

This is not a leak, but creates unnecessary allocator pressure inside the native callback path.

### Required fix

Use a reusable buffer owned exclusively by the native message-loop thread.

Acceptable implementations:

### Option A — Reusable managed byte array

* query required size,
* resize only when needed,
* pin temporarily for `GetRawInputData`,
* reuse the array on subsequent messages.

### Option B — Reusable unmanaged buffer

* allocate one buffer on the native thread,
* grow it only when required,
* free it once during thread cleanup.

### Option C — stack allocation for expected small sizes with safe fallback

Only use this if the implementation remains clear and validates all lengths.

### Requirements

* Buffer ownership must remain on the native input thread.
* No buffer may be accessed concurrently.
* Grow only when required.
* Impose a reasonable maximum accepted raw-input size to prevent pathological allocation.
* Free reusable unmanaged memory deterministically if unmanaged storage is used.
* Do not store pointers beyond the current callback.
* Do not introduce unsafe code unless it materially simplifies correctness and is carefully bounded.

Prefer clarity over micro-optimization.

---

## 6. Validate Raw Input sizes before parsing

### Existing problem

The current code reads `RAWINPUTHEADER` and `RAWKEYBOARD` without confirming the returned byte count is large enough.

### Required validation

Before reading structures:

1. Validate the first `GetRawInputData` size query succeeded.
2. Validate required size is nonzero.
3. Validate required size is not above a reasonable maximum.
4. Validate the second `GetRawInputData` call did not return `uint.MaxValue`.
5. Validate bytes returned are at least the size of:

   * `RAWINPUTHEADER`, and
   * the keyboard payload required for `RAWKEYBOARD`.
6. Validate the raw input type is keyboard before reading keyboard data.
7. Ignore unsupported raw-input types safely.
8. Do not read beyond returned bytes.
9. Treat malformed input as a contained processing error rather than a process crash.

Use checked arithmetic where offset and length calculations could overflow.

Do not assume structure size based solely on the requested buffer size.

---

## 7. Contain all exceptions inside the unmanaged window procedure

No managed exception may escape across the unmanaged Win32 window-procedure boundary.

Wrap custom `WM_INPUT` processing in a defensive boundary:

```csharp
try
{
    ProcessRawInput(...);
}
catch (Exception ex)
{
    ReportInputFailure(ex);
}
```

Requirements:

* Do not throw through the native callback.
* Preserve normal handling through `DefWindowProc` where appropriate.
* Avoid repeated exception storms:

  * rate-limit repeated identical processing errors, or
  * transition the input service to a controlled faulted state.
* Fatal input failures must be reported to the coordinator/UI.
* The native thread must remain capable of clean shutdown after an input-processing failure.
* Do not log scan codes, virtual keys, typed characters, or key sequences in error messages.

---

## 8. Handle `GetMessage()` correctly

### Existing problem

`GetMessage()` returning `-1` is currently treated as normal loop termination.

### Required behavior

Handle all three outcomes:

```text
> 0  message received
= 0  WM_QUIT; normal termination
= -1 Win32 failure
```

On `-1`:

1. capture `Marshal.GetLastWin32Error()`,
2. create a meaningful `Win32Exception`,
3. report it through the input service failure path,
4. leave the message loop,
5. run deterministic cleanup,
6. complete the thread-exit task with failure or otherwise preserve the exception for `StopAsync()`/status reporting.

Do not silently interpret `-1` as successful shutdown.

---

## 9. Deterministically unregister Raw Input

Before destroying the hidden input window, remove the keyboard Raw Input registration using `RIDEV_REMOVE`.

Requirements:

* usage page remains `0x01`,
* usage remains `0x06`,
* flags use `RIDEV_REMOVE`,
* target window must comply with Win32 requirements for removal,
* failures should be captured and reported without preventing remaining cleanup,
* removal must occur on the native thread where practical,
* cleanup must still continue if removal fails.

Do not register or remove devices from multiple unrelated components.

---

## 10. Unregister the native window class

Track whether the custom window class registration succeeded.

During cleanup:

1. destroy the native window if it exists,
2. remove Raw Input registration,
3. unregister the window class using the correct module instance,
4. tolerate the case where class registration never completed,
5. report cleanup failures without abandoning the remaining cleanup steps.

Use a unique, process-safe class name.

Ensure the window procedure delegate remains strongly referenced for at least as long as the window class and window exist.

---

## 11. Preserve voice-limit behavior while reducing avoidable allocations where practical

The audit confirmed:

* voice allocation occurs on the channel consumer thread,
* `CachedVoice.Read()` is allocation-free,
* this is low severity.

Do not build an unnecessarily complex lock-free audio engine.

However, inspect whether the current closure used for voice completion can be replaced by:

* a direct callback,
* explicit voice removal,
* a small reusable voice pool,
* or another straightforward mechanism.

Only make this change if it remains easy to reason about and test.

Mandatory requirements remain:

* maximum 32 simultaneous voices,
* completed voices removed,
* no unbounded collection,
* no disk reads,
* no audio device recreation,
* no new allocation added to the WASAPI callback itself.

Correct shutdown is more important than eliminating every per-click managed allocation.

---

# Lifecycle invariants

After the changes, the following invariants must hold.

## Input service

```text
Stopped:
- no native thread
- no HWND
- no Raw Input registration
- no registered window class

Starting:
- exactly one native thread may be starting
- stop request is observable
- all callers can await startup outcome

Running:
- one native thread
- one hidden window
- one Raw Input registration
- one active message loop

Stopping:
- no new startup accepted
- stop has been signaled
- callers await actual native thread exit

Stopped or Faulted after cleanup:
- native thread has terminated
- HWND is destroyed
- Raw Input is removed when it had been registered
- window class is unregistered when it had been registered
```

## Audio service

```text
Uninitialized:
- no active WasapiOut
- no mixer available for playback

Initialized:
- one persistent output
- one persistent mixer
- cached sample loaded

Stopping:
- new playback is rejected
- existing output is being stopped/disposed

Stopped:
- no output or mixer is reachable for playback
- repeated shutdown calls await or observe the same completed shutdown
```

## Coordinator

```text
- startup occurs once
- shutdown work occurs once
- concurrent shutdown callers await the same completion
- no event-consumer task survives completed shutdown
- input stops before audio is disposed
```

---

# Testing requirements

Expand the automated test suite.

The current suite has 4 tests. Add focused tests for lifecycle coordination and concurrency.

At minimum add tests covering:

## Coordinator

1. Two concurrent `StopAsync()` calls await the same cleanup.
2. The second caller does not complete before the first cleanup finishes.
3. Input stop occurs before audio shutdown/disposal.
4. Shutdown after partial startup failure remains safe.
5. Repeated shutdown after completion is harmless.
6. Exceptions during shutdown are observable and not silently swallowed.

Use controllable fake services with `TaskCompletionSource` to test ordering.

## Input lifecycle logic

Extract or isolate enough lifecycle coordination to test without creating a real Win32 window.

Test:

1. Stop requested while startup is waiting for window creation.
2. Startup completion after stop request does not enter a permanent running state.
3. Native-thread exit completes all pending stop callers.
4. Startup failure propagates.
5. Stop after startup failure completes.
6. Timeout or native-thread nontermination is reported as failure, not success.

Do not create fragile tests that depend on real global keyboard input.

## Audio concurrency

Using a fake or factored state controller, test:

1. Playback is rejected after shutdown begins.
2. Concurrent shutdown calls await the same task.
3. Shutdown disposes output exactly once.
4. `IsInitialized` reflects lifecycle transitions safely.
5. Playback cannot access a mixer after disposal.

If direct NAudio testing is impractical, isolate the lifecycle/state logic behind a small internal abstraction and test it independently.

## Raw Input parsing

Where practical, add tests for:

1. buffer smaller than `RAWINPUTHEADER`,
2. keyboard payload smaller than `RAWKEYBOARD`,
3. non-keyboard raw input type,
4. valid keyboard input,
5. absurdly large reported buffer size,
6. invalid second-read result.

Do not require actual Win32 input delivery for parser tests.

---

# Build and static verification

After implementation:

1. Restore packages.
2. Build the complete solution in Release.
3. Run all tests.
4. Confirm zero compiler errors.
5. Resolve all warnings introduced by this work.
6. Run the application.
7. Confirm it remains open.
8. Close the application normally.
9. Confirm the process terminates.
10. Confirm no hidden native thread remains.
11. Repeat startup and shutdown several times.
12. Trigger application close immediately after startup to exercise the original race.
13. Trigger multiple close requests rapidly.
14. Verify no deadlock, hang, or native thread leak occurs.

If possible, add a development-only diagnostic exposing:

* current input lifecycle state,
* whether the native thread is alive,
* whether an HWND exists,
* whether Raw Input is registered,
* whether audio is initialized.

Do not expose keyboard metadata.

Remove or disable unnecessary diagnostics from production output.

---

# Manual verification checklist

Retain the existing manual checks and add shutdown stress checks.

Verify manually:

1. Audible click playback works.
2. Input works while another application has focus.
3. Holding a key does not create uncontrolled repeat clicks.
4. Enable/disable works while another application has focus.
5. Close immediately after application launch.
6. Close during audio initialization if reproducible.
7. Close during Raw Input startup if reproducible.
8. Click close multiple times quickly.
9. Start and close the application repeatedly.
10. Change or disconnect the audio device, then close.
11. Confirm the process always exits.
12. Confirm no orphan process remains in Task Manager.

---

# Constraints

Do not:

* replace Raw Input with a low-level keyboard hook,
* attach Raw Input to the Avalonia window procedure,
* create a Windows Service,
* redesign the UI,
* begin sound profiles,
* implement a tray application,
* implement cross-platform input,
* add Native AOT,
* introduce blocking `.Wait()` or `.Result` calls on the UI thread,
* hide shutdown failures,
* abandon a native thread after a timeout while reporting success,
* allow managed exceptions to escape a Win32 callback,
* log keyboard values or sequences.

---

# Deliverables

When complete, provide:

1. Summary of every audit issue fixed.
2. Exact lifecycle model used for:

   * Raw Input
   * coordinator
   * audio
3. Files modified.
4. Files added.
5. Tests added.
6. Release build result.
7. Full test result.
8. Result of immediate-close stress testing.
9. Result of repeated-close testing.
10. Confirmation that all concurrent shutdown callers await the same cleanup.
11. Confirmation that the native thread has terminated after shutdown.
12. Confirmation that Raw Input registration and native window class are removed.
13. Remaining known risks.
14. Any audit finding intentionally not fixed, with justification.

## Acceptance criteria

This hardening pass is complete only when:

* shutdown during Raw Input startup cannot leave the native thread running,
* application shutdown no longer depends on an unawaited cleanup operation,
* audio playback cannot race disposal,
* `IsInitialized` is thread-safe,
* all concurrent coordinator shutdown callers await the same cleanup,
* per-message unmanaged allocation has been removed or reduced to reusable storage,
* Raw Input buffer lengths are validated before parsing,
* no exception can escape the unmanaged window procedure,
* `GetMessage() == -1` is treated as a Win32 error,
* Raw Input registration is removed deterministically,
* the native window class is unregistered,
* all existing behavior remains intact,
* the Release build succeeds,
* all tests pass,
* immediate startup-to-shutdown stress does not leave an orphan process or native thread.

Start by inspecting the current implementation and tests. Apply the smallest coherent set of changes that guarantees these lifecycle invariants.
