## ADDED Requirements

### Requirement: Complete user manual
The release SHALL include a user manual for installers and service technicians, covering installation, management of applications and accounts, verification after a service visit, messages and troubleshooting, update, uninstallation and command line reference.

#### Scenario: Manual in the package
- **WHEN** AppStart is installed
- **THEN** the manual is available as a PDF in the Start menu under AppStart – Manual

#### Scenario: Completeness
- **WHEN** the pipeline builds
- **THEN** a test checks that every message, every check and every command of the software is described in the manual

#### Scenario: Deviation in the verification report
- **WHEN** the verification report shows a deviation
- **THEN** the service technician finds the cause and the fix for exactly this check in the manual
