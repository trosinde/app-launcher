## ADDED Requirements

### Requirement: English as the only language
All user-visible texts of AppStart (setup wizard, verify view and report, command line, launcher, bootstrapper, service event log, TestApp), the user manual, the README and the developer documentation SHALL be in English. Code, comments and commit messages stay English.

#### Scenario: Wizard in English
- **WHEN** a service technician opens "AppStart – Setup" on a German Windows
- **THEN** every label, hint, warning and result line of the wizard is English

#### Scenario: Manual as release asset
- **WHEN** a release is published
- **THEN** it contains `AppStart-Manual-<Version>.pdf` and no `AppStart-Handbuch-<Version>.pdf`

#### Scenario: Manual matches the software
- **WHEN** a text of the software is missing from the English manual
- **THEN** the completeness test fails and names the text and where it comes from
