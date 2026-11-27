## ADDED Requirements

### Requirement: Other programs in the wizard
The wizard SHALL offer, per application in step 1, the choice "Other programs" (Blocked (recommended), Allowed helper programs only, All allowed (unsafe)) and the dialog "Allowed helper programs" with Add, Remove, Update checksum and learning mode, according to docs/ui-designs/Main, Helpers, Learn and LearnResult.

#### Scenario: Allow helper program via dialog
- **WHEN** the installer chooses "Allowed helper programs only" for application A, adds Export.exe and finishes the wizard
- **THEN** application A is in apps.json with childProcesses allowlist and Export.exe in allowedChildren

#### Scenario: Shell in the learning result
- **WHEN** the learning mode recording contains cmd.exe
- **THEN** the wizard shows cmd.exe marked and not selectable

#### Scenario: All allowed
- **WHEN** the installer chooses "All allowed (unsafe)"
- **THEN** the wizard requires a confirmation before it continues

### Requirement: Helper programs via command line
AppStart.Setup.exe SHALL manage mode, allowlist and learning mode without user interface: add --child-processes, set-children, allow-child, deny-child, rehash --child, learn start|stop|show|apply and list, with exit codes 0, 1 and 2. Shells and script hosts SHALL be addable only with --allow-shell.

#### Scenario: Allow helper program via command line
- **WHEN** allow-child --app appa --exe "C:\Program Files\Vendor\AppA\Export.exe" is run
- **THEN** Export.exe is in allowedChildren with signature or SHA-256, verify runs automatically and the exit code is 0

#### Scenario: Shell without confirmation
- **WHEN** allow-child is run with C:\Windows\System32\cmd.exe without --allow-shell
- **THEN** apps.json remains unchanged and the exit code is 2

#### Scenario: Writable folder
- **WHEN** the helper program is in a folder that operators can write to
- **THEN** apps.json remains unchanged and the exit code is 1

### Requirement: Silent setup via answer file
AppStart.Setup.exe apply --config <file> SHALL idempotently set up applications, protection, mode, helper programs and operators from a JSON answer file. The file SHALL be fully validated before anything is changed, and MUST NOT contain passwords. Applications that are not in the file SHALL remain, except with --replace.

#### Scenario: First setup
- **WHEN** apply --config is run with a valid file for two applications
- **THEN** both are set up as in the wizard, verify runs and the exit code is 0

#### Scenario: Second run
- **WHEN** the same file is applied again
- **THEN** nothing changes and the exit code is 0

#### Scenario: Invalid file
- **WHEN** the file contains an unknown field or a path does not exist
- **THEN** nothing is changed, the output names the field or path and the exit code is 2

### Requirement: Bootstrapper with answer file
AppStart-Setup.exe /quiet /config <path> SHALL install the MSI silently and then run the installed, signature-verified AppStart.Setup.exe with apply --config <path> without user interface. /config without /quiet SHALL be invalid (exit code 87). If apply ends with 1, the bootstrapper SHALL end with 10; if apply ends with 2, with 11.

#### Scenario: Software distribution in one step
- **WHEN** AppStart-Setup.exe /quiet /config C:\Deploy\appstart.json is run as SYSTEM
- **THEN** AppStart is installed and set up, the log is under C:\ProgramData\AppStart\Logs and the exit code is 0 (or 3010 if a restart is required)

#### Scenario: Setup fails
- **WHEN** the installation succeeds, but apply ends with 2 because the answer file is invalid
- **THEN** the bootstrapper ends with exit code 11

### Requirement: Migration of the configuration
The setup SHALL convert apps.json version 1 to version 2 on the first write: allowChildProcesses false becomes childProcesses blocked, true becomes all.

#### Scenario: Update of an existing installation
- **WHEN** after an update the setup saves a change to apps.json version 1
- **THEN** apps.json is version 2 and every application has the same protection as before
