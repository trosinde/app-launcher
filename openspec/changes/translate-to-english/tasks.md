## 1. Rules and specs
- [x] 1.1 openspec/project.md, CLAUDE.md, docs/entwicklung.md: language rule English
- [x] 1.2 Translate spec deltas of add-appstart-service, add-allowed-child-processes, fix-wizard-findings (quoted UI texts in English); openspec validate --all --strict

## 2. Software texts
- [x] 2.1 Setup: SetupTexts, VerificationTexts, SetupEngineTexts, CliTexts, texts in XAML and code (wizard, verify view, report HTML, helpers, learn windows)
- [x] 2.2 Launcher Texts, Bootstrapper texts, service event texts, TestApp texts
- [x] 2.3 Names: Start menu entries, AppStart-Manual.pdf, release asset AppStart-Manual-<Version>.pdf (Build-Release.ps1, Build-Manual.ps1, ManualBuilder, release.yml)
- [x] 2.4 Tests and e2e scripts updated to the English texts

## 3. Documentation
- [x] 3.1 Manual docs/manual (index + 9 chapters, images/README.md) in English; completeness test green
- [x] 3.2 README in English
- [x] 3.3 docs/ (README, konzept, funktionsweise, sicherheitsmodell, entwicklung), CONTRIBUTING, SECURITY, PR template in English

## 4. Finish
- [ ] 4.1 CI green (Windows), openspec validate green
- [ ] 4.2 Screenshots from the English build on the test VM (later, with add-appstart-service 10.2)
