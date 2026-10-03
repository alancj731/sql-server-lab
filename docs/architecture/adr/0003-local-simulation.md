# ADR 0003 — Local simulation provider

- Status: Accepted (Milestone 2)

## Context

Developers and coding agents must exercise the full Angular → API → job → worker flow without an Azure
subscription, including slow operations, failures, retries, and restarts.

## Decision

- Application logic depends only on interfaces (`ILabInfrastructure`, `ISqlLabExecutor`, `IMetricProvider`,
  `IBackupProvider`, `IPatchProvider`, `ISecretProvider`, `IClock`).
- `LocalLabInfrastructure` simulates VM lifecycle operations as **persisted** rows (`LocalSimOperations`) with a
  start time and duration, so progress is computed from the clock and survives worker restarts exactly like
  polling an Azure long-running operation. Power state is derived from the operation history.
- Delays are configurable (`LocalSimulation:*`); faults are deterministic by lab name (`fail` → permanent,
  `flaky` → first attempt transient) so tests and demos are reproducible.
- Simulated labs are flagged (`isSimulated`) and the UI shows a persistent "Simulated" banner.
- `Infrastructure:Mode` selects the provider; `Azure` fails fast until Milestone 3 implements it.

## Consequences

- The same worker code paths (claim, heartbeat, resume, retry, reconcile) are exercised locally and in tests.
- The simulator lives in the control database for convenience; it is never registered in Azure mode.
