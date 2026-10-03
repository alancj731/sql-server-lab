# Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| Lab stays `Requested` | The worker is not running. Start `backend/src/SqlServerLab.Worker` (or `scripts/dev.sh`). |
| UI shows "Live updates: disconnected" | API not reachable on :5080 or the dev-server proxy is not used. Pages still refetch on reload; the client retries every 5 s. |
| `database is locked` | Two processes share SQLite; WAL mode is enabled on migrate. Keep `Default Timeout=30` in the connection string. |
| API exits: `Authentication:Mode=Development is only allowed…` | Development auth is blocked outside `Development`/`Testing`. Use Entra settings for hosted environments. |
| `deploy.sh` stops at quota | Request vCPU quota for the VM family in the region (link printed), or use `--vm-size` / `--location`. |
| Migration job failed | `az containerapp job logs show -n caj-sqllab-migrate-<env> -g rg-sqllab-platform-<env> --container migrate`. A first run right after the platform deploy can fail while the private endpoint DNS propagates; re-run `deploy.sh --skip-build --yes`. |
| Sign-in loop / AADSTS50011 | The app URL is missing from the SPA redirect URIs; re-run `deploy.sh --skip-build --yes`. |
| 401 from the API in Azure | Token audience mismatch; check `Authentication__Audience` equals the API app's client ID and the SPA requests `api://<id>/access_as_user`. |
| Lab stuck in `Configuring` | The VM is up but SQL Server is still installing (first boot takes ~5 minutes). If it never becomes Ready, check `/var/log/sqllab-setup.log` via `az vm run-command invoke -g <rg> -n <vm> --command-id RunShellScript --scripts 'tail -50 /var/log/sqllab-setup.log'`. |
| Lab `Failed` with Quota/SkuNotAvailable | Regional capacity or quota; delete the lab, request quota, or redeploy with another `--vm-size`. |
| First request slow in Azure | The API scales to zero when idle (lean profile); the first request starts a replica in a few seconds. |
| API exits with `OptionsValidationException` | A configuration value is out of range (e.g. `LabLimits:MaxActiveLabsPerUser` must be 1–20). |
| 409 "Another operation is already in progress" | One mutating job per lab. Wait, or cancel the active job. |
| 409 "Quota exceeded" | Per-user active-lab limit reached; delete a lab. |
| Lab `Failed` with "image unavailable" | Simulated fault injection: the lab name contains `fail`. Delete it. |
| Job stuck `Running` after a crash | It is reclaimed when its lease expires (`Worker:LeaseSeconds`) and resumes the same operation. |
| Error with a correlation ID | Search API/worker logs for that `CorrelationId`; every log scope and audit row carries it. |
| `dotnet ef` not found | Add `~/.dotnet/tools` to `PATH` or install with `dotnet tool install -g dotnet-ef`. |
| Frontend types out of date | `cd frontend && npm run generate:api`. |
