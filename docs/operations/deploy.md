# Deployment

`infra/scripts/deploy.sh` deploys an environment end to end. It is idempotent: re-running updates resources in
place, keeps app registrations, and redeploys new images.

## Prerequisites

- Azure CLI ≥ 2.60, signed in with `az login` as a subscription **Owner** (the script creates a custom role and
  role assignments) who can register Entra apps.
- Docker (optional; without it images are built in the cloud with `az acr build`).
- `jq`, `python3`, `curl`.

## Usage

```bash
./infra/scripts/deploy.sh [--env dev] [--location eastus2] [--vm-size Standard_D2s_v7] \
                          [--budget 50] [--budget-email you@example.com] [--builder auto|docker|acr] \
                          [--skip-build] [--tag TAG] [--preflight-only] [--dry-run] [--yes]
```

State (resource names and public identifiers only — no secrets) is saved in `.deploy/<env>.json` and used by
`smoke-lab.sh` and re-runs.

## Phases

| Phase | What happens |
| --- | --- |
| preflight | Shows subscription, tenant, user, parameters, and cost estimate; asks for confirmation. Installs Bicep and the `containerapp` CLI extension if missing. |
| providers | Registers the 13 resource providers the app needs and waits for them. |
| quota | Verifies the VM size is offered and unrestricted in the region and that regional and family vCPU quota has room for a lab. Stops with a quota-request link otherwise. |
| entra | Creates/updates `sqllab-api-<env>` (scope `access_as_user`, app role `LabAdmin`, Azure CLI pre-authorized) and `sqllab-web-<env>` (SPA, PKCE, no secret), grants tenant consent, and assigns you `LabAdmin`. |
| platform | `infra/bicep/platform/main.bicep` at subscription scope: resource group, VNet (apps / private endpoints / labs subnets, lab NSG), Log Analytics + App Insights, Key Vault, storage, ACR, two managed identities, Azure SQL (Entra-only, private endpoint), Container Apps environment, the `SQL Lab Operator (<env>)` custom role, and the budget. |
| images | Builds `backend/Dockerfile.api` (API + Angular) and `backend/Dockerfile.worker`, pushes to ACR. |
| migrate | Deploys and runs the `caj-sqllab-migrate-<env>` job (`worker --migrate`) as the SQL Entra admin identity: applies migrations and creates the app identity's database user with read/write roles only. Hosted apps never migrate on startup. |
| apps | `infra/bicep/platform/apps.bicep`: API (external HTTPS, scales 0–2, sticky sessions for SignalR) and worker (exactly 1 replica). Configuration is identifiers only; both authenticate with the app managed identity. |
| finalize | Adds the app URL to the SPA redirect URIs and waits for `/health/ready`. |

## Updating

- Code change: `./infra/scripts/deploy.sh --yes` (new image tag, migrations, new revisions).
- Configuration-only change: `./infra/scripts/deploy.sh --skip-build --yes`.
- Lab template change: edit `infra/bicep/lab/main.bicep`, run `./infra/scripts/build-bicep.sh`, commit the
  regenerated `backend/src/SqlServerLab.Infrastructure/Azure/lab.json` (CI fails if it is stale), redeploy.

## Rollback

Redeploy a previous image that is still in ACR (`az acr repository show-tags -n <acr> --repository sqllab-api`):

```bash
./infra/scripts/deploy.sh --skip-build --tag <previous-tag> --yes
```

Migrations must stay backward compatible for one release so the previous API/worker can run against the migrated
schema.

## Verifying

```bash
./infra/scripts/smoke-lab.sh --env dev
```

Creates a lab through the API with your Azure CLI token, waits for `Ready` (SQL health check passes), checks there
is no public IP, deallocates, starts, deletes, and asserts that no resources tagged with the lab ID remain.

## CI

Pull requests lint and build the Bicep, check that the embedded `lab.json` is current, ShellCheck the scripts, and
build both container images. Real-Azure deployment is manual; to automate it, add a federated (OIDC) credential for
the repository and a protected workflow — never store long-lived secrets.
