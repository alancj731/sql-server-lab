# SQL Server Performance Lab — Agent-Executable Build Plan

This document is the source of truth for building the project with Codex, Claude Code, or another coding agent. The agent should implement the phases in order and must not redesign the system unless a requirement is impossible or unsafe.

## 1. Agent operating instructions

When executing this plan:

1. Inspect the repository, existing instructions, installed SDKs, and current tests before editing.
2. Maintain a short task plan and complete one milestone at a time.
3. Do not put Azure credentials, SQL passwords, connection strings, or generated secrets in source control, logs, API responses, or Angular configuration.
4. Do not expose an endpoint that accepts arbitrary SQL, PowerShell, Bicep, Azure resource IDs, or shell commands from the browser.
5. All browser-triggered operations must map to server-side allow-listed commands.
6. Treat VM creation, start, stop, backup, patching, and deletion as asynchronous jobs. Never hold an HTTP request open until an Azure operation completes.
7. Every job must be idempotent, auditable, retry-aware, and recoverable after API or worker restart.
8. Add or update automated tests with every phase. Do not advance while the current phase's tests fail.
9. Preserve user changes and do not perform destructive Git operations.
10. At the end of each phase, report:
    - files changed;
    - tests and validation run;
    - remaining limitations;
    - the next phase to execute.
11. Stop and request input only when credentials, subscription choices, Azure quotas, or a materially different product decision are required.

## 2. Product objective

Build a secure web application that creates disposable SQL Server laboratories on Azure. A user can:

- provision a Windows Server VM with SQL Server Developer Edition;
- start and deallocate the VM;
- generate deterministic sample data;
- compare an identical workload with and without an index;
- inspect duration, CPU, logical reads, waits, and query-plan information;
- deliberately create a controlled SQL Server deadlock;
- inspect the victim, participating sessions, resources, and deadlock graph;
- run corrected deadlock variants and compare the outcome;
- create and verify a database backup;
- assess and install approved SQL Server/Windows patches;
- view infrastructure and database telemetry;
- delete the complete lab;
- rely on automatic expiry so abandoned labs do not run indefinitely.

This is an educational lab. It must not be positioned as a production SQL Server administration portal.

## 3. Scope and release boundaries

### Release 1 — core MVP

- Authentication and authorization.
- Lab list, create, detail, start, deallocate, and delete.
- Azure resource provisioning through Bicep.
- Automatic expiry and cleanup.
- Deterministic data generation.
- One index experiment with repeatable before/after measurement.
- One deterministic two-session deadlock experiment.
- One corrected consistent-lock-order deadlock experiment.
- SQL Server metrics and basic Azure VM metrics.
- Job progress, operation history, and audit log.
- Local development mode that does not require Azure for frontend/API development.

### Release 2 — operations

- Full backup to Azure Blob Storage.
- Backup verification and test restore.
- Patch assessment.
- Explicitly approved patch installation through Azure Update Manager.
- Maintenance-mode UI and restart recovery.

### Release 3 — optional extensions

- Additional index/selectivity/cardinality scenarios.
- Blocking, isolation-level, parameter-sniffing, statistics, and fragmentation labs.
- Query-plan graphical rendering.
- Major-version migration to a newly provisioned VM.
- Multi-region deployment and administrative cost reporting.

Do not implement arbitrary query execution, public RDP, public SQL access, or an in-place major SQL Server upgrade in any release.

## 4. Fixed technical decisions

Use these choices unless the repository already contains an equivalent standard:

- Frontend: current stable Angular, standalone components, strict TypeScript, Angular Material, RxJS, and Signals where appropriate.
- Backend: current .NET LTS, ASP.NET Core Web API, C#, nullable reference types, OpenAPI, and SignalR.
- Worker: .NET Worker Service in a separate process/project.
- Persistence: EF Core. SQLite for local development and tests; Azure SQL Database for hosted control-plane state.
- SQL connectivity: `Microsoft.Data.SqlClient`.
- Azure infrastructure: Bicep and Azure Resource Manager .NET SDKs.
- Authentication: Microsoft Entra ID using authorization-code flow with PKCE in Angular and JWT bearer validation in the API.
- Tests: xUnit for .NET, Angular's supported unit test runner, Playwright for browser tests, and Testcontainers for SQL integration tests when Docker is available.
- Observability: OpenTelemetry, Application Insights, structured logs, correlation IDs, and health checks.
- Styling: accessible responsive dashboard; no custom design system in the MVP.

Record the exact selected versions in `docs/architecture/versions.md` and lock them. Do not upgrade dependencies during the build unless needed for security or compatibility.

## 5. Target architecture

```text
Angular SPA
    |
    | HTTPS, JWT, SignalR
    v
ASP.NET Core API ---------------------- Control database
    |                                  - labs
    | creates commands                 - jobs
    | reads status/metrics             - experiments
    v                                  - audit events
.NET Worker
    |-- Bicep/ARM provisioning
    |-- VM lifecycle operations
    |-- allow-listed SQL experiments
    |-- metric collection
    |-- backup and patch orchestration
    v
Shared Azure platform resources
    |-- application hosting
    |-- shared VNet and subnets
    |-- Key Vault
    |-- backup storage
    |-- Log Analytics/Application Insights
    |
    +-- Per-lab resource group
          |-- SQL Server Developer VM
          |-- NIC and managed disks
          |-- SQL IaaS Agent resource
          `-- monitoring extensions
```

The control database, Key Vault, backup storage, and application hosting must not be inside a disposable lab resource group.

## 6. Azure topology and security requirements

Create a shared application resource group containing:

- application hosting for the API, worker, and Angular assets;
- Azure SQL control database;
- Key Vault;
- shared backup storage account;
- Log Analytics workspace and Application Insights;
- shared virtual network;
- application subnet and lab-VM subnet;
- optional Azure Bastion for administrators only.

Create one resource group per lab containing:

- a Windows Server VM from a SQL Server Developer marketplace image;
- NIC attached to the shared lab subnet;
- OS and SQL data disks;
- SQL IaaS Agent registration/resource;
- Azure Monitor Agent and required associations.

Security constraints:

- Do not create a public IP for lab VMs.
- Allow TCP 1433 only from the application/worker subnet.
- Do not allow internet-originated RDP.
- Use TLS for API traffic and SQL connections.
- Give the application managed identity a custom Azure role limited to approved lab operations and naming/tag rules.
- Generate VM and bootstrap SQL secrets server-side and store them in Key Vault.
- Never return VM administrator or SQL administrator passwords to the browser.
- Create a least-privileged SQL login/user for experiments after bootstrap. Do not use `sa` for normal operations.
- Tag every lab resource with `labId`, `ownerId`, `environment`, `createdAt`, and `expiresAt`.
- Validate resource names and subscription/resource-group scope server-side.
- Require typed confirmation for lab deletion and patch installation.

## 7. Repository layout

Create this layout, adapting names only if the repository already establishes a convention:

```text
SqlServerLab.sln
src/
  SqlServerLab.Web/                 Angular application
  SqlServerLab.Api/                 ASP.NET Core API
  SqlServerLab.Worker/              Durable job executor/reconciler
  SqlServerLab.Domain/              Entities, enums, value objects, rules
  SqlServerLab.Application/         Use cases, interfaces, DTO mapping
  SqlServerLab.Infrastructure/      EF Core, Azure SDK, SqlClient, telemetry
tests/
  SqlServerLab.Domain.Tests/
  SqlServerLab.Application.Tests/
  SqlServerLab.Infrastructure.Tests/
  SqlServerLab.Api.Tests/
  SqlServerLab.E2E/
infra/
  bicep/
    platform/
    lab/
    modules/
  scripts/
sql/
  migrations/
  seed/
  experiments/
    index-orders-customer-date/
    deadlock-account-transfer/
docs/
  architecture/
  operations/
  experiments/
.github/workflows/                 or the repository's existing CI system
```

Add a root README containing prerequisites, local startup, test commands, Azure deployment, cost warning, cleanup instructions, and troubleshooting.

## 8. Domain model

Implement these primary records/entities:

### Lab

- `Id: Guid`
- `OwnerId: string`
- `Name: string`
- `Region: string`
- `ResourceGroupName: string`
- `VmResourceId: string?`
- `SqlVmResourceId: string?`
- `State: LabState`
- `StateReason: string?`
- `CreatedAt`, `UpdatedAt`, `ExpiresAt: DateTimeOffset`
- `RowVersion` concurrency token

`LabState` values:

```text
Requested
Provisioning
Stopped
Starting
Configuring
Ready
Deallocating
Maintenance
Deleting
Deleted
Failed
```

### LabJob

- `Id`, `LabId`, `Type`, `Status`
- serialized validated input, never arbitrary code
- progress percentage and current step
- attempt count and maximum attempts
- lease owner and lease expiry
- correlation ID
- requested, started, heartbeat, and completed timestamps
- sanitized error code and message
- idempotency key

Job types:

```text
ProvisionLab
StartVm
DeallocateVm
DeleteLab
SeedDatabase
ApplyIndex
RemoveIndex
RunIndexBenchmark
RunDeadlock
RunDeadlockResolution
CollectMetrics
CreateBackup
VerifyBackup
AssessPatches
InstallPatches
ReconcileLab
```

### ExperimentRun

- ID, lab ID, scenario ID, scenario version, run status
- index/configuration state
- parameters and warm-up policy
- start/end timestamps
- raw measurement count
- P50/P95 duration
- CPU milliseconds
- logical/physical reads
- returned row count
- plan XML storage reference or compressed value
- plan summary
- wait summary
- failure information

### Other records

- `MetricSample`
- `DeadlockEvent`
- `BackupRecord`
- `PatchAssessment`
- `AuditEvent`

Audit events must be append-only at the application layer.

## 9. Job engine requirements

Implement the control database as the durable job queue for the MVP. The API inserts jobs; the worker leases and executes them.

Required behavior:

- Only one destructive/mutating job runs for a lab at a time.
- Read-only metric collection may run concurrently when safe.
- Claim jobs atomically using a lease and concurrency token.
- Renew the lease with heartbeats.
- Recover jobs whose worker lease expired.
- Use deterministic idempotency keys for repeated requests.
- Store the Azure deployment or operation identifier so polling can resume after restart.
- Classify errors as transient, permanent, authorization, quota, validation, or cancellation.
- Use exponential backoff with jitter only for transient failures.
- Do not automatically retry patch installation or lab deletion after an ambiguous external result; reconcile actual Azure state first.
- Publish progress through SignalR groups scoped to authorized lab users.
- Add a reconciliation service that compares control-plane state with Azure state.

## 10. API contract

Use `/api/v1`. Return RFC 9457-style problem details for errors. Mutating operations return `202 Accepted` with a job resource.

```text
GET    /api/v1/labs
POST   /api/v1/labs
GET    /api/v1/labs/{labId}
POST   /api/v1/labs/{labId}/start
POST   /api/v1/labs/{labId}/deallocate
POST   /api/v1/labs/{labId}/extend-expiration
DELETE /api/v1/labs/{labId}

POST   /api/v1/labs/{labId}/database/seed
GET    /api/v1/labs/{labId}/database/status

GET    /api/v1/labs/{labId}/experiments
POST   /api/v1/labs/{labId}/experiments/index-orders/run
PUT    /api/v1/labs/{labId}/experiments/index-orders/index
DELETE /api/v1/labs/{labId}/experiments/index-orders/index
POST   /api/v1/labs/{labId}/experiments/deadlock/run
POST   /api/v1/labs/{labId}/experiments/deadlock/run-resolution
GET    /api/v1/labs/{labId}/experiments/{runId}

GET    /api/v1/labs/{labId}/metrics?from=&to=&resolution=
GET    /api/v1/labs/{labId}/deadlocks

POST   /api/v1/labs/{labId}/backups
POST   /api/v1/labs/{labId}/backups/{backupId}/verify
GET    /api/v1/labs/{labId}/backups

POST   /api/v1/labs/{labId}/patches/assess
POST   /api/v1/labs/{labId}/patches/install
GET    /api/v1/labs/{labId}/patches

GET    /api/v1/jobs/{jobId}
POST   /api/v1/jobs/{jobId}/cancel
GET    /api/v1/labs/{labId}/audit
```

Minimum job response:

```json
{
  "jobId": "uuid",
  "labId": "uuid",
  "type": "RunIndexBenchmark",
  "status": "Queued",
  "progress": 0,
  "statusUrl": "/api/v1/jobs/uuid"
}
```

Use OpenAPI generation and generate or strongly type the Angular API client. Do not manually duplicate API types in multiple frontend files.

## 11. Angular application

Create these routes:

```text
/labs
/labs/new
/labs/:labId/overview
/labs/:labId/index-lab
/labs/:labId/deadlock-lab
/labs/:labId/backups
/labs/:labId/maintenance
/labs/:labId/audit
```

Required UI behavior:

- Lab list shows state, region, owner, expiry, VM power state, and current job.
- Lab detail has persistent state and job progress.
- Disable actions that conflict with lab state or active jobs.
- Index page shows baseline and indexed results side-by-side using the same units and scale.
- Deadlock page shows the two transaction timelines, victim, resources, and resolution result.
- Dashboard shows SQL CPU, VM CPU, disk latency, batches/sec, waits, active sessions, and blocked sessions.
- Backup and patch pages require confirmation and show durable job progress.
- Deletion requires typing the lab name.
- SignalR events update the UI; reconnect and refetch state after disconnect.
- All charts have textual equivalents and accessible labels.
- Support widths from 360px upward and keyboard-only use.
- Never use optimistic success for Azure operations. Display `requested`, `running`, `succeeded`, or `failed` based on backend state.

## 12. Local development provider

Define interfaces so application logic does not depend directly on Azure SDK types:

```text
ILabInfrastructure
ISqlLabExecutor
IMetricProvider
IBackupProvider
IPatchProvider
ISecretProvider
IClock
```

Implement:

- `LocalLabInfrastructure`, which simulates VM states and configurable delays/failures;
- `AzureLabInfrastructure`, which uses Bicep/ARM;
- `SqlServerLabExecutor`, which uses a local SQL Server container or Azure VM;
- fake backup and patch providers for UI/local testing.

Local mode must let an agent or developer exercise the complete Angular/API/job flow without an Azure subscription. Clearly label simulated operations in the UI.

## 13. SQL database lab design

Create a versioned `SqlLab` database with:

```text
dbo.Customers
dbo.Products
dbo.Orders
dbo.OrderLines
dbo.Accounts
dbo.LabSchemaVersion
dbo.SeedManifest
```

Recommended `Orders` columns:

```text
OrderId bigint primary key clustered
CustomerId int not null
OrderDate datetime2 not null
Status tinyint not null
TotalAmount decimal(18,2) not null
Notes nvarchar(200) null
```

Seed data must be deterministic from a seed value and generated set-wise or in bounded batches on SQL Server. Supported presets:

- Small: 100,000 orders
- Medium: 1,000,000 orders
- Large: 10,000,000 orders, enabled only when the VM/disk profile supports it

Store seed version, requested size, actual counts, start/end time, and checksum-like validation results. A repeated seed command with the same version must not duplicate data.

Enable Query Store with an explicit size, cleanup policy, capture mode, and wait-stat capture. Do not leave defaults undocumented.

## 14. Index experiment specification

Scenario ID: `index-orders-customer-date-v1`.

Test query:

```sql
SELECT OrderId, OrderDate, Status, TotalAmount
FROM dbo.Orders
WHERE CustomerId = @CustomerId
  AND OrderDate >= @FromDate
  AND OrderDate < @ToDate
ORDER BY OrderDate DESC;
```

Test index:

```sql
CREATE INDEX IX_Orders_CustomerId_OrderDate
ON dbo.Orders(CustomerId, OrderDate DESC)
INCLUDE(Status, TotalAmount);
```

Rules:

1. Index creation and removal must check actual catalog state and be idempotent.
2. Use fixed, recorded parameters selected from generated data.
3. Default benchmark policy: three warm-up executions followed by ten measured executions.
4. Use a dedicated connection and application name containing the experiment-run ID.
5. Record every measured execution, then calculate P50 and P95.
6. Capture elapsed time, SQL CPU time, logical reads, physical reads where available, row count, actual plan XML, and relevant waits.
7. Store raw `STATISTICS IO/TIME` messages if used, not only parsed values.
8. Set a known language/session configuration before parsing SQL messages.
9. Summarize the plan as scan/seek, chosen index, estimated versus actual rows, and highest-cost operators.
10. Never claim improvement from a single execution.
11. Show percentage changes only when baseline and comparison use the same scenario version, dataset, parameters, VM, and benchmark policy.

Acceptance target on the medium dataset: the index run must normally show an index seek and substantially fewer logical reads than the no-index run. Do not hard-code a required duration improvement because Azure VM performance varies.

## 15. Deadlock experiment specification

Scenario ID: `deadlock-account-transfer-v1`.

Seed at least two account rows. The worker opens two independent SQL connections and coordinates them with barriers:

```text
Session A
  BEGIN TRANSACTION
  update Account 1
  wait until Session B updated Account 2
  update Account 2

Session B
  BEGIN TRANSACTION
  update Account 2
  wait until Session A updated Account 1
  update Account 1
```

Requirements:

- Use cancellation and a hard timeout so the worker cannot wait forever.
- Expect one session to receive SQL error 1205.
- Roll back or safely complete both transactions.
- Capture the deadlock XML using a dedicated Extended Events session or the `system_health` session.
- Correlate by database, time window, client application name, and session IDs.
- Parse processes, statements, resources, lock modes, owners, waiters, and victim.
- Sanitize displayed statement text.
- Store the raw XML and parsed representation.

Resolution variant 1 must access both accounts in ascending account-ID order and demonstrate that the same concurrent work completes without a deadlock.

Resolution variant 2 may demonstrate a bounded retry on error 1205 with exponential backoff and jitter. It must display that retry handles a victim; it must not claim that retry prevents deadlocks.

The UI explanation must distinguish blocking from deadlocking.

## 16. Metrics and dashboard

Use two telemetry sources:

### Direct SQL samples

Collect every five seconds while a lab is `Ready`, `Maintenance`, or running an experiment:

- active sessions;
- blocked sessions and blocking chains;
- batch requests/sec;
- transactions/sec;
- SQL process CPU when available;
- buffer cache and memory indicators;
- file read/write latency derived from cumulative DMV deltas;
- top wait-category deltas;
- database size and log usage.

Store raw counter values required for correct delta calculation. Reset baselines when SQL Server restarts.

### Azure metrics

Collect at the available platform granularity:

- VM CPU;
- disk IOPS/throughput/latency where exposed;
- network bytes;
- VM availability/power state.

Do not merge metrics with different time resolution without labeling them. Experiment comparisons must use direct SQL measurements; Azure Monitor is contextual infrastructure telemetry.

Retention:

- five-second samples for 24 hours;
- optional one-minute rollups for 30 days;
- delete samples when their retention expires, independently of lab deletion if audit policy requires it.

## 17. Backup workflow

Implement after the MVP:

1. Confirm lab is ready and not running another mutating operation.
2. Create a unique blob path containing lab ID, database name, UTC time, and backup ID.
3. Use `BACKUP DATABASE ... TO URL` with managed identity where the selected SQL version supports it.
4. Report progress when SQL Server exposes it.
5. Record backup type, size, checksum setting, start/end time, SQL version, database backup LSN metadata, and blob URI without credentials.
6. Run `RESTORE VERIFYONLY`.
7. Offer an explicit test-restore job to a temporary database name.
8. Drop the temporary validation database after validation.
9. Apply retention through a lifecycle policy and application metadata.

Never expose SAS tokens in the frontend. If a platform limitation requires a SAS credential, keep it in Key Vault and redact it from SQL/log output.

## 18. Patch workflow

Use Azure Update Manager. Do not enable a second automatic patch mechanism on the same VM.

Patch assessment:

- start a durable assessment job;
- list update title, classification, KB identifier when available, restart requirement, and assessment time;
- store the assessment result;
- do not install anything.

Patch installation:

- require administrator role;
- require a recent successful backup or explicit policy waiver;
- require typed confirmation and selected updates/classifications;
- place the lab in `Maintenance`;
- stop experiments and metric queries that conflict;
- initiate the Azure operation and persist its identifier;
- survive VM restart and worker restart;
- wait for VM availability and SQL health checks;
- record SQL and OS versions before and after;
- return the lab to `Ready` or a diagnosable `Failed` state.

Do not implement major-version upgrades as patch installation. A future major-version workflow must use a new VM plus backup/restore and validation.

## 19. Implementation milestones

### Milestone 0 — bootstrap

Tasks:

- Create the repository structure and solution.
- Add build, formatting, linting, and test commands.
- Add configuration validation and secret-safe local templates.
- Add architecture decision records for job processing, network topology, and local simulation.
- Add CI that builds Angular and .NET and runs unit tests.

Exit criteria:

- A clean checkout can restore, build, lint, and test with documented commands.
- No Azure subscription is required.

### Milestone 1 — domain and durable jobs

Tasks:

- Implement entities, enums, EF mappings, migrations, and repositories.
- Implement the lab state machine as a pure tested domain service.
- Implement job enqueue, atomic lease, heartbeat, retry classification, cancellation, and reconciliation interfaces.
- Add audit-event writing.
- Add API job endpoints and SignalR progress.

Exit criteria:

- Unit tests cover every allowed and forbidden state transition.
- A worker restart resumes or reconciles an expired leased job.
- Duplicate idempotency keys do not create duplicate external operations.

### Milestone 2 — local vertical slice

Tasks:

- Implement local infrastructure simulation.
- Build lab list, create, overview, start, deallocate, and delete UI.
- Connect Angular to the generated API client and SignalR.
- Add fake progress, failures, and retry scenarios.
- Add authorization checks and ownership filtering.

Exit criteria:

- Playwright can create, start, stop, and delete a simulated lab.
- Refreshing the browser during a job preserves accurate state.
- Conflicting buttons are disabled.

### Milestone 3 — Azure platform and lab provisioning

Tasks:

- Implement shared-platform and per-lab Bicep.
- Add Bicep lint/validation and documented deployment parameters.
- Implement Azure resource naming and tags.
- Implement provisioning, start, deallocate, delete, and reconciliation adapters.
- Configure private network access, VM identity, SQL IaaS Agent, monitoring, and Key Vault secrets.
- Implement SQL readiness health checks.
- Implement expiry cleanup.

Exit criteria:

- One command deploys shared development infrastructure.
- A UI request provisions a real lab and eventually reaches `Ready`.
- Deallocate and restart work after API/worker restarts.
- Delete removes all resources in the lab resource group.
- Expired labs are cleaned automatically.

### Milestone 4 — database setup and seeding

Tasks:

- Add versioned SQL schema and migration runner.
- Implement least-privileged experiment user.
- Implement seed manifests and Small/Medium presets.
- Enable/configure Query Store.
- Show seed progress, counts, and validation in Angular.

Exit criteria:

- Seed is deterministic and idempotent.
- Interrupted seed can be safely retried or reset.
- Row counts and manifest version are visible.

### Milestone 5 — index experiment

Tasks:

- Implement index catalog checks, create, and remove.
- Implement warm-up and repeated measured runs.
- Capture and store measurements and plan XML.
- Parse a safe plan summary.
- Build before/after UI and chart accessibility text.
- Add integration and end-to-end tests.

Exit criteria:

- Results prove both runs used identical experiment inputs.
- The medium dataset normally produces scan versus seek and a large logical-read difference.
- Raw measurements remain inspectable.

### Milestone 6 — deadlock experiment

Tasks:

- Implement coordinated two-connection deadlock generator.
- Capture and parse deadlock XML.
- Implement consistent-order resolution and optional retry demonstration.
- Build transaction timeline and resource/victim UI.
- Add cleanup, timeout, and repeated-run tests.

Exit criteria:

- The scenario deterministically captures a deadlock in repeated integration runs.
- The corrected ordering completes without a deadlock.
- No open transaction remains after success, failure, timeout, or cancellation.

### Milestone 7 — live dashboard

Tasks:

- Implement direct SQL sampler and DMV delta calculations.
- Integrate Azure metrics.
- Add retention cleanup and downsampling if needed.
- Build responsive dashboard and history range selection.
- Label sample source, resolution, units, and freshness.

Exit criteria:

- Dashboard survives SQL/VM restart without invalid counter deltas.
- Blocked sessions appear during a blocking/deadlock scenario.
- SQL experiment metrics are not confused with Azure platform averages.

### Milestone 8 — backups

Tasks:

- Provision shared backup storage and identity permissions.
- Implement backup, verify, optional test restore, metadata, and retention.
- Build backup UI and audit entries.
- Test failed, canceled, and duplicate requests.

Exit criteria:

- A full backup is created without exposing credentials.
- Verification succeeds and is recorded.
- Test restore validates the backup and removes the temporary database.

### Milestone 9 — patching

Tasks:

- Implement Update Manager assessment.
- Build assessment UI.
- Implement confirmation-gated installation and maintenance state.
- Reconcile restarts and collect before/after versions.
- Add audit and failure-recovery paths.

Exit criteria:

- Assessment never installs updates.
- Installation cannot begin without authorization and confirmation.
- The job recovers after VM restart and reports the final SQL health state.

### Milestone 10 — hardening and release

Tasks:

- Threat model the API, worker, Azure role, SQL permissions, and browser.
- Add rate limiting, request validation, security headers, and log redaction tests.
- Add resource quotas, per-user limits, TTL enforcement, and cost-warning UI.
- Add health dashboards and operational alerts.
- Run load, accessibility, failure-injection, and Azure cleanup tests.
- Complete deployment, rollback, incident, and orphan-cleanup runbooks.

Exit criteria:

- All release criteria in Section 22 pass.
- No high-severity threat-model item remains open.
- A clean environment can be deployed and removed from documentation alone.

## 20. Automated test matrix

### Unit tests

- Every lab-state transition.
- Job retry/error classification.
- Idempotency-key behavior.
- Ownership and role policies.
- Metric delta/reset calculations.
- Benchmark comparison compatibility rules.
- Deadlock XML parser with captured fixtures.
- Plan-summary parser with captured fixtures.
- Secret and error-message redaction.

### Integration tests

- EF migrations on SQLite and SQL Server.
- Atomic job leasing with competing workers.
- Index creation/removal and benchmark collection.
- Deterministic deadlock and corrected ordering.
- SQL connection loss and command cancellation.
- Query Store and Extended Events setup.

### API tests

- Authentication and unauthorized access.
- Cross-user lab access denial.
- Validation and problem details.
- `202` plus job resource for commands.
- Conflicting-operation rejection.
- Deletion and patch confirmation requirements.

### Angular/Playwright tests

- Local simulated lifecycle.
- Page refresh during active job.
- SignalR reconnect.
- Index comparison display.
- Deadlock victim and resolution display.
- Keyboard navigation and 360px responsive layout.

### Azure smoke tests

- Bicep what-if/lint.
- Provision one minimal lab.
- Reach SQL readiness.
- Seed Small dataset.
- Run both core experiments.
- Deallocate/start.
- Delete resource group.
- Confirm no orphaned disks, NICs, public IPs, or secrets.

Run real Azure smoke tests manually or on a protected scheduled pipeline, never for every pull request.

## 21. CI/CD requirements

Pull-request pipeline:

1. Verify formatting.
2. Restore dependencies.
3. Build .NET with warnings treated appropriately.
4. Run .NET unit/API tests.
5. Build and test Angular.
6. Run Bicep lint.
7. Scan for committed secrets and vulnerable dependencies.
8. Publish test and coverage results.

Deployment pipeline:

1. Build immutable application artifacts.
2. Deploy Bicep with environment-specific parameter files and secret references.
3. Apply control-database migrations as an explicit step.
4. Deploy API and worker.
5. Deploy Angular.
6. Run health and local-provider smoke tests.
7. Optionally run a protected real-Azure lab smoke test.

Use workload identity/OIDC for CI Azure authentication. Do not store long-lived service-principal secrets in the repository or pipeline configuration.

## 22. Definition of done for the complete MVP

The MVP is complete only when all of the following are true:

- A new user can sign in and see only authorized labs.
- A user can create a lab without using Azure Portal.
- Provisioning progress survives browser, API, and worker restarts.
- A ready lab can be deallocated, restarted, expired, and deleted.
- No public SQL or RDP access exists.
- Seed operations are deterministic and idempotent.
- Index comparison records repeated raw measurements and compatible before/after context.
- The index scenario displays a scan/seek difference and logical-read comparison.
- The deadlock scenario reliably records error 1205 and a correlated deadlock graph.
- The consistent-order variant completes without deadlock.
- The dashboard labels data source, units, sampling resolution, and freshness.
- Users cannot invoke arbitrary SQL, PowerShell, ARM, or shell commands.
- Secrets do not appear in client code, API responses, application logs, or audit records.
- Every destructive and maintenance operation is authorized, confirmed, and audited.
- Per-user concurrency and lab TTL limits are enforced.
- Automated cleanup removes expired and failed lab resources.
- CI tests pass, Azure smoke testing passes, and operational documentation is complete.

## 23. Required documentation

Create and maintain:

- `README.md`: setup, local development, tests, deployment, cost warning, and cleanup.
- `docs/architecture/system-overview.md`.
- `docs/architecture/security-model.md`.
- `docs/architecture/job-state-machine.md`.
- `docs/architecture/versions.md`.
- `docs/experiments/index-orders-customer-date.md`.
- `docs/experiments/deadlock-account-transfer.md`.
- `docs/operations/deploy.md`.
- `docs/operations/backup-and-restore.md`.
- `docs/operations/patching.md`.
- `docs/operations/orphan-cleanup.md`.
- `docs/operations/troubleshooting.md`.

Each experiment document must explain what the demonstration proves, what it does not prove, its data and warm-up policy, and how the displayed metrics are collected.

## 24. First prompt to give the coding agent

Use this prompt with this plan attached or placed at the repository root:

> Implement Milestone 0 from `SQL_SERVER_LAB_BUILD_PLAN.md`. Treat that file as the product and architecture specification. First inspect the repository and available SDKs, then create a concise task plan. Implement only Milestone 0, including tests and documentation. Do not require Azure credentials and do not begin Milestone 1. Run every relevant validation command, fix failures, and finish with changed files, commands run, limitations, and the exact next milestone.

After Milestone 0 is accepted, use:

> Continue with Milestone 1 from `SQL_SERVER_LAB_BUILD_PLAN.md`. Inspect the current implementation and preserve existing conventions. Implement all Milestone 1 tasks and exit criteria, add tests, run validation, and stop before Milestone 2. Report changed files, test results, limitations, and the next milestone.

Repeat the second prompt with the milestone number changed. Do not ask one agent turn to implement the entire system at once.
