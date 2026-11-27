## ADDED Requirements

### Requirement: Verification of helper programs
verify SHALL check, for each application with childProcesses allowlist and for every entry in allowedChildren: file present, signature or SHA-256 valid, folder not writable by operators. Applications with childProcesses all SHALL appear in the check with the note "All other programs allowed", without deviation, because the mode was chosen deliberately. An active learning mode SHALL appear as a deviation.

#### Scenario: Helper program modified
- **WHEN** the SHA-256 of Updater.exe no longer matches the entry
- **THEN** verify reports a deviation with path and the hint rehash --app appa --child <path>

#### Scenario: All other programs allowed
- **WHEN** an application has childProcesses all
- **THEN** the check is passed and shows the note "All other programs allowed" with the AppId

#### Scenario: Learning mode forgotten
- **WHEN** learning mode is still active for an application during verification
- **THEN** verify reports a deviation with expiry time and the hint learn stop --app <AppId>
