## ADDED Requirements

### Requirement: Administrators with password
verify SHALL report every enabled local account that is a member of Administrators, directly or via a nested group, and has no password as a deviation and name it in the report.

#### Scenario: Admin without password
- **WHEN** an enabled local account in the Administrators group has an empty password
- **THEN** verify reports the check "Administrators with password" as failed with the account name and returns an exit code other than 0

#### Scenario: All admins with password
- **WHEN** every enabled account in the Administrators group has a password
- **THEN** the check "Administrators with password" is passed

#### Scenario: Disabled account without password
- **WHEN** a disabled account in the Administrators group has no password
- **THEN** it does not count as a deviation
