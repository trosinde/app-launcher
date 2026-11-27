## ADDED Requirements

### Requirement: Protected named pipe
The pipe SHALL be reachable only for AppStart Users, accept no remote access, be created as the first instance at service start and accept only requests in the format START <AppId> with at most 64 bytes.

#### Scenario: Access over the network
- **WHEN** another computer opens the pipe
- **THEN** the connection is rejected

#### Scenario: User outside the group
- **WHEN** a user without membership in AppStart Users opens the pipe
- **THEN** access is denied

#### Scenario: Spoofed pipe
- **WHEN** a pipe with the name already exists at service start
- **THEN** the service does not start and reports an error

#### Scenario: Invalid request
- **WHEN** a request has a different format or is longer than 64 bytes
- **THEN** it is discarded and logged

#### Scenario: Operator creates own pipe instance
- **WHEN** a member of AppStart Users tries to create another server instance of \\.\pipe\AppStart
- **THEN** this is denied, because the group does not have the FILE_CREATE_PIPE_INSTANCE right

#### Scenario: Foreign session
- **WHEN** a request arrives
- **THEN** the application starts only in the session of the requester

### Requirement: Reply to the launcher
The service SHALL answer every valid request with exactly one line: OK, or DENIED <reason> <event ID> with reason from unknown-app, integrity, config, busy, error. The reply contains no paths, checksums or other internal details.

#### Scenario: Launch denied
- **WHEN** the service denies a launch
- **THEN** the launcher receives DENIED with reason and event ID and shows the message "Launch denied" with this event ID

#### Scenario: No internal details
- **WHEN** a launch is denied because of a modified EXE
- **THEN** the reply contains neither the path nor the expected or found checksum
