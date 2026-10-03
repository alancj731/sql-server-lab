# SQL Server Performance Lab

A web application that creates **disposable SQL Server laboratories** for learning performance work —
index experiments, deadlocks, backups, and patching — with an Angular front end, an ASP.NET Core API,
and a separate .NET worker that runs every infrastructure operation as a durable, auditable job.

> **Educational lab, not a production SQL Server administration portal.**
> Users can never submit arbitrary SQL, PowerShell, ARM/Bicep, or shell commands. Every browser action maps
> to a server-side allow-listed command.

The product and architecture specification is [`SQL_SERVER_LAB_BUILD_PLAN.md`](SQL_SERVER_LAB_BUILD_PLAN.md).

## Status

| Milestone | Scope | Status |
| --- | --- | --- |
| 0 | Repository, build, lint, test, CI, ADRs | ✅ Done |
| 1 | Domain model, state machine, durable job engine, audit, job API, SignalR | ✅ Done |
| 2 | Local simulated vertical slice: create / start / deallocate / delete in the UI | ✅ Done |
| 3 | Azure platform + per-lab Bicep, real provisioning, Entra ID sign-in, one-command deploy | ✅ Done |
| 4–10 | Seeding, index & deadlock experiments, dashboard, backups, patching, hardening | Planned (next: 4) |

Pages for later milestones (Index lab, Deadlock lab, Backups, Maintenance) exist as clearly labelled placeholders.

## Repository layout

```text
backend/                       .NET 10 solution (SqlServerLab.slnx)
  src/SqlServerLab.Domain/         entities, LabStateMachine, job/expiry/naming policies
  src/SqlServerLab.Application/    LabService, JobService, provider interfaces, DTOs, redaction
  src/SqlServerLab.Infrastructure/ EF Core control DB, job lease store, local simulator, Azure provider
  src/SqlServerLab.Migrations.SqlServer/ control-DB migrations for Azure SQL
  src/SqlServerLab.Api/            minimal API (/api/v1), auth, ProblemDetails, SignalR hub
  src/SqlServerLab.Worker/         job runner, lifecycle handlers, expiry service
  tests/                           xUnit: Domain, Application, Infrastructure (+worker), Api
frontend/                      Angular 22 SPA, Playwright e2e (frontend/e2e)
docs/                          architecture, ADRs, experiments, operations
infra/bicep/                   platform (shared), apps, migrate job, per-lab VM templates
infra/scripts/                 deploy.sh, destroy.sh, smoke-lab.sh, build-bicep.sh
sql/                           lab database scripts (Milestones 4–6)
scripts/dev.sh                 run API + worker + Angular locally
```

## Prerequisites

- .NET SDK 10.0.1xx (`dotnet --list-sdks`)
- Node.js 22 LTS and npm 10
- Optional: `dotnet-ef` (`dotnet tool install -g dotnet-ef`) to add migrations; Docker for future SQL integration tests

No Azure subscription is needed for local development.

## Run locally (simulated infrastructure)

```bash
cd frontend && npm ci && cd ..
./scripts/dev.sh
```

Open http://localhost:4200. The API listens on http://localhost:5080 (OpenAPI at `/openapi/v1.json`,
health at `/health/live` and `/health/ready`). The control database is SQLite at `.data/controlplane.db`
and is migrated automatically in Development.

Local mode signs you in as `dev-user` via a development-only authentication handler (refused outside the
`Development`/`Testing` environments). Simulated delays and failures are configurable under `LocalSimulation`
in `appsettings.Development.json`. Lab names steer fault injection:

| Name contains | Behaviour |
| --- | --- |
| `fail` | provisioning fails permanently → lab `Failed` (deletable) |
| `flaky` | first provisioning attempt fails transiently → retried with backoff |

To run the pieces separately:

```bash
cd backend/src/SqlServerLab.Api && dotnet run          # :5080
cd backend/src/SqlServerLab.Worker && dotnet run
cd frontend && npm start                               # :4200, proxies /api and /hubs
```

## Tests and checks

```bash
# Backend
cd backend
dotnet restore && dotnet build          # warnings are errors
dotnet format --verify-no-changes
dotnet test

# Frontend
cd frontend
npm ci
npm run lint && npm run format:check
npm run build
npm test
npx playwright install chromium        # once
npm run e2e                            # starts its own API + worker + dev server on ports 5181/4301
```

After changing API contracts, run `npm run generate:api` in `frontend/` and commit the regenerated
`openapi.json` and `schema.d.ts`.

## Azure deployment

One script takes an empty subscription to a working HTTPS app with Microsoft Entra sign-in:

```bash
az login
./infra/scripts/deploy.sh --dry-run        # show the plan; changes nothing
./infra/scripts/deploy.sh --preflight-only # register providers, check vCPU quota
./infra/scripts/deploy.sh                  # full deploy (~25 minutes the first time), safe to re-run
./infra/scripts/smoke-lab.sh               # optional: create, stop, start, and delete one real lab
./infra/scripts/destroy.sh                 # remove everything (asks you to type the env name)
```

Defaults: environment `dev`, region `eastus2`, lab VM `Standard_D2s_v7`, $50/month budget alert to the
signed-in user. Details, phases, and re-run behaviour: [`docs/operations/deploy.md`](docs/operations/deploy.md).

## Cost warning

| Item | Approximate cost |
| --- | --- |
| Shared platform (Container Apps worker, Azure SQL Basic, ACR Basic, Key Vault, logs) | ~$35–60 / month, even with no labs |
| One running lab (Ubuntu 22.04 on Standard_D2s_v7; SQL Server 2022 Developer Edition is free) | ~$0.10 / hour |
| One lab's OS disk (64 GB Standard SSD) | ~$5 / month until the lab is deleted |

A deallocated lab still pays for its disk. Labs expire automatically (1–8 h initial TTL, 24 h maximum), and the
worker deletes orphaned lab resource groups, but **delete labs you no longer need** and run `destroy.sh` when you
are done with the environment.

## Cleanup

- Local: stop `scripts/dev.sh` (Ctrl+C) and delete `.data/` to reset all labs.
- Azure: delete labs from the UI; `./infra/scripts/destroy.sh --env dev [--include-entra]` removes the whole
  environment. See [`docs/operations/orphan-cleanup.md`](docs/operations/orphan-cleanup.md).

## Troubleshooting

See [`docs/operations/troubleshooting.md`](docs/operations/troubleshooting.md).
