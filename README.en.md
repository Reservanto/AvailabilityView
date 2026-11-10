# AvailabilityView

[Česky](README.md)

A read-only page that shows website visitors the free capacity of a [Reservanto](https://www.reservanto.cz) account: open classes and free time of resources for individual services. Embed it in a website with an iframe or open it as a link. The page books nothing and has no database, user accounts or cookies.

- **Views:** agenda, week in columns, week with an hour grid, month, and a sentence with the next free slot.
- **Filters:** segment, location, service and resource, fixed in the link or switchable by the visitor.
- **Languages:** Czech and English.
- **Look:** custom CSS through CSS variables and classes (`docs/vzhled.md`).
- **Generator** builds the link and the iframe code, the iframe adapts its height to the content.

One instance serves one account (merchant). The [specification](docs/SPEC.md) describes exactly what the page does.

Project documentation, code comments and the setup wizard are in Czech. The visitor page is in Czech and English.

## Installing on a server

The IIS guide is in [docs/instalace-iis.md](docs/instalace-iis.md): publishing, HTTPS, connecting the account, allowed domains for the iframe and updates.

## Development

### Requirements

- Windows, .NET SDK 10 (version in `global.json`).
- Node.js, only for the check script `eng/check.ps1` (hook test and duplicate detector). `dotnet build` compiles SCSS and TypeScript, it does not need Node.
- For browser tests, PowerShell 7 (`pwsh`) to install Chromium once.
- To connect to the API, a Reservanto account and your own ClientId. Ask for a ClientId at obchod@reservanto.cz with the return address `https://localhost:5001/Auth/Callback`.

### Running

```powershell
dotnet dev-certs https --trust
dotnet run --project src/AvailabilityView.Web --launch-profile AvailabilityView.Web
```

The app runs on `https://localhost:5001`. An instance that is not connected links to the `/Setup` wizard. The wizard guides you through the consent in Reservanto and at the end shows the command that stores the token in user-secrets:

```powershell
dotnet user-secrets set "Reservanto:LongTimeToken" "<token>" --project src/AvailabilityView.Web
```

The token gives access to the account. It belongs only in user-secrets or in `appsettings.Production.json` on the server, never in the repository. Configuration keys are described in [section 5 of the specification](docs/SPEC.md#5-nastavení).

### Checks and tests

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/check.ps1 -Mode Fast
```

| Mode | What it does |
|---|---|
| `Fast` | build with warnings as errors and tests without network or browser. Tests replay recorded API responses from `tests/api-recordings`. |
| `Full` | adds browser tests (Playwright, axe-core), a trial publish and the duplicate detector |
| `Smoke` | tests against the production API, read only. Reads the token from the app's user-secrets. |

The smoke tests are written for the project's test account. They expect more than five segments, both classes and individual segments with free slots, a fully booked class, and the `Event_r` right (the occupancy option enabled in the wizard). With another account some of them fail. Tests without network and browser tests do not depend on the account.

Before the first `Full` run, install Chromium (after the tests have been built once):

```powershell
pwsh tests/AvailabilityView.Web.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Pitfall: run `dotnet test` without `--nologo`, otherwise it ends saying no test ran.

### Layout

| Folder | Contents |
|---|---|
| `src/AvailabilityView.Web` | visitor page, connection wizard, generator |
| `src/AvailabilityView.Application` | free time and period calculation, no dependency on the web or the API |
| `src/AvailabilityView.Infrastructure` | reading data from the API per segment type, cache |
| `src/Reservanto.ApiClient` | Reservanto API client, read only |
| `tests/` | tests for each project, architecture rules, repository checks, browser tests, API recordings |
| `docs/` | specification, decisions, conventions, look, IIS guide |

## Contributing

Report bugs and ideas as issues, send changes as pull requests.

- Follow the [conventions](docs/conventions.md). Comments are in Czech and explain why.
- `eng/check.ps1 -Mode Fast` must pass before a pull request. A change to the page should also pass `-Mode Full`.
- URL parameters, configuration keys, CSS variables and classes are a public interface that merchants' websites rely on. Describe a change to them in an issue first.
- The repository is public. Tokens, ClientIds, personal data and paths from your disk do not belong in code, tests, recordings or documents. Tests in `tests/AvailabilityView.Repository.Tests` check part of that.
- New API recordings come from the recorder, which removes tokens, contacts and real names (`tests/api-recordings/README.md`).

## Developing with AI

The project is developed with [Claude Code](https://claude.com/claude-code). Its setup is in the repository:

- `CLAUDE.md` and `.claude/`: instructions for the AI, a `Stop` hook that runs the fast checks before the end of a turn, and allowed commands.
- `docs/ai-working-agreement.md`: how the human and the AI work together, who decides what and how work is verified.
- `docs/project-profile.md`: the level of checks for each area of the project.
- `docs/templates/` and `docs/milestones/`: milestone templates and records.

Without Claude Code you can ignore these files, they do not affect building or running the app. Codex CLI is optionally used as an independent opponent during evaluation.

## License

[MIT](LICENSE), Copyright (c) 2026 Reservanto s.r.o.
