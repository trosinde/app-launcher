## ADDED Requirements

### Requirement: Warning about administrators without password
The wizard SHALL warn in the step "Accounts and group" and name the affected accounts if an enabled account in the Administrators group has no password. Setup remains possible.

#### Scenario: Warning in the wizard
- **WHEN** the technician opens the step "Accounts and group" and an enabled admin account has no password
- **THEN** the wizard shows a warning with the account name and the advice to set a password

### Requirement: Explanation of data folder and registry key
The wizard SHALL explain in the step "Select application", at the fields data folder and registry key, that the target is locked down to appstart-app, SYSTEM and Administrators, that operators have no access afterwards, that the original permissions are restored on uninstallation and that an empty field locks nothing.

#### Scenario: Explanation visible
- **WHEN** the technician selects an application
- **THEN** the explanation and an example (C:\ProgramData\<Vendor>\<App>, HKLM\SOFTWARE\<Vendor>\<App>) are shown next to data folder and registry key

### Requirement: Warning about data folder in the program folder
The wizard SHALL warn if the data folder is the folder of the EXE, lies inside it or contains it. The warning SHALL state that appstart-app could then modify the EXE and DLLs, that swapped DLLs are not detected by the hash and signature verification and that a folder under C:\ProgramData is recommended. Continuing is possible only after explicit confirmation. apply and the answer file output the same warning.

#### Scenario: Data folder equals program folder
- **WHEN** the technician enters the folder of the selected EXE as data folder
- **THEN** the wizard shows the warning and "Next" is possible only after confirmation

#### Scenario: Data folder outside
- **WHEN** the data folder neither contains the folder of the EXE nor lies inside it
- **THEN** no warning appears

### Requirement: Removing operators
The wizard SHALL be able to remove every member of the group AppStart Users except appstart-user, including members from earlier runs and members added by hand. When applying, the setup SHALL remove all members from the group that are no longer in the list.

#### Scenario: Remove an operator added earlier
- **WHEN** the technician opens the wizard again, removes an operator already listed and applies
- **THEN** the account is no longer a member of AppStart Users and can no longer launch any application via AppStart

#### Scenario: appstart-user stays
- **WHEN** the technician edits the list of operators
- **THEN** appstart-user is not in the removable list; it is controlled only by its own option

### Requirement: Own icon
AppStart SHALL have its own icon: as program icon of AppStart.Setup.exe, AppStart.Service.exe, AppStart.Launcher.exe and AppStart-Setup.exe, as window icon of the wizard and the verification view, in the Start menu entries "AppStart – Setup" and "AppStart – Verify setup" and for the MSI in "Apps & features". The shortcuts of the approved applications keep the icon of their EXE.

#### Scenario: Start menu shows the AppStart icon
- **WHEN** the technician opens the Start menu folder "AppStart"
- **THEN** "AppStart – Setup" and "AppStart – Verify setup" show the AppStart icon

#### Scenario: Applications keep their icon
- **WHEN** an operator sees the shortcut of an approved application
- **THEN** it shows the icon of the application, not the AppStart icon
