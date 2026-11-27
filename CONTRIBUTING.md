# Contributing

Thanks for your interest! This project has a single maintainer (@trosinde).
Contributions are welcome as pull requests. Only the maintainer can push to `main` and merge.

## How to

1. Fork the repository and create a branch from `main`.
2. Make your change and add or update tests for it.
3. Run the tests locally (Windows, .NET SDK as in `global.json`):
   `dotnet test --solution AppStart.slnx -c Release`
4. Open a pull request against `main`.

## What happens next

- CI runs automatically for your pull request. For brand-new GitHub accounts the
  maintainer has to approve the first run.
- The pull request is **reviewed only once all required checks are green**: "Build and test"
  (Windows: build, tests, manual) and "OpenSpec validate". If a check
  fails, fix it with another push to the same branch.
- If `main` has moved on, your branch has to be updated before the merge ("Update branch"
  or rebase). Keep "Allow edits by maintainers" enabled so the maintainer can do this.
- The maintainer decides whether and when to merge. Pull requests are squash-merged.

## Reporting security vulnerabilities

Please do not open public issues for vulnerabilities. See [SECURITY.md](SECURITY.md).
