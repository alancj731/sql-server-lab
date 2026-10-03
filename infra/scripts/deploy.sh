#!/usr/bin/env bash
# One-command deployment of SQL Server Lab to Azure. Idempotent: safe to re-run (it updates in place).
#
#   ./infra/scripts/deploy.sh --budget-email you@example.com            # full deploy (asks before changing anything)
#   ./infra/scripts/deploy.sh --dry-run                                  # show the plan, change nothing
#   ./infra/scripts/deploy.sh --preflight-only                           # providers + quota checks only
#
# Phases: preflight → providers → quota → entra → platform → images → migrate → apps → finalize
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ENV_NAME="dev"
LOCATION="eastus2"
VM_SIZE="Standard_D2s_v7"
SQL_LOCATION=""
SQL_FALLBACK_REGIONS=(canadacentral centralus westus3 eastus eastus2 northcentralus southcentralus westus2)
BUDGET=50
BUDGET_EMAIL=""
BUILDER="auto"
SKIP_BUILD=false
DRY_RUN=false
PREFLIGHT_ONLY=false
ASSUME_YES=false
IMAGE_TAG=""
EXPLICIT_TAG=false

usage() {
  cat <<EOF
Usage: $0 [options]
  --env NAME            Environment name, 2-8 lowercase letters/digits (default: $ENV_NAME)
  --location REGION     Azure region (default: $LOCATION)
  --vm-size SIZE        Lab VM size (default: $VM_SIZE)
  --sql-location REGION Control-database region (default: --location, or the nearest region allowing new SQL servers)
  --budget AMOUNT       Monthly budget alert in subscription currency, 0 to skip (default: $BUDGET)
  --budget-email EMAIL  Budget alert recipient (default: signed-in user's email if it looks like one)
  --builder auto|docker|acr  Image build: local Docker or ACR cloud build (default: auto = docker if available)
  --skip-build          Reuse the images from the last deployment
  --tag TAG             Image tag (default: git SHA or timestamp)
  --preflight-only      Register providers and check quota, then stop
  --dry-run             Print the plan without changing anything
  --yes                 Do not prompt for confirmation
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env) ENV_NAME="$2"; shift 2 ;;
    --location) LOCATION="$2"; shift 2 ;;
    --vm-size) VM_SIZE="$2"; shift 2 ;;
    --sql-location) SQL_LOCATION="$2"; shift 2 ;;
    --budget) BUDGET="$2"; shift 2 ;;
    --budget-email) BUDGET_EMAIL="$2"; shift 2 ;;
    --builder) BUILDER="$2"; shift 2 ;;
    --skip-build) SKIP_BUILD=true; shift ;;
    --tag) IMAGE_TAG="$2"; EXPLICIT_TAG=true; shift 2 ;;
    --preflight-only) PREFLIGHT_ONLY=true; shift ;;
    --dry-run) DRY_RUN=true; shift ;;
    --yes|-y) ASSUME_YES=true; shift ;;
    -h|--help) usage; exit 0 ;;
    *) usage; die "Unknown option: $1" ;;
  esac
done

[[ "$ENV_NAME" =~ ^[a-z0-9]{2,8}$ ]] || die "--env must be 2-8 lowercase letters or digits."
[[ "$BUDGET" =~ ^[0-9]+$ ]] || die "--budget must be a whole number."
[[ "$BUILDER" =~ ^(auto|docker|acr)$ ]] || die "--builder must be auto, docker, or acr."

PROVIDERS=(Microsoft.Compute Microsoft.Network Microsoft.Storage Microsoft.KeyVault Microsoft.SqlVirtualMachine
  Microsoft.Sql Microsoft.OperationalInsights Microsoft.Insights Microsoft.App Microsoft.ManagedIdentity
  Microsoft.ContainerRegistry Microsoft.Consumption Microsoft.OperationsManagement)
AZURE_CLI_APP_ID="04b07795-8ddb-461a-bbee-02f9e1bf7b46"
API_APP_NAME="sqllab-api-$ENV_NAME"
SPA_APP_NAME="sqllab-web-$ENV_NAME"
PLATFORM_RG="rg-sqllab-platform-$ENV_NAME"

# --------------------------------------------------------------------------------------------------------------
preflight() {
  phase "Preflight"
  require az jq python3
  az account show >/dev/null 2>&1 || die "Not signed in. Run: az login"
  az bicep version >/dev/null 2>&1 || { info "Installing Bicep CLI"; az bicep install >/dev/null; }
  az extension show --name containerapp >/dev/null 2>&1 || { info "Installing the containerapp CLI extension"; az extension add --name containerapp --yes >/dev/null; }

  SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
  SUBSCRIPTION_NAME="$(az account show --query name -o tsv)"
  TENANT_ID="$(az account show --query tenantId -o tsv)"
  USER_NAME="$(az account show --query user.name -o tsv)"
  USER_OBJECT_ID="$(az ad signed-in-user show --query id -o tsv)"
  [[ -z "$BUDGET_EMAIL" && "$USER_NAME" == *@*.* ]] && BUDGET_EMAIL="$USER_NAME"

  if [[ "$BUILDER" == "auto" ]]; then
    if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then BUILDER=docker; else BUILDER=acr; fi
  fi
  [[ -n "$IMAGE_TAG" ]] || IMAGE_TAG="$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null || date -u +%Y%m%d%H%M%S)"

  info "Subscription : $SUBSCRIPTION_NAME ($SUBSCRIPTION_ID)"
  info "Tenant       : $TENANT_ID"
  info "Signed in as : $USER_NAME"
  info "Environment  : $ENV_NAME in $LOCATION   (platform resource group $PLATFORM_RG)"
  info "Lab VM size  : $VM_SIZE"
  info "Budget alert : $([[ "$BUDGET" -gt 0 && -n "$BUDGET_EMAIL" ]] && echo "$BUDGET/month → $BUDGET_EMAIL" || echo none)"
  info "Images       : tag $IMAGE_TAG, built with $BUILDER$([[ "$SKIP_BUILD" == true ]] && echo ' (skipped)')"
  info "Estimated cost: platform ~\$35-60/month; each running lab ~\$0.10/hour plus ~\$10/month disk until deleted."

  if [[ "$DRY_RUN" == true ]]; then
    phase "Dry run — nothing will be changed. Planned phases:"
    info "providers  register: ${PROVIDERS[*]}"
    info "quota      require free vCPUs for $VM_SIZE in $LOCATION"
    info "entra      app registrations $API_APP_NAME (API, scope access_as_user, role LabAdmin) and $SPA_APP_NAME (SPA, PKCE)"
    info "platform   az deployment sub create  infra/bicep/platform/main.bicep"
    info "images     build + push sqllab-api:$IMAGE_TAG and sqllab-worker:$IMAGE_TAG"
    info "migrate    Container Apps job: worker --migrate as the SQL Entra admin identity"
    info "apps       az deployment group create infra/bicep/platform/apps.bicep (API + worker)"
    info "finalize   add the app URL to the SPA redirect URIs and wait for /health/ready"
    exit 0
  fi

  confirm "Deploy to this subscription? This creates billable resources." || die "Cancelled."
  state_merge "$ENV_NAME" <<<"$(jq -n --arg s "$SUBSCRIPTION_ID" --arg t "$TENANT_ID" --arg l "$LOCATION" \
    '{subscriptionId:$s, tenantId:$t, location:$l}')"
}

# --------------------------------------------------------------------------------------------------------------
providers() {
  phase "Resource providers"
  local pending=()
  for p in "${PROVIDERS[@]}"; do
    local state
    state="$(az provider show --namespace "$p" --query registrationState -o tsv 2>/dev/null || echo NotRegistered)"
    if [[ "$state" != "Registered" ]]; then
      az provider register --namespace "$p" >/dev/null
      pending+=("$p")
    fi
  done

  for p in "${pending[@]}"; do
    info "Waiting for $p …"
    wait_for 900 Registered az provider show --namespace "$p" --query registrationState -o tsv \
      || die "$p did not finish registering. Re-run the script in a few minutes."
  done
  ok "All ${#PROVIDERS[@]} providers registered"
}

# --------------------------------------------------------------------------------------------------------------
quota() {
  phase "Capacity and quota in $LOCATION"
  local sku family vcpus restriction
  sku="$(az vm list-skus --location "$LOCATION" --size "$VM_SIZE" --resource-type virtualMachines -o json \
    | jq -c --arg s "$VM_SIZE" '[.[] | select(.name == $s)][0]')"
  [[ "$sku" != "null" && -n "$sku" ]] || die "$VM_SIZE is not offered in $LOCATION. Choose another --vm-size or --location."

  restriction="$(jq -r '[.restrictions[]? | select(.type == "Location") | .reasonCode] | first // empty' <<<"$sku")"
  [[ -z "$restriction" ]] || die "$VM_SIZE is restricted for this subscription in $LOCATION ($restriction). Try --vm-size Standard_D2s_v4 or another region."

  family="$(jq -r '.family' <<<"$sku")"
  vcpus="$(jq -r '.capabilities[] | select(.name == "vCPUs") | .value' <<<"$sku")"

  local usage
  usage="$(az vm list-usage --location "$LOCATION" -o json)"
  check_quota() {
    local name="$1" label="$2"
    local limit current free
    limit="$(jq -r --arg n "$name" '[.[] | select(.name.value == $n)][0].limit // 0' <<<"$usage")"
    current="$(jq -r --arg n "$name" '[.[] | select(.name.value == $n)][0].currentValue // 0' <<<"$usage")"
    free=$((limit - current))
    if ((free < vcpus)); then
      warn "$label: $current of $limit vCPUs used; a lab needs $vcpus."
      die "Not enough quota. Request more at https://portal.azure.com/#view/Microsoft_Azure_Capacity/QuotaMenuBlade/~/myQuotas (Compute, $LOCATION, $label), then re-run."
    fi
    ok "$label: $free vCPUs free ($current/$limit used) — room for $((free / vcpus)) running lab(s)"
  }
  check_quota cores "Total Regional vCPUs"
  check_quota "$family" "$family"
  sql_region
}

sql_available() {
  [[ "$(az rest --method get --query status -o tsv \
    --uri "https://management.azure.com/subscriptions/$SUBSCRIPTION_ID/providers/Microsoft.Sql/locations/$1/capabilities?api-version=2021-11-01" 2>/dev/null)" == "Available" ]]
}

# Some subscriptions (notably Free Trial) cannot create SQL servers in busy regions. The control database can live in
# another region: its private endpoint stays in this VNet, so traffic remains private.
sql_region() {
  if [[ -n "$SQL_LOCATION" ]]; then
    sql_available "$SQL_LOCATION" || die "Azure SQL provisioning is not available in $SQL_LOCATION for this subscription."
  elif sql_available "$LOCATION"; then
    SQL_LOCATION="$LOCATION"
  else
    for candidate in "${SQL_FALLBACK_REGIONS[@]}"; do
      if [[ "$candidate" != "$LOCATION" ]] && sql_available "$candidate"; then SQL_LOCATION="$candidate"; break; fi
    done
    [[ -n "$SQL_LOCATION" ]] || die "No nearby region accepts new Azure SQL servers for this subscription. Pass --sql-location."
    warn "Azure SQL provisioning is restricted in $LOCATION; the control database will be created in $SQL_LOCATION (private endpoint in $LOCATION)."
  fi
  ok "Azure SQL control database region: $SQL_LOCATION"
}

# --------------------------------------------------------------------------------------------------------------
graph_patch() { az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$1" --headers "Content-Type=application/json" --body "$2" >/dev/null; }

ensure_app() {
  local name="$1" app_id
  app_id="$(az ad app list --display-name "$name" --query "[0].appId" -o tsv)"
  if [[ -z "$app_id" ]]; then
    app_id="$(az ad app create --display-name "$name" --sign-in-audience AzureADMyOrg --query appId -o tsv)"
    info "Created app registration $name" >&2
  fi
  az ad sp show --id "$app_id" >/dev/null 2>&1 || az ad sp create --id "$app_id" >/dev/null
  printf '%s' "$app_id"
}

entra() {
  phase "Entra ID app registrations"
  API_APP_ID="$(ensure_app "$API_APP_NAME")"
  local api_obj scope_id role_id
  api_obj="$(az ad app show --id "$API_APP_ID" --query id -o tsv)"
  scope_id="$(az ad app show --id "$API_APP_ID" --query "api.oauth2PermissionScopes[?value=='access_as_user'].id | [0]" -o tsv)"
  role_id="$(az ad app show --id "$API_APP_ID" --query "appRoles[?value=='LabAdmin'].id | [0]" -o tsv)"
  [[ -n "$scope_id" ]] || scope_id="$(python3 -c 'import uuid; print(uuid.uuid4())')"
  [[ -n "$role_id" ]] || role_id="$(python3 -c 'import uuid; print(uuid.uuid4())')"

  graph_patch "$api_obj" "$(jq -n --arg uri "api://$API_APP_ID" --arg sid "$scope_id" --arg rid "$role_id" '{
    identifierUris: [$uri],
    api: {
      requestedAccessTokenVersion: 2,
      oauth2PermissionScopes: [{
        id: $sid, value: "access_as_user", type: "User", isEnabled: true,
        adminConsentDisplayName: "Use SQL Server Lab", adminConsentDescription: "Create and manage your SQL Server labs.",
        userConsentDisplayName: "Use SQL Server Lab", userConsentDescription: "Create and manage your SQL Server labs."
      }]
    },
    appRoles: [{
      id: $rid, value: "LabAdmin", displayName: "Lab administrator", isEnabled: true,
      description: "Can see all labs and run maintenance operations.", allowedMemberTypes: ["User"]
    }]
  }')"
  # Pre-authorize the Azure CLI so smoke-lab.sh can get a token without a consent prompt.
  graph_patch "$api_obj" "$(jq -n --arg sid "$scope_id" --arg cli "$AZURE_CLI_APP_ID" \
    '{api: {preAuthorizedApplications: [{appId: $cli, delegatedPermissionIds: [$sid]}]}}')"
  ok "$API_APP_NAME ($API_APP_ID): scope access_as_user, role LabAdmin"

  SPA_APP_ID="$(ensure_app "$SPA_APP_NAME")"
  local spa_obj existing_uris
  spa_obj="$(az ad app show --id "$SPA_APP_ID" --query id -o tsv)"
  existing_uris="$(az ad app show --id "$SPA_APP_ID" --query "spa.redirectUris" -o json)"
  graph_patch "$spa_obj" "$(jq -n --argjson uris "$existing_uris" --arg api "$API_APP_ID" --arg sid "$scope_id" '{
    spa: { redirectUris: (($uris // []) + ["http://localhost:4200/"] | unique) },
    requiredResourceAccess: [{ resourceAppId: $api, resourceAccess: [{ id: $sid, type: "Scope" }] }]
  }')"
  # Tenant-wide consent so users are not prompted (you are the tenant admin).
  az ad app permission grant --id "$SPA_APP_ID" --api "$API_APP_ID" --scope access_as_user >/dev/null 2>&1 \
    || warn "Could not grant admin consent; users will be asked to consent on first sign-in."
  ok "$SPA_APP_NAME ($SPA_APP_ID): single-page app with PKCE, no client secret"

  local api_sp
  api_sp="$(az ad sp show --id "$API_APP_ID" --query id -o tsv)"
  if [[ -z "$(az rest --method GET --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$api_sp/appRoleAssignedTo" \
      --query "value[?principalId=='$USER_OBJECT_ID' && appRoleId=='$role_id'].id | [0]" -o tsv)" ]]; then
    az rest --method POST --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$api_sp/appRoleAssignedTo" \
      --headers "Content-Type=application/json" \
      --body "$(jq -n --arg p "$USER_OBJECT_ID" --arg r "$api_sp" --arg a "$role_id" '{principalId:$p, resourceId:$r, appRoleId:$a}')" >/dev/null
  fi
  ok "Assigned LabAdmin to $USER_NAME"

  state_merge "$ENV_NAME" <<<"$(jq -n --arg a "$API_APP_ID" --arg s "$SPA_APP_ID" '{apiAppId:$a, spaAppId:$s}')"
}

# --------------------------------------------------------------------------------------------------------------
platform() {
  phase "Shared platform (Bicep) — first run takes ~10-15 minutes"
  local outputs
  outputs="$(az deployment sub create \
    --name "sqllab-platform-$ENV_NAME" \
    --location "$LOCATION" \
    --template-file "$REPO_ROOT/infra/bicep/platform/main.bicep" \
    --parameters env="$ENV_NAME" location="$LOCATION" sqlLocation="$SQL_LOCATION" \
    --query properties.outputs -o json)"
  state_merge "$ENV_NAME" <<<"$(jq 'with_entries(.value = .value.value) | del(.appInsightsConnectionString)' <<<"$outputs")"
  APPI_CONNECTION="$(jq -r '.appInsightsConnectionString.value' <<<"$outputs")"
  ok "Platform deployed to $(state_get "$ENV_NAME" resourceGroupName)"
  budget
}

# Budgets are created here rather than in Bicep: some offers (e.g. Free Trial) do not support them, and that must
# not block the deployment.
budget() {
  [[ "$BUDGET" -gt 0 && -n "$BUDGET_EMAIL" ]] || return 0
  local body
  body="$(jq -n --argjson amount "$BUDGET" --arg email "$BUDGET_EMAIL" --arg start "$(date -u +%Y-%m-01T00:00:00Z)" '{
    properties: {
      category: "Cost", amount: $amount, timeGrain: "Monthly", timePeriod: { startDate: $start },
      notifications: {
        actual80: { enabled: true, operator: "GreaterThanOrEqualTo", threshold: 80, thresholdType: "Actual", contactEmails: [$email] },
        actual100: { enabled: true, operator: "GreaterThanOrEqualTo", threshold: 100, thresholdType: "Actual", contactEmails: [$email] },
        forecast100: { enabled: true, operator: "GreaterThanOrEqualTo", threshold: 100, thresholdType: "Forecasted", contactEmails: [$email] }
      }
    }
  }')"
  if az rest --method PUT \
      --uri "https://management.azure.com/subscriptions/$SUBSCRIPTION_ID/providers/Microsoft.Consumption/budgets/budget-sqllab-$ENV_NAME?api-version=2023-11-01" \
      --headers "Content-Type=application/json" --body "$body" >/dev/null 2>"$STATE_DIR/budget.err"; then
    ok "Budget alert: $BUDGET/month → $BUDGET_EMAIL (80%, 100%, forecast 100%)"
  else
    warn "Budget not created ($(head -c 200 "$STATE_DIR/budget.err")). Set one in the portal: Cost Management → Budgets."
  fi
}

# --------------------------------------------------------------------------------------------------------------
images() {
  local acr server
  acr="$(state_get "$ENV_NAME" acrName)"
  server="$(state_get "$ENV_NAME" acrLoginServer)"
  if [[ "$SKIP_BUILD" == true ]]; then
    [[ "$EXPLICIT_TAG" == true ]] || IMAGE_TAG="$(state_get "$ENV_NAME" imageTag)"
    phase "Images (skipped; using tag $IMAGE_TAG)"
    az acr repository show --name "$acr" --image "sqllab-api:$IMAGE_TAG" >/dev/null 2>&1 || die "Image tag $IMAGE_TAG is not in $acr."
    state_merge "$ENV_NAME" <<<"$(jq -n --arg t "$IMAGE_TAG" '{imageTag:$t}')"
    return
  fi

  phase "Images ($BUILDER build, tag $IMAGE_TAG)"
  for image in api worker; do
    if [[ "$BUILDER" == "docker" ]]; then
      az acr login --name "$acr" >/dev/null
      docker build -f "$REPO_ROOT/backend/Dockerfile.$image" -t "$server/sqllab-$image:$IMAGE_TAG" "$REPO_ROOT" >/dev/null
      docker push "$server/sqllab-$image:$IMAGE_TAG" >/dev/null
    else
      az acr build --registry "$acr" --image "sqllab-$image:$IMAGE_TAG" --file "$REPO_ROOT/backend/Dockerfile.$image" "$REPO_ROOT" --no-logs >/dev/null
    fi
    ok "$server/sqllab-$image:$IMAGE_TAG"
  done
  state_merge "$ENV_NAME" <<<"$(jq -n --arg t "$IMAGE_TAG" '{imageTag:$t}')"
}

s() { state_get "$ENV_NAME" "$1"; }

# --------------------------------------------------------------------------------------------------------------
migrate() {
  phase "Control database migration"
  local rg job execution status
  rg="$(s resourceGroupName)"
  job="$(az deployment group create --resource-group "$rg" --name "sqllab-migrate" \
    --template-file "$REPO_ROOT/infra/bicep/platform/migrate.bicep" \
    --parameters env="$ENV_NAME" location="$LOCATION" \
      containerAppsEnvironmentId="$(s containerAppsEnvironmentId)" acrLoginServer="$(s acrLoginServer)" \
      workerImage="$(s acrLoginServer)/sqllab-worker:$IMAGE_TAG" \
      migratorIdentityId="$(s migratorIdentityId)" migratorIdentityClientId="$(s migratorIdentityClientId)" \
      appIdentityName="$(s appIdentityName)" appIdentityClientId="$(s appIdentityClientId)" sqlServerFqdn="$(s sqlServerFqdn)" sqlDatabaseName="$(s sqlDatabaseName)" \
    --query properties.outputs.jobName.value -o tsv)"

  execution="$(az containerapp job start --name "$job" --resource-group "$rg" --query name -o tsv)"
  info "Running $job ($execution) …"
  local deadline=$((SECONDS + 1200))
  while ((SECONDS < deadline)); do
    status="$(az containerapp job execution show --name "$job" --resource-group "$rg" --job-execution-name "$execution" \
      --query properties.status -o tsv 2>/dev/null || echo Unknown)"
    case "$status" in
      Succeeded) ok "Migrations applied; app identity granted db_datareader/db_datawriter"; return ;;
      Failed|Stopped|Degraded)
        die "Migration job $status. Logs: az containerapp job logs show -n $job -g $rg --execution $execution --container migrate" ;;
    esac
    sleep 15
  done
  die "Migration job did not finish in 20 minutes."
}

# --------------------------------------------------------------------------------------------------------------
apps() {
  phase "API and worker container apps"
  local rg
  rg="$(s resourceGroupName)"
  [[ -n "${APPI_CONNECTION:-}" ]] || APPI_CONNECTION="$(az monitor app-insights component show --app "appi-sqllab-$ENV_NAME" -g "$rg" --query connectionString -o tsv)"
  API_FQDN="$(az deployment group create --resource-group "$rg" --name "sqllab-apps" \
    --template-file "$REPO_ROOT/infra/bicep/platform/apps.bicep" \
    --parameters env="$ENV_NAME" location="$LOCATION" \
      containerAppsEnvironmentId="$(s containerAppsEnvironmentId)" acrLoginServer="$(s acrLoginServer)" \
      apiImage="$(s acrLoginServer)/sqllab-api:$IMAGE_TAG" workerImage="$(s acrLoginServer)/sqllab-worker:$IMAGE_TAG" \
      appIdentityId="$(s appIdentityId)" appIdentityClientId="$(s appIdentityClientId)" \
      sqlServerFqdn="$(s sqlServerFqdn)" sqlDatabaseName="$(s sqlDatabaseName)" keyVaultUri="$(s keyVaultUri)" \
      labSubnetId="$(s labSubnetId)" vmSize="$VM_SIZE" tenantId="$(s tenantId)" \
      apiClientId="$(s apiAppId)" spaClientId="$(s spaAppId)" appInsightsConnectionString="$APPI_CONNECTION" \
    --query properties.outputs.apiFqdn.value -o tsv)"
  state_merge "$ENV_NAME" <<<"$(jq -n --arg f "$API_FQDN" '{apiFqdn:$f}')"
  ok "https://$API_FQDN"
}

# --------------------------------------------------------------------------------------------------------------
finalize() {
  phase "Finalize"
  local spa_obj uris url="https://$API_FQDN/"
  spa_obj="$(az ad app show --id "$(s spaAppId)" --query id -o tsv)"
  uris="$(az ad app show --id "$(s spaAppId)" --query "spa.redirectUris" -o json)"
  graph_patch "$spa_obj" "$(jq -n --argjson uris "$uris" --arg u "$url" '{spa: {redirectUris: (($uris // []) + [$u] | unique)}}')"
  ok "Sign-in redirect URI registered: $url"

  info "Waiting for the API to report healthy (first start can take a few minutes) …"
  local deadline=$((SECONDS + 600)) code=''
  while ((SECONDS < deadline)); do
    code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 30 "${url}health/ready" || true)"
    [[ "$code" == "200" ]] && break
    sleep 10
  done
  [[ "$code" == "200" ]] || die "API not healthy (HTTP $code). Logs: az containerapp logs show -n ca-sqllab-api-$ENV_NAME -g $(s resourceGroupName) --tail 100"
  ok "API healthy"

  phase "Done"
  info "App:        $url"
  info "Sign in as: $USER_NAME (LabAdmin)"
  info "Smoke test: ./infra/scripts/smoke-lab.sh --env $ENV_NAME   (creates and deletes one real lab, ~20-30 min)"
  info "Tear down:  ./infra/scripts/destroy.sh --env $ENV_NAME"
}

preflight
providers
quota
[[ "$PREFLIGHT_ONLY" == true ]] && { phase "Preflight complete — stopping (--preflight-only)"; exit 0; }
entra
platform
images
migrate
apps
finalize
