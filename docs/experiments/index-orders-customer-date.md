# Experiment: index-orders-customer-date-v1

**Status: specified; implemented in Milestone 5.** The UI route `/labs/:id/index-lab` is a placeholder until then.

## What it demonstrates

The same parameterised query against `dbo.Orders`, run with and without
`IX_Orders_CustomerId_OrderDate (CustomerId, OrderDate DESC) INCLUDE (Status, TotalAmount)`, normally changes a
clustered index scan into an index seek and cuts logical reads by orders of magnitude on the Medium dataset.

## What it does not prove

- That the index makes *every* workload faster (write cost and storage are not measured here).
- A specific duration improvement — Azure VM performance varies, so duration is reported but not asserted.
- Anything from a single execution; comparisons require repeated measurements.

## Data and warm-up policy

- Deterministic seed (Small 100 k / Medium 1 M orders) with recorded seed version and checksum.
- Fixed parameters selected from the generated data and stored with the run.
- 3 warm-up executions, then 10 measured executions on a dedicated connection whose application name contains the
  experiment-run ID; fixed session language/settings before parsing `STATISTICS IO/TIME` output.

## Metrics and how they are collected

| Metric | Source |
| --- | --- |
| Elapsed time P50/P95 | Client stopwatch per execution + SQL elapsed time |
| CPU ms | `STATISTICS TIME` (raw messages stored) |
| Logical / physical reads | `STATISTICS IO` (raw messages stored) |
| Rows | Result row count |
| Plan | Actual plan XML (stored compressed) summarised as scan/seek, index used, estimated vs actual rows, top operators |
| Waits | Session wait deltas |

Percentage changes are shown only when scenario version, dataset, parameters, VM, and benchmark policy match.
