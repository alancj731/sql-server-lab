# Experiment: deadlock-account-transfer-v1

**Status: specified; implemented in Milestone 6.** The UI route `/labs/:id/deadlock-lab` is a placeholder until then.

## What it demonstrates

Two sessions update two account rows in opposite order, coordinated with barriers so each holds one lock and
requests the other. SQL Server detects the cycle and kills one session with error **1205** (the victim). The
deadlock graph is captured and shown: processes, statements (sanitised), resources, lock modes, owners, waiters,
and the victim.

**Blocking vs deadlocking:** blocking is one session waiting for another to release a lock — it resolves when the
holder commits. A deadlock is a cycle of waits that can never resolve on its own, so SQL Server must abort one
participant.

## Resolutions compared

1. **Consistent lock order** — both sessions touch accounts in ascending ID order; the same concurrent work
   completes without a deadlock.
2. **Bounded retry on 1205** (optional) — shows that retry *handles* a victim with exponential backoff and jitter;
   it does not prevent deadlocks.

## Safety

Hard timeouts and cancellation on both sessions; both transactions are rolled back or completed on success,
failure, timeout, or cancellation. Capture uses a dedicated Extended Events session (or `system_health`),
correlated by database, time window, application name, and session IDs. Raw XML and the parsed form are stored.
