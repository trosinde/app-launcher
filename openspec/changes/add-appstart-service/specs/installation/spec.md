## ADDED Requirements

### Requirement: Selection of applications
The setup SHALL allow selecting one or more application EXEs, via dialog and via command line.

#### Scenario: Multiple selection
- **WHEN** the installer selects two applications
- **THEN** both are in apps.json and each has its own shortcut

### Requirement: Shortcut per application
The setup SHALL create one shortcut per application to the launcher with --app <AppId>, with the icon from the application's EXE.

#### Scenario: Icon applied
- **WHEN** the shortcut has been created
- **THEN** it shows the icon of the selected application

### Requirement: Accounts and group
The setup SHALL create the account appstart-app (admin, no interactive logon), the group AppStart Users and, on request, the standard account appstart-user.

#### Scenario: No logon as app account
- **WHEN** someone tries to log on interactively or via Remote Desktop as appstart-app
- **THEN** the logon is denied

### Requirement: Permissions on data and registry
The setup SHALL restrict the data folder and registry key of each application to appstart-app, SYSTEM and Administrators.

#### Scenario: Operator reads registry
- **WHEN** an operator opens the registry key in regedit
- **THEN** access is denied

### Requirement: Installation location
The package SHALL install exclusively per-machine into Program Files.

#### Scenario: Program folder
- **WHEN** the installation is complete
- **THEN** only SYSTEM and Administrators can write in the program folder

### Requirement: Interactive installation via bootstrapper
The package SHALL include a signed bootstrapper AppStart-Setup.exe (Native AOT, no extracted libraries) that requires admin rights, installs the embedded MSI silently and then starts the installed AppStart.Setup from the program folder with the wizard.

#### Scenario: First installation
- **WHEN** the installer starts AppStart-Setup.exe and confirms the UAC prompt
- **THEN** the MSI is installed and then step 1 of the wizard appears from C:\Program Files\AppStart, without a second UAC prompt

#### Scenario: Start from a writable folder
- **WHEN** AppStart-Setup.exe is started from a folder writable by users, such as Downloads
- **THEN** the bootstrapper with admin rights loads or starts no files from this folder or from %TEMP% and starts AppStart.Setup only from the program folder after successful signature verification

#### Scenario: Error during MSI installation
- **WHEN** msiexec ends with an error, for example 1618 because another installation is running
- **THEN** the bootstrapper shows a message with cause and action, stores the MSI log under C:\ProgramData\AppStart\Logs and does not start the wizard

#### Scenario: Cancel in the wizard
- **WHEN** the installer cancels the wizard after a successful MSI installation
- **THEN** AppStart remains installed, the wizard can be started again from the Start menu and verify reports the missing setup as a deviation

#### Scenario: Update over an existing installation
- **WHEN** AppStart-Setup.exe of a newer version is started on a computer that has been set up
- **THEN** the MSI is updated, the wizard shows the existing configuration and verify runs at the end

### Requirement: Silent installation via MSI
The MSI SHALL be installable without bootstrapper and without user interface, so that software distribution can use it. Setup is then done via the command line of AppStart.Setup.

#### Scenario: Software distribution as SYSTEM
- **WHEN** msiexec /i AppStart.msi /qn is run as SYSTEM
- **THEN** AppStart is installed in the program folder, the service is running and no user interface appears

### Requirement: Installer hooks without user interface
The install hook of the package SHALL only register and start the service, the uninstall hook only stop and delete the service. Neither shows a user interface. Configuration, accounts, group, shortcuts and permissions are removed exclusively by AppStart.Setup.exe uninstall.

#### Scenario: Update keeps the setup
- **WHEN** a new MSI is installed over an installation that has been set up
- **THEN** apps.json, accounts, group, shortcuts and permissions remain unchanged and the service runs in the new version

#### Scenario: Complete uninstallation
- **WHEN** the service technician runs AppStart.Setup.exe uninstall and then the MSI uninstallation
- **THEN** the original shortcuts are restored, permissions on data folders and registry keys are reset, accounts and group are removed, and the service and program folder are deleted
