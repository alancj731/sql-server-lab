# Orphan cleanup

## Local

Delete `.data/` (and `frontend/.e2e-data/` for e2e runs). Nothing exists outside the SQLite file.

## Azure

Every lab resource group is named `rg-sqllab-{name}-{id8}` and tagged `labId`, `ownerId`, `environment`,
`createdAt`, `expiresAt`, `app=sql-server-lab`.

### Automatic

- `ExpiryService` (worker, every 30 s) queues deletion for labs past `expiresAt`.
- `OrphanReconciler` (worker, Azure mode, every 10 min) lists resource groups tagged `environment=<env>` that match
  the lab naming pattern and deletes those older than 1 hour whose `labId` is missing from the control database or
  whose lab is already `Deleted`. Each deletion is audited as `lab.orphan-deleted`. Labs in `Failed` are left for
  the owner to delete (their resources are useful for diagnosis).
- Deleting a lab also deletes and purges its two Key Vault secrets.

### Manual check

```bash
az group list --tag environment=dev --query "[?starts_with(name,'rg-sqllab-') && name!='rg-sqllab-platform-dev'].{name:name, labId:tags.labId, expires:tags.expiresAt}" -o table
az resource list --tag labId=<lab-id> -o table          # should be empty after deletion
az keyvault secret list --vault-name <kv> -o table       # only secrets for existing labs
```

### Remove an entire environment

```bash
./infra/scripts/destroy.sh --env dev                 # labs, platform, Key Vault purge, custom role, budget
./infra/scripts/destroy.sh --env dev --include-entra  # also the two app registrations
```
