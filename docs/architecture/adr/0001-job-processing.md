# ADR 0001 — Durable job processing in the control database

- Status: Accepted (Milestone 1)

## Context

VM creation, start, deallocation, deletion, backups, and patching take minutes and must survive browser, API, and
worker restarts. The plan requires idempotent, auditable, retry-aware, recoverable jobs, with only one mutating job
per lab at a time.

## Decision

- The control database **is the queue** for the MVP. The API inserts `LabJobs` rows; a separate worker process
  claims them with a conditional `UPDATE` that sets a unique lease token and expiry, and renews the lease with
  heartbeats. All subsequent writes are fenced by the lease token.
- One mutating job per lab is enforced by a filtered unique index on `MutexKey`; idempotency by a unique index on
  `IdempotencyKey`. Both hold under concurrency without application locks.
- Handlers persist the external operation ID (Azure deployment/LRO or simulated op) before polling, so a recovered
  job resumes polling instead of starting a second operation.
- The worker does not call the API. The API runs a `JobProgressBroadcaster` that polls rows changed since its last
  tick (with a 5 s overlap window and de-duplication) and pushes them to SignalR groups.

## Consequences

- No extra infrastructure (Service Bus, Redis) for the MVP; works identically on SQLite and Azure SQL.
- Progress latency is bounded by the broadcaster interval (~750 ms) — fine for minute-long operations.
- Polling load is small at lab-scale volumes. If the API scales out, each instance broadcasts to its own clients;
  Azure SignalR Service (or a backplane) is the upgrade path.
- SQLite needs WAL mode and a busy timeout for the API and worker to share a file locally.
