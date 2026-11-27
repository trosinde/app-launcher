## ADDED Requirements

### Requirement: Launch of approved applications
The service SHALL launch only applications from apps.json, after successful verification of signature or SHA-256, as account appstart-app in the session of the requesting user.

#### Scenario: Launch by operator
- **WHEN** a member of AppStart Users opens the shortcut of an approved application
- **THEN** the application runs as appstart-app on their screen, without a password or UAC prompt

#### Scenario: Modified EXE
- **WHEN** the signature or checksum of the EXE does not match
- **THEN** the launch is denied and reported in the Event Log

#### Scenario: Unknown AppId
- **WHEN** an AppId is requested that is not in apps.json
- **THEN** the launch is denied and logged

### Requirement: No child processes
The service SHALL launch applications with allowChildProcesses false in a job object that prevents further processes.

#### Scenario: Escape via file dialog
- **WHEN** the application tries to start cmd.exe
- **THEN** starting the child process fails

### Requirement: Protected and valid configuration
The service SHALL reread apps.json on every request and deny every launch if the file or its folder is writable by accounts other than SYSTEM, Administrators and TrustedInstaller, or if the file is invalid. Valid means: version 1, every AppId consists only of lowercase letters, digits and hyphens with 1 to 32 characters and occurs only once, every path is absolute, check is signature with a thumbprint (40 hex characters) or sha256 with a checksum (64 hex characters).

#### Scenario: Configuration writable by operators
- **WHEN** apps.json or C:\ProgramData\AppStart grants write access to Users or to an operator
- **THEN** the service denies every launch and logs the cause

#### Scenario: Invalid configuration
- **WHEN** apps.json does not match the format, for example because of an AppId with uppercase letters or a duplicate AppId
- **THEN** the service denies every launch and logs the first error with line or field

### Requirement: Verification and launch of the same file
The service SHALL open the EXE before verification so that it can be neither modified, replaced nor deleted until the process is started, and SHALL verify signature or checksum against exactly this opened content. Signature verification requires a valid Authenticode chain and the thumbprint from apps.json; no online revocation check is performed, because lab computers are often offline.

#### Scenario: Swap between verification and launch
- **WHEN** an attempt is made during verification to overwrite or rename the EXE
- **THEN** this attempt fails and only the verified content is launched

#### Scenario: Foreign signature
- **WHEN** the EXE is validly signed, but by a certificate other than the one stored in apps.json
- **THEN** the launch is denied and logged

### Requirement: Launcher without privileges and with clear messages
The launcher SHALL accept only the argument --app <AppId>, check the format of the AppId before sending, send START <AppId> over the pipe and exit without a window on success. On every error it shows a message according to the UI design with the name of the shortcut as title, a heading, a text with the action to take and, if available, the event ID with date and time.

#### Scenario: Service not reachable
- **WHEN** the AppStart service is not running
- **THEN** the launcher shows "Launch service not reachable" with the advice to contact the administrator or service

#### Scenario: Launch denied
- **WHEN** the service replies with DENIED integrity
- **THEN** the launcher shows "Launch denied", the text about the modified application and the event ID

#### Scenario: No permission
- **WHEN** the user is not a member of AppStart Users
- **THEN** the launcher shows "No permission" and sends nothing

#### Scenario: Invalid shortcut
- **WHEN** the launcher is started without --app, with additional arguments or with an invalid AppId
- **THEN** it shows "Invalid shortcut" and does not connect to the service
