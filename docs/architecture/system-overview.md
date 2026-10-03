# System overview

```text
Angular SPA (frontend/)
    │  HTTPS /api/v1 (JSON, ProblemDetails)      SignalR /hubs/labs (labChanged, jobChanged)
    ▼
ASP.NET Core API (SqlServerLab.Api) ──────────► Control database (EF Core; SQLite local, Azure SQL hosted)
    │  validates, authorizes, enqueues jobs        Labs · LabJobs · AuditEvents · experiment/metric/backup/patch records
    │  JobProgressBroadcaster polls changes ◄──┐
    │                                          │
.NET Worker (SqlServerLab.Worker) ─────────────┘  claims jobs with leases, runs allow-listed handlers
    │
    ▼
ILabInfrastructure  ── LocalLabInfrastructure (simulated, Milestone 2)
                    └─ AzureLabInfrastructure  (ARM deployments of infra/bicep/lab, Azure mode)
```

## Request flow (example: deallocate)

1. `POST /api/v1/labs/{id}/deallocate` → `LabService` loads the lab **scoped to the caller** (404 otherwise),
   checks `LabStateMachine.IsAllowed(state, Deallocate)` (409 otherwise).
2. `JobService` inserts a `DeallocateVm` job with a deterministic idempotency key and a `MutexKey`; unique indexes
   make duplicate requests return the same job and concurrent mutations fail with 409. An audit event is written in
   the same transaction. The API returns **202** with the job and `statusUrl`.
3. The worker claims the job with an atomic conditional `UPDATE` (lease token + expiry), heartbeats while running,
   transitions the lab `Ready → Deallocating`, begins the infrastructure operation, persists its operation ID, polls
   it, then transitions `Deallocating → Stopped` and completes the job.
4. The API's broadcaster sees the changed rows and pushes `jobChanged`/`labChanged` to the lab's SignalR group.
   The UI never shows success optimistically; it renders backend state only and refetches after reconnects.

## Projects

| Project | Responsibility | Depends on |
| --- | --- | --- |
| Domain | Entities, enums, `LabStateMachine`, `LabJobPolicy`, naming/region/expiry policies | — |
| Application | Use cases (`LabService`, `JobService`), provider interfaces, DTOs, `Redactor` | Domain, EF Core abstractions |
| Infrastructure | `ControlDbContext` + migrations, `JobLeaseStore`, local simulator, fakes, DI | Application |
| Api | HTTP surface, auth, ProblemDetails, SignalR, health, OpenTelemetry | Infrastructure |
| Worker | `JobProcessor`/`JobRunner`, lifecycle handlers, `ExpiryService` | Infrastructure |

The API and worker are separate processes that share only the control database (ADR 0001).

## Key decisions

- [ADR 0001 — Durable job processing in the control database](adr/0001-job-processing.md)
- [ADR 0002 — Network topology](adr/0002-network-topology.md)
- [ADR 0003 — Local simulation provider](adr/0003-local-simulation.md)
