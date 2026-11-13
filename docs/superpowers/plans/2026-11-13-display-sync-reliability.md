# Display-Sync Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recover automatic display-lighting synchronization from one transient Bluetooth failure and install the app from a stable per-user location.

**Architecture:** The control-operation interface returns the device result to `DisplayLightingSync`, which schedules one guarded delayed retry for a transient automatic failure. A minimal trace writes operational events locally. A PowerShell installer publishes versioned per-user releases and repairs user launch integration.

**Tech Stack:** .NET 10, WPF, MSTest, PowerShell, Windows Registry and Shell Link COM.

**Spec:** `docs/superpowers/specs/2026-11-13-display-sync-reliability-design.md`

## Global Constraints

- Retry exactly once only for automatic `Unavailable`, `ConnectionFailed`, or `Timeout` lighting results.
- Do not retry manual controls, create a general queue, or add shutdown-time Bluetooth work.
- DIM remains diagnostic-only and user-scoped settings remain unchanged.
- Log diagnostics best-effort without changing app behavior when logging fails.
- Publish and register only within the current user's profile; add no dependencies.

## Review Focus

- A second display transition before the retry delay must invalidate the stale retry; Task 1 test.
- A disabled preference or disposed controller before the retry delay must prevent the retry; Task 1 test.
- A non-transient automatic failure must not retry; Task 1 test.
- Manual lighting commands must retain the existing UI result mapping; Task 2 test.
- Installer output must be versioned and desktop/Run registrations must target the published executable; Task 4 test.

---

### Task 1: Result-aware automatic retry

**Files:**

- Modify: `src/Xb31.Control/Display/IXb31OperationHost.cs`
- Modify: `src/Xb31.Control/Display/DisplayLightingSync.cs`
- Test: `tests/Xb31.Control.Tests/DisplayLightingSyncTests.cs`

**Interfaces:**

- Produces: `bool TryStartLighting(LightingMode mode, out Task<Xb31Result> operation)`.
- Produces: a generation-checked single retry for transient automatic failures.

- [ ] **Step 1: Write failing retry tests**

Add tests completing automatic `LightOff` with `Unavailable`, then release an injected delay and observe a second `LightOff`; also cover changed display, disabled sync, and `WriteFailed`.

- [ ] **Step 2: Verify red**

Run `dotnet test tests/Xb31.Control.Tests/Xb31.Control.Tests.csproj --filter DisplayLightingSyncTests`; expect missing result/retry behavior.

- [ ] **Step 3: Implement the result-aware host and guarded retry**

Change the interface to return `Task<Xb31Result>`. Preserve pending-target coalescing and add a 3-second retry only for the specified statuses when the captured generation and desired state remain current.

- [ ] **Step 4: Verify green**

Run the focused display-sync test command; expect PASS.

- [ ] **Step 5: Commit**

Commit with `fix: retry transient automatic lighting sync`.

### Task 2: Preserve view-model result handling

**Files:**

- Modify: `src/Xb31.Control/ViewModels/MainViewModel.cs`
- Test: `tests/Xb31.Control.Tests/MainViewModelTests.cs`

**Interfaces:**

- Consumes: result-bearing `IXb31OperationHost`.
- Produces: result-bearing `TryStartLighting` while `SetLightingAsync` remains a `Task` UI command.

- [ ] **Step 1: Write a failing view-model result test**

Assert a successful started lighting operation resolves to the client result while retaining the existing UI text updates.

- [ ] **Step 2: Verify red**

Run `dotnet test tests/Xb31.Control.Tests/Xb31.Control.Tests.csproj --filter MainViewModelTests`; expect no result-bearing operation.

- [ ] **Step 3: Return the result after existing UI mapping**

Make `CompleteLightingAsync(LightingMode)` return `Task<Xb31Result>` and make the public UI operation await/discard it.

- [ ] **Step 4: Verify green**

Run the focused view-model test command; expect PASS.

- [ ] **Step 5: Commit**

Commit with `refactor: expose lighting operation result to display sync`.

### Task 3: Best-effort application diagnostics

**Files:**

- Create: `src/Xb31.Control/AppTrace.cs`
- Modify: `src/Xb31.Control/App.xaml.cs`
- Modify: `src/Xb31.Control/TrayApplicationController.cs`
- Modify: `src/Xb31.Control/Display/DisplayLightingSync.cs`
- Test: `tests/Xb31.Control.Tests/AppTraceTests.cs`

**Interfaces:**

- Produces: `AppTrace.Write(string eventName, string? detail = null)` that never escapes I/O failures.

- [ ] **Step 1: Write a failing trace test**

Use a temporary log path and assert two named events are appended in order.

- [ ] **Step 2: Verify red**

Run `dotnet test tests/Xb31.Control.Tests/Xb31.Control.Tests.csproj --filter AppTraceTests`; expect missing type.

- [ ] **Step 3: Implement and wire best-effort tracing**

Trace startup path/version plus display states, automatic starts/results, and retry decisions without letting I/O errors affect app control.

- [ ] **Step 4: Verify green**

Run `dotnet test tests/Xb31.Control.Tests/Xb31.Control.Tests.csproj`; expect PASS.

- [ ] **Step 5: Commit**

Commit with `feat: add display sync activity diagnostics`.

### Task 4: Publish and repair per-user launch integration

**Files:**

- Create: `Install-Xb31Control.ps1`
- Create: `tests/Install-Xb31Control.Tests.ps1`
- Modify: `README.md`

**Interfaces:**

- Produces: `Install-Xb31Control.ps1 [-SourcePublishPath <path>] [-InstallRoot <path>] [-DesktopPath <path>]`.

- [ ] **Step 1: Write a failing installer integration test**

Supply a fake publish directory and temporary install/desktop directories; assert the returned executable is versioned and shortcut/Run registration target it.

- [ ] **Step 2: Verify red**

Run `Invoke-Pester .\tests\Install-Xb31Control.Tests.ps1`; expect missing installer.

- [ ] **Step 3: Implement the per-user installer**

Publish if no source is supplied, copy to a fresh release folder, create the desktop shortcut, update the per-user Run registration, and remove only the verified obsolete PowerShell shortcut.

- [ ] **Step 4: Verify green**

Run `Invoke-Pester .\tests\Install-Xb31Control.Tests.ps1; dotnet test .\SRS-XB31.slnx`; expect PASS.

- [ ] **Step 5: Commit**

Commit with `feat: publish control app to stable user location`.
