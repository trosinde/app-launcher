## MODIFIED Requirements

### Requirement: No child processes
The service SHALL launch every application according to its field childProcesses: blocked (default) in a job object that prevents further processes; allowlist in a monitored job object according to requirement "Allowlist for child processes"; all without a job object. apps.json version 1 SHALL remain valid: allowChildProcesses false corresponds to blocked, true corresponds to all.

#### Scenario: Escape via file dialog
- **WHEN** an application with childProcesses blocked tries to start cmd.exe
- **THEN** starting the child process fails

#### Scenario: Configuration version 1
- **WHEN** apps.json version 1 contains an application with allowChildProcesses false
- **THEN** the service launches it as with childProcesses blocked

## ADDED Requirements

### Requirement: Allowlist for child processes
With childProcesses allowlist the service SHALL launch the application in a job object without process limit, without breakaway and with KILL_ON_JOB_CLOSE, check every new process in the job against allowedChildren (path and signature or SHA-256) and immediately terminate every process without a valid entry. Every error during the check SHALL lead to termination. Every decision SHALL be recorded in the Event Log with operator, AppId, path and reason.

#### Scenario: Allowed helper program
- **WHEN** the application starts Export.exe and Export.exe is in allowedChildren with a passing check
- **THEN** Export.exe keeps running and an information event is written

#### Scenario: Process not allowed
- **WHEN** the application starts cmd.exe and cmd.exe is not in allowedChildren
- **THEN** the service terminates cmd.exe immediately and writes a warning event

#### Scenario: Modified file
- **WHEN** a modified Export.exe is started whose SHA-256 does not match the entry
- **THEN** it is terminated and the event names the reason "Checksum does not match"

#### Scenario: Grandchild process
- **WHEN** the allowed Export.exe starts a process that is not in allowedChildren
- **THEN** this process is terminated

#### Scenario: Application exits before the helper program
- **WHEN** the application exits while an allowed Updater.exe is still running
- **THEN** Updater.exe keeps running and stays monitored until it exits

#### Scenario: Service stops
- **WHEN** the service is stopped or crashes while an application with allowlist is running
- **THEN** Windows terminates all processes of its job object

### Requirement: Learning mode
The service SHALL support a time-limited learning mode for an AppId that only administrators can switch on via the protected file learn.json. In learning mode the service SHALL NOT terminate processes in the job, but record path, signature, SHA-256, count, timestamps and parent process in C:\ProgramData\AppStart\Learn\<AppId>.json. After expiry the allowlist SHALL apply again, also for instances that are already running.

#### Scenario: Recording
- **WHEN** learning mode is active for appa and application A starts Export.exe
- **THEN** Export.exe runs and appears in the recording with path, check values and count

#### Scenario: Expiry
- **WHEN** the expiry time has passed and the still running application A starts cmd.exe
- **THEN** the service terminates cmd.exe according to the allowlist

#### Scenario: Tampered control file
- **WHEN** learn.json or its folder is writable by operators
- **THEN** the service ignores learning mode and writes an error event
