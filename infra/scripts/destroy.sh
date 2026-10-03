#!/usr/bin/env bash
# Removes everything deploy.sh created for an environment: every lab resource group, the platform resource group,
# the soft-deleted Key Vault, the custom role, the budget, and (optionally) the Entra app registrations.
#
#   ./infra/scripts/destroy.sh --env dev [--include-entra] [--yes]
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

ENV_NAME="dev"
INCLUDE_ENTRA=false
ASSUME_YES=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env) ENV_NAME="$2"; shift 2 ;;
    --include-entra) INCLUDE_ENTRA=true; shift ;;
    --yes|-y) ASSUME_YES=true; shift ;;
    -h|--help) sed -n '2,6p' "$0"; exit 0 ;;
    *) die "Unknown option: $1" ;;
  esac
done
[[ "$ENV_NAME" =~ ^[a-z0-9]{2,8}$ ]] || die "--env must be 2-8 lowercase letters or digits."
require az jq

PLATFORM_RG="rg-sqllab-platform-$ENV_NAME"
phase "Destroy environment '$ENV_NAME' in $(az account show --query name -o tsv)"

mapfile -t LAB_RGS < <(az group list --tag "environment=$ENV_NAME" --query "[?starts_with(name, 'rg-sqllab-') && name != '$PLATFORM_RG'].name" -o tsv)
info "Lab resource groups: ${#LAB_RGS[@]}"
for rg in "${LAB_RGS[@]}"; do info "  $rg"; done
info "Platform resource group: $PLATFORM_RG$(az group exists -n "$PLATFORM_RG" | grep -q true || echo ' (not found)')"
[[ "$INCLUDE_ENTRA" == true ]] && info "Entra apps: sqllab-api-$ENV_NAME, sqllab-web-$ENV_NAME"

if [[ "$ASSUME_YES" != true ]]; then
  read -r -p "    Type the environment name ($ENV_NAME) to permanently delete it: " typed
  [[ "$typed" == "$ENV_NAME" ]] || die "Confirmation did not match; nothing was deleted."
fi

KV_NAME="$(az keyvault list -g "$PLATFORM_RG" --query "[0].name" -o tsv 2>/dev/null || true)"
LOCATION="$(az group show -n "$PLATFORM_RG" --query location -o tsv 2>/dev/null || true)"

phase "Deleting lab resource groups"
for rg in "${LAB_RGS[@]}"; do az group delete -n "$rg" --yes --no-wait; done
for rg in "${LAB_RGS[@]}"; do
  wait_for 1800 false az group exists -n "$rg" || warn "$rg is still deleting"
  ok "$rg"
done

phase "Deleting platform (~10 minutes)"
if [[ "$(az group exists -n "$PLATFORM_RG")" == "true" ]]; then
  az group delete -n "$PLATFORM_RG" --yes
fi
ok "$PLATFORM_RG"

if [[ -n "$KV_NAME" ]]; then
  if az keyvault purge --name "$KV_NAME" >/dev/null 2>&1; then ok "Purged Key Vault $KV_NAME"; else warn "Key Vault $KV_NAME not purged (already gone?)"; fi
fi

# Azure auto-creates NetworkWatcher_<region> with the first VNet in a region. Remove it only when no VNets remain there.
if [[ -n "$LOCATION" && "$(az network vnet list --query "length([?location=='$LOCATION'])" -o tsv)" == "0" ]]; then
  if az network watcher configure --locations "$LOCATION" --enabled false -g NetworkWatcherRG -o none >/dev/null 2>&1; then
    ok "Removed NetworkWatcher_$LOCATION"
  fi
  [[ "$(az resource list -g NetworkWatcherRG --query 'length(@)' -o tsv 2>/dev/null || echo 1)" == "0" ]] \
    && az group delete -n NetworkWatcherRG --yes --no-wait && ok "Removed empty NetworkWatcherRG"
fi

ROLE="SQL Lab Operator ($ENV_NAME)"
az role assignment delete --role "$ROLE" >/dev/null 2>&1 || true
if az role definition delete --name "$ROLE" >/dev/null 2>&1; then ok "Removed custom role"; else info "Custom role already removed"; fi
if az consumption budget delete --budget-name "budget-sqllab-$ENV_NAME" >/dev/null 2>&1; then ok "Removed budget"; else info "No budget to remove"; fi
az deployment sub delete --name "sqllab-platform-$ENV_NAME" >/dev/null 2>&1 || true

if [[ "$INCLUDE_ENTRA" == true ]]; then
  for app in "sqllab-api-$ENV_NAME" "sqllab-web-$ENV_NAME"; do
    id="$(az ad app list --display-name "$app" --query "[0].appId" -o tsv)"
    [[ -n "$id" ]] && az ad app delete --id "$id" && ok "Deleted app registration $app"
  done
fi

rm -f "$(state_file "$ENV_NAME")"
phase "Environment '$ENV_NAME' removed"
