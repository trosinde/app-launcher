## ADDED Requirements

### Requirement: Test application like an affected fat client
AppStart.TestApp SHALL require admin rights, store values including a test password in HKLM\SOFTWARE\AppStartTest, read and write files in C:\ProgramData\AppStartTest and display its state.

#### Scenario: Start without admin rights
- **WHEN** a standard user starts the TestApp EXE directly
- **THEN** it exits with a message

#### Scenario: Start via the launcher
- **WHEN** a member of AppStart Users starts the TestApp via its shortcut
- **THEN** it shows account appstart-app, admin yes and access to registry and files ok

#### Scenario: Data blocked for operators
- **WHEN** the operator opens the registry key or data folder of the TestApp directly
- **THEN** access is denied

#### Scenario: Escape attempt
- **WHEN** "Start command prompt" is chosen in the TestApp
- **THEN** starting cmd.exe fails
