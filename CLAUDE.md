# CLAUDE.md
- Work spec-driven according to openspec/project.md and the active change.
- Development runs on Windows. Install setup and service only on the test VM. This machine is the test VM (Proxmox, Windows 11 Pro): install via the E2E/UAT scripts (tests/e2e/Invoke-Uat.ps1, scheduled task "AppStartUAT"), which remove everything again.
- Implement tasks.md in order, check off completed tasks.
- No code without a requirement in the specs. If something is missing, add it to the spec first and validate.
- Document spike results (section 1) in the change before section 3 starts.
- The security rules in project.md are not negotiable.
- Language: code, comments, UI and documentation in English.
- When done: all tests green, then openspec archive add-appstart-service.
