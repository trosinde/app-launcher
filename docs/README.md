# Documentation

| Document | Contents | Audience |
|---|---|---|
| [How it works](funktionsweise.md) | Start sequence, accounts, `apps.json`, setup, messages, Event Log, verification | everyone |
| [Security model](sicherheitsmodell.md) | Security goals, attacks and countermeasures, residual risks | security and review |
| [Development](entwicklung.md) | Prerequisites, build, tests, release, project structure | developers and contributors |
| User manual (`manual/`) | Installation and service step by step, with screenshots of the finished software | installation and service – **work in progress** |

Other sources:

- [UI designs](ui-designs/) – binding template for the user interfaces. Rendered views are in [`images/`](images/).
- [Original concept](konzept/konzept.md) – state at project start. Where it differs from the specs, the specs apply.
- [OpenSpec change](../openspec/changes/add-appstart-service/) – requirements, decisions, tasks.

## Updating the images

The images in `images/` are rendered from the HTML designs in `ui-designs/`. Once the user interfaces are finished, they are replaced by screenshots of the real software from the test VM. File names stay the same so the links remain valid.
