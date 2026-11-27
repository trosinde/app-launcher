# Security model

AppStart gives admin rights to programs that a standard user starts. This makes AppStart itself a worthwhile target. This page shows what AppStart protects, how, and where the limits are.

## Security goals

1. An operator does **not** get admin rights for arbitrary programs. Only approved applications run with admin rights. What their own dialogs allow is described under [Residual risks](#residual-risks-and-limits).
2. An operator can start **only** approved, **unmodified** applications.
3. An operator **cannot read or change** the applications' data and registry values.
4. Every start attempt is **traceable**.
5. A **deviation** from the expected state is noticed instead of silently leading to unsafe operation.

Principle: when in doubt, AppStart refuses the start and logs the reason.

## Attacks and countermeasures

Measures marked "planned" are implemented by the setup, which is still in progress.

| Attack | Countermeasure | Status |
|---|---|---|
| Operator starts an arbitrary EXE through the launcher | The launcher sends only an AppId. Paths, arguments and environment come exclusively from `apps.json`. | implemented |
| Operator changes `apps.json` and adds their own programs | File writable only for SYSTEM and Administrators. If it is not, the service refuses every start. | check in the service implemented, setting the rights planned |
| Operator replaces an application's EXE | Verification of the signature (with a pinned certificate) or SHA-256 before every start. | implemented |
| Operator replaces the EXE between verification and start | The service keeps the file locked from opening until the process starts and verifies through the same handle. | implemented |
| Operator opens a command prompt from within the application, e.g. via "Open file" | Mode "Blocked" (default): job object with a limit of one process, child processes fail. Mode "Allowed helper programs only": the service monitors the job object and immediately ends every process that is not on the list with path and signature or SHA-256 (event 1206). Shells and script hosts get on the list only with explicit confirmation; learning mode never suggests them. | implemented |
| Operator replaces an allowed helper program | Verification as for the main EXE on every start; helper programs only from folders that operators cannot write to (`allow-child` refuses otherwise, `verify` reports it). | implemented |
| Operator starts programs through a forgotten learning mode | Learning mode runs for at most 120 minutes and afterwards no longer applies, even in running instances; `verify` reports an active learning mode as a deviation. `learn.json` is protected like `apps.json`; if operators can write to it, the service ignores it (event 1207). | implemented |
| Service is stopped so that disallowed programs keep running | Job object with `KILL_ON_JOB_CLOSE`: if the service ends, Windows ends all processes of the monitored application. | implemented |
| Operator replaces the service or launcher EXE | Installation only to `C:\Program Files\AppStart`, where only SYSTEM and Administrators have write access. Per-user installation is ruled out. | implemented |
| A program creates a fake pipe `AppStart` to intercept requests | The service creates the pipe as the first instance. If it already exists, the service does not start (event 1300). The group has no right to create further pipe instances. | implemented |
| Access to the pipe over the network | `PIPE_REJECT_REMOTE_CLIENTS` and an explicit deny for network logons. | implemented |
| User without approval uses AppStart | Pipe opened only for the group `AppStart Users`. | implemented |
| Start of an application in another user's session | The service starts only in the session the request comes from. | implemented |
| Flooding the service with requests | Fixed length (64 bytes), timeout 2 s, one request per session, lockout time after every request. | implemented |
| Logon as `appstart-app` | Interactive and Remote Desktop logon denied. Password random, unknown, stored nowhere. The service logs the account on without a password (S4U). | S4U implemented, logon block planned |
| Operator reads the application's passwords in the registry | Registry key per application readable only for `appstart-app`, SYSTEM and Administrators. | planned |
| Misconfiguration after a service visit (e.g. operator is admin again) | `verify` checks all points and reports every deviation with an exit code other than 0. | planned |

<p align="center">
  <img src="images/testapp.png" width="480" alt="Test application: child process cmd.exe blocked by the job object">
  <br>
  <sub>The test application visibly checks the most important protections, here: child process blocked. (UI design)</sub>
</p>

## What the application sees

The application runs as `appstart-app`, not as the operator and not as SYSTEM:

- It has full admin rights on the local computer.
- Its `HKEY_CURRENT_USER` is in the profile of `appstart-app` (`C:\Users\appstart-app`), not in the operator's profile.
- Its window appears on the operator's screen and can be used normally.
- It has **no network credentials**. It cannot reach network drives or databases that use Windows authentication.

## Residual risks and limits

| Item | Assessment |
|---|---|
| The application's file dialogs | "Open file" and "Save as" run in the application's process, i.e. with admin rights. An operator can use them to rename, delete or replace files – also outside the application, in the extreme case AppStart's configuration. Check for each application which dialogs it offers and assess the risk. |
| Only the EXE is verified | DLLs and other files in the application's program folder are not. This folder must be read-only for operators. |
| Administrators can still read the applications' passwords | Remains. AppStart only protects against operators without admin rights. |
| The application itself has admin rights | A vulnerability in the application that executes arbitrary code runs with admin rights. The job object prevents new processes, but not code in the application's process. |
| Mode "All allowed" | Weakens the protection considerably: the application can start any program with admin rights. Choose it only if "Allowed helper programs only" is not enough, and assess it per application. `verify` lists these applications as a notice. |
| Mode "Allowed helper programs only": gap of milliseconds | Without a kernel driver, Windows offers no way to prevent a process before it starts. The service ends a disallowed process after it has started. Measured (spike 1.1): 0.7 to 4.7 ms, once 16 ms under load; the starting process received "Access denied" each time. That is not enough for interactive work, but a prepared program could execute something in this time. Countermeasures: data folder locked, helper programs only from protected folders, shells never automatic. If the residual risk is not acceptable: mode "Blocked", or AppLocker/WDAC (separate project). |
| Allowed helper programs run with admin rights | An allowed PDF viewer or editor can open and save files just like the application itself. Learning mode suggests such programs but does not preselect them ("can open files – weigh up"). |
| Handover to processes outside the job object | A program can hand a task to an instance that is already running (e.g. Edge) or to a COM server that Windows starts outside the job object. AppStart does not monitor these processes in any mode. Only processes of `appstart-app` in the same session are affected. |
| Helper programs with SHA-256 | After an update of the application the checksum no longer matches; the service ends the helper program. `verify` reports it with the command `rehash --app … --child …`. |
| No online revocation check of certificates | Deliberate, because many workstations are offline. Pinning to the thumbprint replaces it. A compromised vendor certificate remains valid until the approval is adjusted. |
| Windows 10 | Start in the operator session so far tested only on Windows 11. |

## Reporting vulnerabilities

Please report confidentially through [GitHub Security Advisories](https://github.com/trosinde/app-launcher/security/advisories/new), not as a public issue. See [SECURITY.md](../SECURITY.md).

## Further reading

- Technical decisions with rationale: [design.md](../openspec/changes/add-appstart-service/design.md)
- Evidence from the spikes: [spike-results.md](../openspec/changes/add-appstart-service/spike-results.md)
- Requirements as scenarios: [specs](../openspec/changes/add-appstart-service/specs/)
