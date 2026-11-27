# AppStart

## Purpose
Starts approved Windows applications for standard users with admin rights, without a password and without UAC. Standalone component; changes neither the applications nor their installers.

## Tech Stack
- C# / .NET 10 LTS, self-contained, x64, Windows 10 and 11
- Service: .NET Worker Service (Microsoft.Extensions.Hosting.WindowsServices)
- Win32: CsWin32
- Launcher: Native AOT, WinExe without a window
- Bootstrapper AppStart-Setup.exe: Native AOT, embeds the MSI, then starts the installed setup
- UI: WPF (MVVM, Fluent theme) for setup and TestApp
- Command line: System.CommandLine
- Package: Velopack MSI, per-machine
- Tests: xUnit, integration tests on a Windows test VM with snapshot
- CI: GitHub Actions, windows-latest (CI on push/PR, release on tag v*.*.*)

## Environment
- Development, build and tests on Windows 11 x64 only
- Setup, service installation and integration tests only on the separate test VM, never on the development machine

## Conventions
- Security before convenience: every deviation from the expected configuration leads to rejection and an entry in the Event Log
- No paths or free parameters from the launcher, only an AppId from apps.json
- No plain-text passwords, no secrets in the repository
- All binaries Authenticode-signed
- Run code with admin rights only from folders that operators cannot write to (program folder); never from Downloads or %TEMP%
- Code, comments, UI and documentation in English
- Every requirement has at least one automated test
