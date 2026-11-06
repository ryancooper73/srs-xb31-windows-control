# Manual Shutdown Launcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove shutdown-time Bluetooth work and deliver a manual Desktop launcher that shuts down Windows only after a confirmed speaker power-off.

**Architecture:** The WPF app returns to ordinary Windows lifecycle behavior: no message hook, `SessionEnding` handler, shutdown block reason, or Bluetooth transaction during system shutdown. The existing standalone PowerShell transaction becomes fail-closed and displays a native notification on helper failure. A batch launcher invokes that script with explicit shutdown consent, and an installer script writes a user-specific copy to the Desktop.

**Tech Stack:** .NET 10 WPF, .NET 8 standalone helper, Windows PowerShell 5.1, MSTest, PowerShell integration tests.

**Spec:** `docs/superpowers/specs/2026-11-06-manual-shutdown-launcher-design.md`

## Global Constraints

- Do not issue Bluetooth I/O from any Windows shutdown lifecycle callback.
- On helper failure, show a visible notification, exit non-zero, and do not run `shutdown.exe`.
- Keep normal in-app and tray Power Off behavior unchanged.
- Do not alter the Bluetooth driver, protocol frames, display sync, startup registration, or single-instance behavior.

## Review Focus

- Helper exits non-zero: test that no shutdown starts and the original failure code propagates.
- Notification API itself fails: test that the fallback still exits non-zero and does not shut down Windows.
- PowerShell path containing spaces: test a quoted batch invocation.
- Missing Desktop path: installer reports a clear error without writing elsewhere.
- Existing Desktop launcher: installer replaces only its named launcher.

---

### Task 1: Remove the automatic shutdown integration

**Files:** `src/Xb31.Control/App.xaml.cs`, `src/Xb31.Control/TrayApplicationController.cs`, `src/Xb31.Control/Display/DisplayStateMonitor.cs`, affected tests; delete `SystemShutdownPowerOff.cs`, `ShutdownTrace.cs`, and their test file.

**Interfaces:** consumes `StartupClientFactory.Create(string[] args)` and `DisplayStateMonitor()`; produces application startup and display monitoring with no shutdown-handler dependency.

- [ ] Write tests that exercise display monitoring and tray lifecycle without `SystemShutdownPowerOff`.
- [ ] Run focused tests to watch the obsolete-constructor compilation failure.
- [ ] Remove construction, forwarding, `SessionEnding` subscription, obsolete handler/trace, and dedicated tests.
- [ ] Run `dotnet test tests/Xb31.Control.Tests/Xb31.Control.Tests.csproj` and commit `Remove shutdown-time Bluetooth power off`.

### Task 2: Make the manual shutdown transaction fail closed

**Files:** `Shutdown-With-Xb31.ps1`, the existing shutdown PowerShell tests.

**Interfaces:** consumes `PowerOff-Xb31.ps1` exit code and `-DryRun`, `-ForceShutdown`, `-LogPath`; produces a preserved non-zero helper exit after notification and calls `shutdown.exe /s /f /t 0` only after helper exit `0`.

- [ ] Extend failure tests to assert exit `10`, a notification/fallback diagnostic, and no shutdown invocation; run and observe RED.
- [ ] Add `Show-ErrorNotification([string] $message)` and return early on helper failure, preserving the exit code.
- [ ] Run all four shutdown-script tests and commit `Abort manual shutdown when speaker power off fails`.

### Task 3: Create and install the Desktop batch launcher

**Files:** create `Shutdown-With-Xb31.bat`, `Install-Shutdown-With-Xb31Launcher.ps1`, `tests/Install-Shutdown-With-Xb31Launcher.Tests.ps1`; update `README.md`.

**Interfaces:** installer resolves `[Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)` and creates `Shutdown XB31 and Windows.bat` which uses a quoted PowerShell command with `-NoProfile -ExecutionPolicy Bypass -ForceShutdown`, returning `%ERRORLEVEL%`.

- [ ] Write installer tests for batch content, quoting, named-file-only overwrite, and missing Desktop path; run RED.
- [ ] Implement the reusable batch template and user-scoped installer.
- [ ] Run installer tests, install the current-user launcher, update README, and commit `Add desktop shutdown launcher`.

### Task 4: Final verification

- [ ] Run `dotnet test` and `powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-All.ps1`.
- [ ] Inspect the installed Desktop launcher and commit any verification-only correction.
