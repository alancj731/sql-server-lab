# Patching

**Implemented in Milestone 9** with Azure Update Manager (no second automatic patch mechanism on the VM).

- **Assessment** is a durable job that lists updates (title, classification, KB, restart requirement) and never
  installs anything.
- **Installation** requires the admin role, a recent successful backup (or explicit waiver), typed confirmation,
  and selected updates. The lab enters `Maintenance`, conflicting experiments and metric queries stop, the Azure
  operation ID is persisted, and the job survives VM and worker restarts. SQL/OS versions are recorded before and
  after; the lab returns to `Ready` or a diagnosable `Failed` state.
- `InstallPatches` is never retried automatically; an ambiguous result triggers reconciliation.
- Major-version upgrades are out of scope; they require a new VM plus backup/restore.
