# ADR 0002 — Network topology

- Status: Accepted; implemented in Milestone 3 (`infra/bicep/modules/core.bicep`)

## Decision

- A **shared application resource group** holds hosting for the API/worker/SPA, the Azure SQL control database,
  Key Vault, backup storage, Log Analytics/Application Insights, and one VNet with an application subnet and a
  lab-VM subnet. Optional Azure Bastion for administrators only.
- Each lab gets its **own resource group** containing only disposable resources (VM, NIC, disks, SQL IaaS Agent
  resource, monitoring associations). Deleting a lab deletes its resource group.
- Lab VMs have **no public IP**. The lab subnet NSG allows TCP 1433 **only** from the application subnet and denies
  inbound internet traffic, including RDP.
- The worker reaches SQL Server over the private network with TLS, using a lab login whose secret lives in Key Vault.
- Lab VMs run **Ubuntu 22.04 with SQL Server 2022 Developer Edition** (installed on first boot), not Windows Server
  as the original build plan specified. Changed at the product owner's request: the engine features the labs teach
  are identical, Linux VMs cost about half as much, and SQL-on-Linux marketplace images were not offered in the
  deployment region. Lab VMs need outbound internet for the apt install (subnet default outbound access).
- The control database may live in a different region from the apps (e.g. Canada Central while apps run in East US 2)
  when SQL provisioning is restricted; its private endpoint stays in the local VNet.

## Consequences

- Lab deletion can never remove shared state (control DB, Key Vault, backups).
- Users never get direct SQL or RDP access; every interaction goes through allow-listed worker operations.
- Local development needs no network setup because the simulator replaces Azure (ADR 0003).
