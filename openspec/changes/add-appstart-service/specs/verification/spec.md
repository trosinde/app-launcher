## ADDED Requirements

### Requirement: Verify command
verify SHALL run all checks of the expected setup, create a verification report and return an exit code other than 0 on a deviation.

#### Scenario: Correct setup
- **WHEN** verify runs on a correctly set up machine
- **THEN** the exit code is 0 and the report shows all checks as passed

#### Scenario: Deviation after service
- **WHEN** an operator is admin again or the registry key is readable again
- **THEN** verify reports the deviation with an exit code other than 0

### Requirement: Starting the verification
The verification SHALL be startable from a Start menu entry, a button in the setup and the command line, require admin rights and store every report with date and time.

#### Scenario: Service technician after the service visit
- **WHEN** the service technician chooses "AppStart – Verify setup" in the Start menu and confirms the UAC prompt
- **THEN** the verification view shows the result per check and the report is in C:\ProgramData\AppStart\Reports

#### Scenario: Operator starts the verification
- **WHEN** an operator without admin rights wants to start the verification
- **THEN** it runs only after admin credentials are entered
