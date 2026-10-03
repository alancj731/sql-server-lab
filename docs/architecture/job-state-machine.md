# Lab state machine and job lifecycle

## Lab states

Source of truth: `backend/src/SqlServerLab.Domain/Labs/LabStateMachine.cs` (tests in `LabStateMachineTests` cover
every from/to pair).

| From | Allowed next states |
| --- | --- |
| Requested | Provisioning, Deleting, Failed |
| Provisioning | Configuring, Failed |
| Configuring | Ready, Failed |
| Ready | Deallocating, Maintenance, Deleting, Failed |
| Deallocating | Stopped, Failed |
| Stopped | Starting, Deleting, Failed |
| Starting | Configuring, Ready, Failed |
| Maintenance | Ready, Failed |
| Deleting | Deleted, Failed |
| Failed | Deleting |
| Deleted | — (terminal) |

Reconciliation (`LabStateMachine.Reconcile`) may adopt any observed state except resurrecting a `Deleted` lab.

### User commands by state

| State | Start | Deallocate | Delete | Extend expiry |
| --- | --- | --- | --- | --- |
| Ready | | ✓ | ✓ | ✓ |
| Stopped | ✓ | | ✓ | ✓ |
| Failed | | | ✓ | |
| Requested / Provisioning / Configuring / Starting / Deallocating / Maintenance | | | | ✓ |
| Deleting / Deleted | | | | |

Start, Deallocate, and Delete are additionally disabled while any mutating job is active. The API returns the
effective list as `allowedActions` so the UI never re-implements these rules.

## Job lifecycle

```text
Queued ──claim──► Running ──► Succeeded
  ▲                 │ ├────► Failed      (permanent / validation / authorization / quota, or retries exhausted)
  │                 │ └────► Cancelled   (cancel requested; a ReconcileLab job is queued)
  └──reschedule─────┘        (transient error, backoff with full jitter, attempts < max)
```

| Rule | Implementation |
| --- | --- |
| Atomic claim | Conditional `UPDATE … WHERE Status=Queued AND NotBefore<=now OR Status=Running AND LeaseExpiresAt<now`; one row affected = claimed. Each claim gets a unique lease token. |
| Fencing | Every later write is conditioned on the lease token; a worker whose lease expired gets `LeaseLostException`. |
| Heartbeat | Renews the lease every `Worker:HeartbeatIntervalMs`; also observes `CancelRequested`. |
| Recovery | Expired leases are reclaimed (audited as `job.lease-recovered`). Handlers resume from the persisted `ExternalOperationId`, so no duplicate external operation is started. More than 10 claims → failed. |
| One mutation per lab | `MutexKey` = lab ID while a mutating job is active; filtered unique index. |
| Idempotency | `IdempotencyKey` unique: client `Idempotency-Key` header (scoped to user) or `{labId}:{type}:{lab.RowVersion}`. Finishing a job bumps the lab's `RowVersion`. |
| Retry policy | `LabJobPolicy`: only `Transient` errors retry, max 5 attempts, backoff `U(0, min(2 min, 2 s·2^(n-1)))`. `DeleteLab` and `InstallPatches` never auto-retry; an ambiguous transient failure queues `ReconcileLab` instead. |
| Error categories | Transient, Permanent, Authorization, Quota, Validation, Cancellation. Messages are redacted. |
| Expiry | `ExpiryService` queues `DeleteLab` (actor `system`, audited `lab.expired`) for labs past `ExpiresAt` that allow deletion. |
